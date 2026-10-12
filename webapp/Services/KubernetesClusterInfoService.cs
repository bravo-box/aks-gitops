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

    public async Task<string> GetApiServerVersionAsync()
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        var tokenPath = Path.Combine(ServiceAccountDir, "token");
        var caCertPath = Path.Combine(ServiceAccountDir, "ca.crt");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port) || !File.Exists(tokenPath))
        {
            return "not running in-cluster";
        }

        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
        using var caCert = X509Certificate2.CreateFromPemFile(caCertPath);

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
        var gitVersion = doc.RootElement.GetProperty("gitVersion").GetString();
        var platform = doc.RootElement.TryGetProperty("platform", out var p) ? p.GetString() : null;

        return platform is null ? gitVersion ?? "unknown" : $"{gitVersion} ({platform})";
    }
}
