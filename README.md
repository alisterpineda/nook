# nook

## Running locally

With Docker running, start the stack from `src/Nook.AppHost`:

```sh
aspire run
```

In VS Code, the "Aspire: Nook" launch configuration does the same.

On an empty database the stack creates an admin, so there is no setup form to fill in. Sign in at
<http://localhost:4200> with:

- Email: `admin@nook.test`
- Password: `admin@nook.test` (the same as the email)

A database that was set up before the stack created this admin keeps its own admin until its volume is removed.

## Installing

Nook runs from a container image, `ghcr.io/alisterpineda/nook`, next to a Postgres database. You need Docker
with Compose, on an amd64 or arm64 machine.

In a new folder, download the compose file:

```sh
curl -fsSLO https://raw.githubusercontent.com/alisterpineda/nook/main/compose.yaml
```

Set the database's password in a `.env` file beside it. Use a long random value made only of letters and
digits, because the value goes into a connection string:

```sh
echo "POSTGRES_PASSWORD=<a long random value>" > .env
```

Create the folder Nook keeps its files in, and give it to the user Nook runs as. Nook does not run as root: it
runs as a user with the id 1654.

```sh
mkdir data
sudo chown 1654:1654 data
```

Start Nook:

```sh
docker compose up -d
```

Nook then answers at <http://localhost:8080>. The first visit shows the setup page, which creates the admin.

Keep two things to be able to restore Nook: the `data` folder, which holds the library and Nook's keys, and
the `postgres-data` volume, which holds the database. Both are needed.

To upgrade, pull the new image and start it:

```sh
docker compose pull
docker compose up -d
```

The `latest` image follows main, and Nook migrates the database when it starts. To stay on one build, set
`NOOK_IMAGE=ghcr.io/alisterpineda/nook:<commit SHA>` in `.env`, with the commit's full SHA.

To check on Nook, `/health` answers 200 when Nook and its database are up, and `/alive` answers 200 when the
process is up. Neither needs a sign-in.

