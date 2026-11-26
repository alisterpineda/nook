# Settings

Every setting in the tables of these guides can be given in either of two ways: as an environment variable on
the `nook` service in the compose file, or in a file named `nook.json` in the `data` folder. The file needs no
mount of its own, because the `data` folder is already mounted.

```json
{
  "Nook": {
    "Oidc": {
      "Authority": "https://auth.example.com/realms/home",
      "ClientId": "nook",
      "ClientSecret": "<the client secret>"
    }
  },
  "RateLimiting": {
    "Anonymous": { "PermitLimit": 600 }
  }
}
```

A setting's name gives its place in the file: each `:` in the name is one level, so `Nook:Oidc:Authority` is
`Authority` inside `Oidc` inside `Nook`. The file may hold `//` comments.

Nook reads the file when it starts, so restart Nook after changing it: `docker compose restart nook`. The
user Nook runs as, with the id 1654, must be able to read it. When the file is there, Nook logs that it read
it, and it refuses to start when the file is not valid JSON.

When one of those settings is in both places, the environment variable wins. The data root, `Nook:DataPath`,
cannot be set in the file, because it says where the file is.

The file is only for the settings in the tables. Anything else, such as `ASPNETCORE_URLS`, stays an
environment variable.

The tables are in [Storage](storage.md), [Single sign-on](single-sign-on.md),
[Public access](public-access.md) and [Behind a reverse proxy](reverse-proxy.md).
