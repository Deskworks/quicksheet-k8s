# quicksheet-k8s

A [QuickSheet](https://github.com/cemheren/QuickSheet) extension that shows live
Kubernetes pod status from your current kubeconfig context — right on your desktop
wallpaper or terminal spreadsheet.

## Why?

SREs and DevOps engineers run `kubectl get pods` dozens of times a day. This extension
makes pod status **ambient** — CrashLoopBackOff, pending pods, and restart counts
appear on your wallpaper without opening a terminal.

## Install

In any QuickSheet cell:

```
ext: github:cemheren/quicksheet-k8s
```

## Usage

| Cell value | What it shows |
|---|---|
| `k8s: default` | Pods in the `default` namespace |
| `k8s: kube-system` | Pods in `kube-system` |
| `k8s: all` | Pods across all namespaces |

## Output

```
☸ my-cluster          ns: default
POD                   STATUS      READY  RESTARTS  AGE
api-7f8b9c-x2k       🟢 Running  True   0         3d
worker-5d4e3f-abc     🟢 Running  True   2         5d
db-migration-z9y      🔴 Failed   False  5         1h
cache-warmer-q1w      🟡 Pending  False  0         2m
```

### Status icons

| Icon | Meaning |
|---|---|
| 🟢 | Running |
| ✅ | Succeeded / Completed |
| 🟡 | Pending / ContainerCreating |
| 🔴 | CrashLoopBackOff / Error / Failed |
| 🟠 | Terminating |
| ⚪ | Evicted |

## Requirements

- `kubectl` installed and on PATH
- A valid kubeconfig (`~/.kube/config`) with a current context set
- QuickSheet v0.8.0+

## How it works

Pure subprocess call to `kubectl get pods` with custom columns. Zero network APIs,
zero credentials to configure beyond your existing kubeconfig. No NuGet dependencies.

## License

MIT
