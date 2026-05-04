# AKS GitOps Quickstart

This repository contains a simple SQL Server implementation for Kubernetes, designed to demonstrate a GitOps workflow. This general 

In this quickstart, Azure Kubernetes Service (AKS) is used as the development and validation environment before promotion to an edge environment running Azure Local with Arc-enabled Kubernetes.

In this scenario the assumption is that SQL Server must run at the edge, where stability is critical. To reduce risk, changes are first built and tested in AKS, then pushed to the edge only after validation and approval within git.

The GitHub repository enforces branch protection on `main`, so all changes must go through a pull request and approval process before merge.

GitOps is configured so Azure watches the `dev` branch and the edge environment watches the `main` branch. The flow is: commit to `dev`, validate in AKS, open a pull request, merge to `main`, and automatically update the edge.

### Steps in quickstart

1. Build and validate the deployment in git, use `kustomize` to create resources.
2. Test it in AKS.
3. Promote the same manifests to a live Azure Local Arc-enabled kubernetes environment.

The same `kustomization.yaml` is the core deployment unit across environments, which makes promotion predictable and repeatable.

## What Is In This Repo

- `namespace.yaml`: Creates the `sql-server` namespace.
- `secret.yaml`: Provides the SQL Server SA password secret.
- `statefulset.yaml`: Deploys SQL Server 2022 as a `StatefulSet` with persistent storage.
- `service.yaml`: Exposes SQL Server on port 1433 with a `LoadBalancer` service. **Note this is NOT part of the kustomization**.
- `kustomization.yaml`: Defines which manifests are included in the deployment set as part of the GitOps process.

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
6) Here we create a new Kustomization. Give it a `Instance Name` and the path to the artifacts, eg: `blank` if in the root or `/sql` if in a sql folder.
7) Leave the sync settings for now, select `Prune` and `Force`. Prune will remove objects if removed from the code, and force will recreate objects if needed. We dont have any Depends on Kustomizations, however in future deployments you can have dependencies in place to ensure applications work correctly.

NOTE: now that this has been created you will see the GitOps status screen with the Kustomizations and a compliance and State status.

If you go into the configuration and take a look at the Configuration Objects you should see 2 objects: `sql-server` and `sql-server-dev`, one being a GitRepository and the other the Kustomization. Both should be in a Compliant State ![check](https://img.shields.io/badge/-✔-brightgreen?style=flat-circle).

### Check rollout and runtime status

```bash
kubectl get ns sql-server
kubectl -n sql-server get pods
kubectl -n sql-server get statefulset mssql
kubectl -n sql-server get pvc
```

If `service.yaml` is included in `kustomization.yaml`, verify external connectivity:

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


- The current secret in `secret.yaml` is for demo use. Replace it with a strong password and a secure secret-management workflow for real environments.
- `service.yaml` currently specifies a fixed `loadBalancerIP`. Ensure that IP is valid and routable in your target environment. It is not included in the kustomization.