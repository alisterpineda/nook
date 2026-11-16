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
| `Nook:Oidc:MemberGroup` | `Nook__Oidc__MemberGroup` | none | The group whose members are members. |
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

When `AdminGroup` or `MemberGroup` is set, every single sign-on sets the person's role from their groups: admin
when they are in `AdminGroup`, otherwise member when they are in `MemberGroup`, otherwise guest. The configured
name must equal a value of the groups claim exactly, including its case. A provider that sends group paths,
such as `/admins`, does not match `admins`.

When neither is set, everyone new is a guest, and single sign-on leaves the role of an existing account as it
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
