# Behind a reverse proxy

A reverse proxy that ends https, such as nginx, Traefik or Caddy, talks to Nook over plain http. It passes the
original scheme and client address in the `X-Forwarded-Proto` and `X-Forwarded-For` headers. Nook uses them only
from a proxy it has been told to trust, so tell it which one.

| Setting | Environment variable | Default | What it is |
|---|---|---|---|
| `Nook:TrustedProxies` | `Nook__TrustedProxies` | only loopback, inside Nook's own container or host | The addresses or ranges of the proxies Nook takes forwarded headers from, separated by commas or spaces, such as `172.18.0.0/16, 10.0.0.5`. |

[Settings](settings.md) says where a setting can be given. In `nook.json` the value is one string, as it is in
the environment, and not a list. Each entry is an IP address or a range such as
`172.18.0.0/16`. Nook refuses to start when an entry is anything else, such as a host name, and the error names
the entry.

## Why Nook must trust the proxy

Without it, Nook ignores the forwarded headers and sees every request as plain http from the proxy's address:

- The sign-in cookie is not marked `Secure`.
- The rate limits count every visitor as one client, the proxy, so one visitor can use up the limit for all.
- The links Nook builds, such as the ones the reader opens a book from, start with `http://`.
- Single sign-on sends the provider a callback URL that starts with `http://`, which the provider refuses.

Nook honours only `X-Forwarded-For` and `X-Forwarded-Proto`, and takes one address from `X-Forwarded-For`, so it
expects one proxy in front of it.

## Finding the value

When a request comes through a proxy that Nook does not trust, Nook logs a warning, once for each address:

```
A request came through a proxy at 172.18.0.3 that Nook does not trust, so Nook ignored its X-Forwarded headers. If that is your reverse proxy, add 172.18.0.3 to Nook:TrustedProxies.
```

The address is the one Nook sees the proxy at. For a proxy in a container, `docker network inspect <network>`
lists its address. For a proxy on the Docker host, Nook sees the network's gateway, which the same command shows.

An address can change when a container or a network is recreated, so trust the network's range, not one address,
and give the network a fixed subnet, as below.

In Docker, the default never covers a proxy. Without `Nook:TrustedProxies`, Nook trusts only loopback, which in a
container is the container itself: a proxy on the same host reaches Nook from the network's gateway, not from
loopback. Outside Docker, a proxy on the same host as Nook is on loopback and needs no setting.

## Keeping Nook's port to the proxy

A sender that reaches Nook directly can claim any client address and scheme once its own address is trusted.
Nook's port must be open to the proxy and nobody else, in one of two ways. Either one gives the network a fixed
subnet, and `Nook:TrustedProxies` names that subnet. Pick a range that nothing else on the host uses.

For a proxy on the Docker host, publish the port on loopback only. The proxy reaches Nook at `127.0.0.1:8080`,
and Nook sees it from the compose network's gateway, which is inside the subnet:

```yaml
services:
  nook:
    ports:
      - "127.0.0.1:8080:8080"
    environment:
      Nook__TrustedProxies: 172.28.0.0/16

networks:
  default:
    ipam:
      config:
        - subnet: 172.28.0.0/16
```

For a proxy in a container, drop `ports` and put Nook and the proxy on a network they share. Nothing outside
Docker can then reach Nook, and the proxy reaches it at `http://nook:8080`:

```yaml
services:
  nook:
    networks:
      - default
      - proxy
    environment:
      Nook__TrustedProxies: 172.29.0.0/16

networks:
  proxy:
    name: nook-proxy
    ipam:
      config:
        - subnet: 172.29.0.0/16
```

Give the proxy's own compose file the same network as `external: true`, named `nook-proxy`.

## What the proxy must do

- Pass the original `Host` header. Nook ignores `X-Forwarded-Host`, and builds its links from the Host it
  receives, so a proxy that replaces Host breaks them.
- Set `X-Forwarded-For` to the client's address, and `X-Forwarded-Proto` to `https`.
- Pass WebSockets for `/api/hubs/activity`, which the app uses to show what is happening in the library. Without
  them it falls back to slower transports.
- Allow a request body larger than `Nook:Uploads:MaxBytes`, which is 200 MB by default. Nook accepts a body of
  `MaxBytes` plus 1 MiB for the rest of the upload form, so set the proxy's limit above that, and raise it
  together with the setting. Set `Nook__Uploads__MaxBytes` to change the limit.
- Send `Strict-Transport-Security` itself. Nook does not, so set HSTS at the proxy.

## nginx

nginx replaces `Host` with the name of the upstream and passes WebSockets only when told to, and its default
`client_max_body_size` is 1 MB, so an upload fails with a 413 until it is raised. This server block covers all
of it:

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 443 ssl;
    server_name nook.example.com;

    ssl_certificate     /etc/ssl/nook.example.com/fullchain.pem;
    ssl_certificate_key /etc/ssl/nook.example.com/privkey.pem;

    add_header Strict-Transport-Security "max-age=31536000" always;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        client_max_body_size 250m;
    }
}
```

Change `proxy_pass` to `http://nook:8080` for a proxy that shares a network with Nook.

## Traefik

Traefik passes the client's `Host` header to the service unless `passHostHeader` is set to false, so leave it at
its default. Traefik's buffering middleware can limit the size of a request body: its `maxRequestBodyBytes` is
unlimited by default, so if you use it, do not set it below the upload limit. Make sure that Traefik passes
WebSockets and sends the forwarded headers as described above. Set HSTS with its headers middleware.

## Caddy

Caddy passes the incoming `Host` header to the backend unchanged, supports WebSocket connections through
`reverse_proxy`, and sets `X-Forwarded-For` and `X-Forwarded-Proto` itself:

```
nook.example.com {
    reverse_proxy 127.0.0.1:8080
    header Strict-Transport-Security "max-age=31536000"
}
```

Caddy's documentation for `reverse_proxy` states no limit on the size of a request body. Make sure that nothing
in your configuration sets one below the upload limit.

## The framework's variable

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` still works, but it makes Nook trust forwarded headers from any
sender, and Nook logs a warning at startup to say so. Set `Nook:TrustedProxies` instead, and remove the
variable. When both are set, the list in `Nook:TrustedProxies` applies.
