# bitween (API) Helm chart

Deploys the Bitween API as a Kubernetes `Deployment` + `Service` (plus a
`Secret` for connection strings and credentials), exposed north-south through
either **ingress-nginx** (default) or the **Gateway API** (`HTTPRoute`).

## Routing modes

Two mutually exclusive routing modes, selected at deploy time:

| Mode | How to select | Renders |
|------|---------------|---------|
| **ingress-nginx** (default) | nothing - this is the default | `Ingress` |
| **Gateway API** | `--set gateway.enabled=true` | `HTTPRoute` (and the `Ingress` is suppressed) |

`gateway.enabled` defaults to `false`, so the chart renders only the `Ingress`
out of the box. Setting `gateway.enabled=true` flips routing to the Gateway API
and automatically suppresses the `Ingress` - you do **not** need to also set
`ingress.enabled=false`.

### Behavior matrix

| `gateway.enabled` | `ingress.enabled` | Resources rendered |
|-------------------|-------------------|--------------------|
| `false` (default) | `true` (default)  | `Ingress` only     |
| `false`           | `false`           | neither            |
| `true`            | `true` or `false` | `HTTPRoute` only   |

## Usage

### ingress-nginx (default)

```sh
helm install bitween ./charts/default \
  --set ingress.hosts[0]=api.example.com
# paths default to /api, /swagger and / (ingress.paths)
```

### Gateway API

```sh
helm install bitween ./charts/default \
  --set gateway.enabled=true \
  --set gateway.hostnames[0]=api.example.com \
  --set gateway.parentRefs[0].name=public-gateway \
  --set gateway.parentRefs[0].namespace=my-gateway-ns \
  --set gateway.parentRefs[0].sectionName=https \
  --set gateway.routes[0].path=/api \
  --set gateway.routes[1].path=/swagger
```

The Gateway API requires the
[Gateway API CRDs](https://gateway-api.sigs.k8s.io/) and a Gateway controller in
the cluster, plus a `Gateway` for the `parentRefs` to attach to. The chart does
**not** create the `Gateway` or any TLS `Certificate` - those are cluster/CI
concerns.

## Values

### Ingress (`ingress-nginx`)

| Key | Default | Description |
|-----|---------|-------------|
| `ingress.enabled` | `true` | Render an `Ingress` (only when `gateway.enabled` is `false`). |
| `ingress.annotations` | `{}` | Annotations applied to the `Ingress`. |
| `ingress.hosts` | _(unset)_ | List of hostnames; one rule per host. |
| `ingress.paths` | `[/api, /swagger, /]` | Paths exposed per host. `/` serves the admin UI. |
| `ingress.path` | `/` | Single path used only when `ingress.paths` is empty. |
| `ingress.tls` | `[]` | List of `{ secretName, hosts }` TLS entries. |

### Gateway API (`HTTPRoute`)

| Key | Default | Description |
|-----|---------|-------------|
| `gateway.enabled` | `false` | Render an `HTTPRoute` instead of the `Ingress`. |
| `gateway.parentRefs` | see `values.yaml` | `Gateway`/listener references the route attaches to (`name`, `namespace`, `sectionName`). |
| `gateway.hostnames` | see `values.yaml` | Hostnames the route matches. |
| `gateway.routes` | `[{ path: "/", pathType: PathPrefix }]` | Path matches and backend refs. Each entry supports `path`, `pathType`, optional `timeout.{request,backendRequest}`, and `backendRef.{name,port,namespace,weight}`. `backendRef.name` defaults to the release fullname; `backendRef.port` defaults to `service.port`. The default `/` already matches the ingress paths. |

> The shipped `gateway` defaults reference Simplify9 cluster infrastructure
> (`public-gateway` / `s9-dev-edge` / `*.sf9.io`) and a single `/` route.
> Override `parentRefs`, `hostnames`, and `routes` for your own environment.

### Common

| Key | Default | Description |
|-----|---------|-------------|
| `service.type` | `ClusterIP` | Service type. |
| `service.port` | `80` | Service port (also the default `HTTPRoute` backend port). |
| `serviceAccount.name` | `''` | Existing ServiceAccount the pod runs as, e.g. one bound to an Azure Workload Identity. Empty renders no `serviceAccountName`. The chart does not create it. |
| `busDefaultConcurrency` | `''` | Parallel exchanges for the Ungrouped lane and the bus's other consumers. Empty takes `busDefaultQueuePrefetch`; `'1'` runs them one at a time, in order. Rendered as `Bitween__BusDefaultConcurrency` only when set. |
| `terminationGracePeriodSeconds` | `60` | How long a stopping pod has before it is killed. Keep it above the app's 45 second shutdown, which lets running exchanges finish for up to 30 seconds. |
| `podLabels` | `{}` | Extra labels on the pod template only, never the selector. Values render as strings, e.g. `--set 'podLabels.azure\.workload\.identity/use=true'`. `app`, `draft` and `release` are set by the chart and can't be overridden. |
