# Single sign-on

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
| `Nook:PasswordSignIn` | `Nook__PasswordSignIn` | `true` | Set to `false` to turn password sign-in off, so the provider is the only way in. See [Single sign-on only](#single-sign-on-only). |

Nook refuses to start when `Authority` is set and `ClientId` or `ClientSecret` is missing, and when
`Nook:PasswordSignIn` is `false` and `Authority` is not set.

Single sign-on needs https: the browser must reach Nook over https, because the cookies that carry a sign-in to
the provider and back are marked `Secure`.

To run Nook behind a reverse proxy, see [Behind a reverse proxy](reverse-proxy.md), which says how Nook learns
that a request came over https.

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

## Roles

When `AdminGroup` or `ContributorGroup` is set, every single sign-on sets the person's role from their groups:
admin when they are in `AdminGroup`, otherwise contributor when they are in `ContributorGroup`, otherwise
viewer. The configured name must equal a value of the groups claim exactly, including its case. A provider that
sends group paths, such as `/admins`, does not match `admins`.

When neither is set, everyone new is a viewer, and single sign-on leaves the role of an existing account as it
is.

The rule also applies to an account that has a password. An admin who signs in through the provider without
being in `AdminGroup` stops being an admin, so join the admin group at the provider before the first single
sign-on.

## Existing accounts

When the provider's email is the email of a Nook account that has not used single sign-on yet, Nook links the
two only when the provider sends `email_verified` as true. Otherwise the sign-in is refused. Once linked, the
account's password still works. An account that is already linked to one provider account is never linked to a
second: that sign-in is refused too.

Password sign-in and first-run setup stay available when single sign-on is on, unless
[Single sign-on only](#single-sign-on-only) turns them off. Signing out of Nook does not
sign out of the provider.

## Before Nook is set up

While Nook has no accounts, single sign-on only lets in someone who is in `AdminGroup`, who then is the first
admin in place of first-run setup. Anyone else is refused until an admin exists, because the first account
closes setup, and a first account that is not an admin would leave Nook with no admin. With no `AdminGroup`
set, create the first admin on the setup page, unless [Single sign-on only](#single-sign-on-only) has closed
the setup page: then `AdminGroup` is the only way to a first admin.

## Single sign-on only

Set `Nook:PasswordSignIn` to `false` and the provider is the only way to sign in. It turns password sign-in
off entirely, not just the form: a visitor who sends an email and password to Nook is told there is nothing
there, so passwords cannot be guessed. It also turns off first-run setup, which creates an account with a
password. The sign-in page then shows only the "Sign in with ..." link, with no email or password field. It
does not leave for the provider by itself: someone opening `/sign-in` still has to click the link.

How an empty instance gets its first admin: as in [Before Nook is set up](#before-nook-is-set-up), the first
person in `AdminGroup` to sign in through the provider. The setup page is closed, so `AdminGroup` is the only
way: set it before turning password sign-in off on an instance with no accounts.

If the provider is down, nobody can sign in through it, and this is the way back in, only for an account that
has a password. Set `Nook:PasswordSignIn` back to `true` (or remove it) and restart Nook. The setting changes
no stored password, so the passwords people had work again. An account created through the provider has no
password until its owner adds one on the Account page, or an admin sets one, as [Accounts](accounts.md)
describes, and until then it needs the provider. Turn the setting off again when the provider is back.

What happens to people already signed in:

- Existing sessions carry on. Turning the setting off, or on, does not sign anyone out.
- Someone removed at the provider keeps their Nook session for as long as they keep using it. It ends when
  they sign out, or when the 30-day session runs out without being renewed, and using Nook renews it. Nook
  asks the provider only when someone signs in.

## Authentik

- The authority has the form `https://<authentik host>/application/o/<application slug>/`. That form is not
  verified against Authentik itself, so copy the "OpenID Configuration Issuer" value Authentik shows for the
  provider.
- Since 2025.10 Authentik sends `email_verified` as false unless the provider's email scope mapping is
  replaced, so by default it cannot sign in to an account that already exists in Nook. See
  [Authentik's page on verifying email addresses](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/).
- Check in Authentik which claim lists someone's groups, and set `GroupsClaim` to it when it is not `groups`.