The compose file serves Nook over http. To serve it over https, put it behind a reverse proxy, as
[Behind a reverse proxy](#behind-a-reverse-proxy) describes. [Single sign-on](#single-sign-on) covers signing
in through a provider. [Storage](#storage) covers keeping the library or the cache somewhere other than the
`data` folder.

## Storage

Nook keeps its files in four folders. By default all four are in the data root, which is `/data` in the
container image and the `data` folder beside the compose file.

| Folder | What it holds | When it is lost |
|---|---|---|
| `library` | Every book file and cover. | The books are gone. |
| `keys` | The keys that protect sign-ins and the links to open books. | Everyone is signed out, and a book that is open stops loading until it is opened again. |
| `staging` | Uploads waiting to be added to the library. | The uploads still waiting are gone. |
| `cache` | Resized covers. | Nothing: Nook makes them again from the library. |

The library and the cache can each have a folder of their own. The other two always stay in the data root.

| Setting | Environment variable | Default | What it is |
|---|---|---|---|
| `Nook:DataPath` | `Nook__DataPath` | `/data` in the image | The data root. Required. |
| `Nook:LibraryPath` | `Nook__LibraryPath` | `library` in the data root | The folder that holds the library. |
| `Nook:CachePath` | `Nook__CachePath` | `cache` in the data root | The folder that holds the cache. |

In a container there are two ways to move a folder. Either mount another folder over its place, which needs no
setting, or set its path and mount a folder there. This keeps the library on another disk and the cache inside
the container, where it is made again after the container is replaced:

```yaml
services:
  nook:
    volumes:
      - ./data:/data
      - /mnt/books:/data/library
    environment:
      Nook__CachePath: /tmp/nook-cache
```

The user Nook runs as, with the id 1654, must be able to write to every folder. A folder Docker creates for a
mount belongs to root, so create the folder first and give it to that user, as with `data`.

When Nook starts, it logs where each folder is. It refuses to start when it cannot write to one of them, or
when two of them are the same folder or one is inside another, and the error names the folder and the setting
that places it.

The database records where each file is inside the library, not where the library is. To move the library,
stop Nook, move the folder, point the mount or `Nook:LibraryPath` at its new place, and start Nook.

## Single sign-on

Nook can sign people in through an OpenID Connect provider, such as Authentik or Keycloak, next to the email
and password. It works with one provider at a time and is off until `Nook:Oidc:Authority` is set.

| Setting | Environment variable | Default | What it is |
|---|---|---|---|
| `Nook:Oidc:Authority` | `Nook__Oidc__Authority` | none | The provider's issuer URL. Setting it turns single sign-on on. |
| `Nook:Oidc:ClientId` | `Nook__Oidc__ClientId` | none | The client id Nook is registered under at the provider. Required once `Authority` is set. |
| `Nook:Oidc:ClientSecret` | `Nook__Oidc__ClientSecret` | none | The client secret Nook is registered with. Required once `Authority` is set. |
| `Nook:Oidc:DisplayName` | `Nook__Oidc__DisplayName` | `SSO` | The provider's name on the sign-in page, as in "Sign in with SSO". |
| `Nook:Oidc:AdminGroup` | `Nook__Oidc__AdminGroup` | none | The group whose members are admins. |
| `Nook:Oidc:ContributorGroup` | `Nook__Oidc__ContributorGroup` | none | The group whose members are contributors. |
| `Nook:Oidc:GroupsClaim` | `Nook__Oidc__GroupsClaim` | `groups` | The claim that lists the groups someone is in. |
| `Nook:Oidc:Scopes` | `Nook__Oidc__Scopes` | `openid profile email` | The scopes Nook asks the provider for, separated by spaces. |

Nook refuses to start when `Authority` is set and `ClientId` or `ClientSecret` is missing.

Single sign-on needs https: the browser must reach Nook over https, because the cookies that carry a sign-in to
the provider and back are marked `Secure`.

At the provider, register Nook as a confidential client that uses the authorization code flow, with this
callback (redirect) URL:

```
https://<nook host>/api/auth/oidc/callback
```

What the provider must send:

- An `email` claim. Nook creates the account on the first sign-in, with that email and no password. Someone
  whose provider account has no email cannot sign in.
- The groups someone is in, as the values of the claim named by `GroupsClaim`, when roles should follow groups.
- `email_verified` as true, for someone whose email already belongs to a Nook account. See below.

### Roles

When `AdminGroup` or `ContributorGroup` is set, every single sign-on sets the person's role from their groups:
admin when they are in `AdminGroup`, otherwise contributor when they are in `ContributorGroup`, otherwise
viewer. The configured name must equal a value of the groups claim exactly, including its case. A provider that
sends group paths, such as `/admins`, does not match `admins`.

When neither is set, everyone new is a viewer, and single sign-on leaves the role of an existing account as it
is.

The rule also applies to an account that has a password. An admin who signs in through the provider without
being in `AdminGroup` stops being an admin, so join the admin group at the provider before the first single
sign-on.

### Existing accounts

When the provider's email is the email of a Nook account that has not used single sign-on yet, Nook links the
two only when the provider sends `email_verified` as true. Otherwise the sign-in is refused. Once linked, the
account's password still works. An account that is already linked to one provider account is never linked to a
second: that sign-in is refused too.

Password sign-in and first-run setup stay available when single sign-on is on. Signing out of Nook does not
sign out of the provider.

### Before Nook is set up

While Nook has no accounts, single sign-on only lets in someone who is in `AdminGroup`, who then is the first
admin in place of first-run setup. Anyone else is refused until an admin exists, because the first account
closes setup, and a first account that is not an admin would leave Nook with no admin. With no `AdminGroup`
set, create the first admin on the setup page.

### Authentik

- The authority has the form `https://<authentik host>/application/o/<application slug>/`. That form is not
  verified against Authentik itself, so copy the "OpenID Configuration Issuer" value Authentik shows for the
  provider.
- Since 2025.10 Authentik sends `email_verified` as false unless the provider's email scope mapping is
  replaced, so by default it cannot sign in to an account that already exists in Nook. See
  [Authentik's page on verifying email addresses](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/).
- Check in Authentik which claim lists someone's groups, and set `GroupsClaim` to it when it is not `groups`.

## Behind a reverse proxy

Behind a reverse proxy that ends https, such as Traefik, set this environment variable on Nook:

```
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

The proxy talks to Nook over plain http and passes the original scheme and client address in the
`X-Forwarded-Proto` and `X-Forwarded-For` headers. Without the variable Nook ignores them, sees every request
as http, and sends the provider a callback URL that starts with `http://`, which the provider refuses.

With the variable set, Nook accepts those headers from any sender. Keep Nook's port reachable only from the
proxy, so that nobody else can send them.

## Licence

Nook is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE).
