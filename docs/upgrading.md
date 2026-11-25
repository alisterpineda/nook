# Upgrading

Nook has numbered versions, such as `0.1.0`. The version Nook is on is the last line of the user menu, in the
sidebar's footer, for anyone signed in: "Nook 0.1.0+abc1234". The part after the `+` is the commit the image
was built from.

## Image tags

Each release is published under four tags of `ghcr.io/alisterpineda/nook`:

| Tag      | Follows                                                    |
| -------- | ---------------------------------------------------------- |
| `X.Y.Z`  | One release, such as `0.1.0`. It never moves.              |
| `X.Y`    | The newest release of that major and minor, such as `0.1`. |
| `X`      | The newest release of that major, such as `0`.             |
| `latest` | The newest release.                                        |

`compose.yaml` names `latest`, so an install that sets nothing follows the newest release.

Two more tags are not releases. `main` follows the newest build of the main branch, which has had less testing
than a release: it is the build to try first. A main build also has a tag that is its full commit SHA, which
never moves. There are no prereleases.

## What a version promises

- A major version, such as `1.0.0`, needs manual steps. Its release notes on GitHub say what they are.
- A minor version, such as `0.2.0`, may migrate the database.
- A patch version, such as `0.1.1`, never migrates the database.

The number alone says whether going back needs a backup restore: from a patch of the same minor version,
it does not, and from a newer minor or major version, it does.

## Upgrade

Make a backup first, as [Backup and restore](backup-and-restore.md) describes. Nook migrates the database when
a new version starts, and an older Nook cannot use a database that a newer one has migrated. The backup is the
only way back from a minor or major version.

Then note the version Nook is on, from the user menu. Going back needs it.

Then pull the new image and start it:

```sh
docker compose pull
docker compose up -d
```

An install that names its image with `NOOK_IMAGE` in `.env` does not follow `latest`. Change that value to the
version to move to first, such as `NOOK_IMAGE=ghcr.io/alisterpineda/nook:0.2.0`, and then run the two commands.

## Staying on one version

To stay on one version, set `NOOK_IMAGE` in `.env` to its tag:

```sh
NOOK_IMAGE=ghcr.io/alisterpineda/nook:0.1.0
```

`docker compose pull` then fetches nothing newer, because `0.1.0` never moves. To take patch releases and
nothing else, name `0.1` instead, which is safe because a patch never migrates the database. Naming `0` takes
minor releases as well, which may migrate it.

## Going back

From a patch version to an older one of the same minor version, set `NOOK_IMAGE` in `.env` to the older
version and run `docker compose up -d`. The database was not migrated, so nothing else is needed.

From a minor or major version, restore the backup made before the upgrade, with one step added: before the
restore's last command, `docker compose up -d`, set `NOOK_IMAGE` in `.env` to the version noted before the
upgrade. The restore puts back the `.env` from the backup, so a `NOOK_IMAGE` set any earlier is lost, and
without it the last command starts the newer version, which migrates the database again.

Changing the image alone is not enough, because the database has already been migrated. An older Nook started
on it refuses to start, and says so:

> This database was migrated by a newer Nook: it holds migrations this build does not define (...). Start the
> newer build, or restore the backup made before the upgrade.

The database is left as it was, so starting the newer version again brings Nook back.
