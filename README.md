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
- Password: `nook-dev-password`

A database that was set up before the stack created this admin keeps its own admin until its volume is removed.
