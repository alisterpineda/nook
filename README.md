# nook

Nook is a self-hosted library for a household's ebooks: upload them, then browse, search, read in the browser
and download.

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

To check on Nook, `/health` answers 200 when Nook and its database are up, and `/alive` answers 200 when the
process is up. Neither needs a sign-in.

The compose file serves Nook over http. To serve it over https, put it behind a reverse proxy, as
[Behind a reverse proxy](docs/reverse-proxy.md) describes.

## Guides

- [Backup and restore](docs/backup-and-restore.md): what to keep, and how to get Nook back from it.
- [Upgrading](docs/upgrading.md): moving to a newer version, staying on one, and going back.
- [Releasing](docs/releasing.md): how a new version is tagged and published.
- [Settings](docs/settings.md): the two places a setting can be given.
- [Storage](docs/storage.md): the folders Nook keeps, and moving the library or the cache out of `data`.
- [Accounts](docs/accounts.md): adding people, passwords, roles, deleting, and keeping a second way in.
- [Single sign-on](docs/single-sign-on.md): signing in through an OpenID Connect provider.
- [Public access](docs/public-access.md): opening parts of the library to visitors with no account.
- [Behind a reverse proxy](docs/reverse-proxy.md): serving Nook over https.

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

## Licence

Nook is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE).
