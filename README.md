# AKS GitOps Quickstart

This repository contains a simple SQL Server implementation for Kubernetes, designed to demonstrate a GitOps workflow. The philosophy here is the git is the source of truth for your application / deployment needs. You manage your state and deployment in a single place and then monitor to ensure there is no configuration drift.

In this quickstart, Azure Kubernetes Service (AKS) is used as the development and validation environment before promotion to an edge environment running Azure Local with Arc-enabled Kubernetes.

In this scenario the assumption is that SQL Server must run at the edge, where stability is critical. To reduce risk, changes are first built and tested in AKS, then pushed to the edge only after validation and approval within git.

The GitHub repository enforces branch protection on `main`, so all changes must go through a pull request and approval process before merge.

GitOps is configured so Azure watches the `dev` branch and the edge environment watches the `main` branch. The flow is: commit to `dev`, validate in AKS, open a pull request, merge to `main`, and automatically update the edge.

### Steps in quickstart

1. Build and validate the deployment in git, use `kustomize` to create resources.
2. Test it in AKS.
3. Promote the same manifests to a live Azure Local Arc-enabled kubernetes environment.

Each folder below is its own `kustomization.yaml` sync unit, which makes promotion predictable and repeatable, and lets Flux apply them in dependency order (namespace → SQL Server / ACR pull secret → webapp).

## What Is In This Repo

- `namespace-flux/`: Creates the `sql-server` namespace (`namespace.yaml`). Every other Kustomization depends on this one.
- `sql-server-flux/`: Deploys SQL Server.
  - `sql-secret.yaml`: Provides the SQL Server SA password secret.
  - `statefulset.yaml`: Deploys SQL Server 2025 as a `StatefulSet` with persistent storage.
  - `service.yaml`: Exposes SQL Server on port 1433 as a `ClusterIP` service (no external IP), providing only the in-cluster `mssql` DNS name the webapp connects to.
- `acr-pull-flux/`: Provides `acr-pull-secret.yaml`, the `dockerconfigjson` Secret the webapp uses to pull its image from ACR. See [Web App](#web-app) below for how to populate it — the committed file is a placeholder, not real credentials.
- `web-flux/`: Deploys the webapp.
  - `webapp-deployment.yaml` / `webapp-service.yaml`: Deploys the webapp container and exposes it via a `LoadBalancer` service on port 80.
  - `kustomization.yaml`: Includes an `images:` transformer so you can point at your registry without editing the Deployment directly.
- `webapp/`: Source for a simple .NET 10 Blazor Server app that connects to the SQL Server instance and reports connection status. See [Web App](#web-app) below for build instructions.

## Web App

The `webapp/` folder contains a minimal Blazor Server app (`Microsoft.Data.SqlClient`) that opens a connection to the `mssql` service and runs `SELECT @@VERSION`, showing success/failure on the home page.

The SA password is never duplicated in a connection string secret — the app reads `Db__Server`, `Db__Database`, `Db__User` as plain env vars and `Db__Password` via `secretKeyRef` from the existing `mssql-secret`, then builds the connection string in code.

### Build and push the image

```bash
cd webapp
docker build --platform linux/amd64 -t betanet.azurecr.us/webapp:latest .
docker push betanet.azurecr.us/webapp:latest
```

> **Note:** always pass `--platform linux/amd64` when building on Apple Silicon (M-series) Macs. Without it, Docker builds for your Mac's native `arm64`, which will fail to run on AKS's `amd64` nodes (`exec format error`). Docker Desktop's bundled QEMU emulation builds `linux/amd64` correctly, just slower than a native build.

Update the `newName` value under `images:` in `web-flux/kustomization.yaml` to point at your registry/image.

### ACR pull secret

`acr-pull-flux/acr-pull-secret.yaml` is committed as a placeholder only — never commit real ACR credentials to git. Two options:

- **Recommended:** skip the secret entirely and attach ACR to AKS via managed identity, which grants the cluster's kubelet identity `AcrPull` with nothing to store or rotate:
  ```bash
  az aks update --name <aks-cluster-name> --resource-group <rg> --attach-acr <acr-name>
  ```
  Then remove `imagePullSecrets` from `web-flux/webapp-deployment.yaml` and drop `acr-pull-flux` from your GitOps config.
- **Otherwise:** generate and apply the real secret directly against the cluster (not through git):
  ```bash
  kubectl create secret docker-registry acr-pull-secret \
    --namespace sql-server \
    --docker-server=betanet.azurecr.us \
    --docker-username=<ACR_USERNAME> \
    --docker-password=<ACR_PASSWORD_OR_TOKEN> \
    --dry-run=client -o yaml | kubectl apply -f -
  ```
  If you do this, remove `acr-pull-flux` from GitOps sync so it doesn't overwrite the real secret with the placeholder.

`imagePullSecrets` only resolves Secrets in the same namespace as the Pod — there's no cross-namespace reference, which is why the placeholder is scoped to `sql-server` to match where the webapp runs.

## Prerequisites

- A git repo, bucket or blob for the artifacts
- Azure Kubernetes Service and Arc-Enabled Kubernetes on Azure Local deployed
- Access to the AKS and Azure Local Kubernetes to configure the GitOps Kustomization.

## Deployment

1) Fork this repo or pull the code to a repo of your choice, or place in a bucket or Azure Blob.
2) Create a new branch called `dev`
3) In AKS under `settings` select `GitOps` then `Create`
4) Enter your `Configuration Name` enter the name of the `Namepace` for this example we will use `sql-server`, set the scope to `Namespace`, select **Next**
5) In the source select your source that you used for your code, for this quickstart we will use `Git Repository`, enter the url and set the reference type. We will use `Branch` and then set the branch name, as this is AKS and our dev environment set the branch name to `dev`. Enter the connection details to your Repository type. Leave the remaining Sync Configuration as is.
6) Create one GitOps Kustomization per folder, each with its **Path** pointed at that folder (e.g. `/namespace-flux`, `/sql-server-flux`, `/acr-pull-flux`, `/web-flux`) and set **Depends On** so they apply in order:
   - `namespace-flux` — no dependency, applies first.
   - `sql-server-flux` and `acr-pull-flux` — depend on `namespace-flux`.
   - `web-flux` — depends on `sql-server-flux` and `acr-pull-flux`, so the webapp only deploys once SQL Server and the pull secret are ready.
