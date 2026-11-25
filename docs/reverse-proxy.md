# Behind a reverse proxy

Behind a reverse proxy that ends https, such as Traefik, set this environment variable on Nook:

```
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

The proxy talks to Nook over plain http and passes the original scheme and client address in the
`X-Forwarded-Proto` and `X-Forwarded-For` headers. Without the variable Nook ignores them, sees every request
as http, and sends the provider a callback URL that starts with `http://`, which the provider refuses.

With the variable set, Nook accepts those headers from any sender. Keep Nook's port reachable only from the
proxy, so that nobody else can send them.
