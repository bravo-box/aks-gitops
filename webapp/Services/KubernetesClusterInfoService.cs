using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace WebApp.Services;

// Every pod gets KUBERNETES_SERVICE_HOST/PORT plus a mounted ServiceAccount token/CA cert
// automatically, so the real API server can be queried at runtime instead of hardcoding
// a per-cluster name that goes stale when the manifest is promoted to another cluster.
public class KubernetesClusterInfoService
{
    private const string ServiceAccountDir = "/var/run/secrets/kubernetes.io/serviceaccount";

    public async Task<ApiServerInfo> GetApiServerVersionAsync()
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        var tokenPath = Path.Combine(ServiceAccountDir, "token");
        var caCertPath = Path.Combine(ServiceAccountDir, "ca.crt");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port) || !File.Exists(tokenPath))
        {
            return new ApiServerInfo("not running in-cluster", null, null, null);
        }

        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
        // ca.crt has no private key — CreateFromPemFile expects one and throws; use the cert-only loader.
        using var caCert = X509CertificateLoader.LoadCertificateFromFile(caCertPath);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null)
                {
                    return false;
                }

                chain.ChainPolicy.ExtraStore.Add(caCert);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                return chain.Build(cert)
                    && chain.ChainElements.Cast<X509ChainElement>().Any(e => e.Certificate.Thumbprint == caCert.Thumbprint);
            }
        };

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync($"https://{host}:{port}/version");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        string? Get(string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

        return new ApiServerInfo(
            Get("gitVersion") ?? "unknown",
            Get("platform"),
            Get("buildDate"),
            Get("goVersion"));
    }
}

public record ApiServerInfo(string GitVersion, string? Platform, string? BuildDate, string? GoVersion);