7) Leave the sync settings for now, select `Prune` and `Force`. Prune will remove objects if removed from the code, and force will recreate objects if needed.

NOTE: now that this has been created you will see the GitOps status screen with the Kustomizations and a compliance and State status.

If you go into the configuration and take a look at the Configuration Objects you should see one `GitRepository` object plus one `Kustomization` object per folder above. All should be in a Compliant State ![check](https://img.shields.io/badge/-✔-brightgreen?style=flat-circle).

### Check rollout and runtime status

```bash
kubectl get ns sql-server
kubectl -n sql-server get pods
kubectl -n sql-server get statefulset mssql
kubectl -n sql-server get pvc
```

Verify the service (no `EXTERNAL-IP` is expected — SQL Server is only reachable in-cluster, via the webapp):

```bash
kubectl -n sql-server get svc mssql
```

To test your Kustomization is working, within AKS you can go ahead make some changes to the YAML. At the next run you will notice that the kustomization objects turn to a non-compliant status ![fail](https://img.shields.io/badge/-✖-red?style=flat-circle).

### Move from Dev to Prod

In your Git repository do a pull request from the `dev` branch to your `main` branch. Depending on how you have setup your repo you may need a 2nd person to approve if you have rules in place. Once the branches have been merged, do not delete the dev branch as this will break AKS GitOps configuration.

### Azure Local AKS

After validation in AKS we can now go ahead and run through the same steps as the **DEPLOYMENT** above to create the Kustomization in the Azure Local. The one modication will be Step 5, use the `main` branch and not the `dev` branch.

#### Validate status on the Azure Local

NOTE: you will need to connect using your K8s proxy connection. See the Arc-enabled scripts README.MD [here](https://github.com/bravo-box/azure-scripts/blob/main/azure-local/arc-enabled-kubernetes/readme.md)

```bash
kubectl -n sql-server get pods
kubectl -n sql-server get statefulset mssql
kubectl -n sql-server get pvc
kubectl -n sql-server get svc mssql
```

## Notes

This is the key GitOps flow for this quickstart: test in Azure AKS first, then deploy live to Azure Local AKS using the same kustomization-driven manifests.


- The current secret in `sql-server-flux/sql-secret.yaml` is for demo use. Replace it with a strong password and a secure secret-management workflow for real environments.
- `sql-server-flux/service.yaml` is intentionally `ClusterIP`, not `LoadBalancer` — SQL Server should not be reachable outside the cluster; the webapp is the only sanctioned access path.