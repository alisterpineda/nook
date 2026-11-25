# Upgrading

Make a backup first, as [Backup and restore](backup-and-restore.md) describes. Nook migrates the database when
a new version starts, and an older Nook cannot use a database that a newer one has migrated. The backup is the
only way back.

Then note the build Nook is on, unless `NOOK_IMAGE` in `.env` already names it. Going back needs it. Do this
before anything is pulled: once a newer image has been pulled, `latest` names that one, even while Nook is
still running the older build.

```sh
docker image inspect --format '{{index .RepoDigests 0}}' ghcr.io/alisterpineda/nook:latest
```

The answer has the form `ghcr.io/alisterpineda/nook@sha256:<digest>`. Keep it with the backup.

Then pull the new image and start it:

```sh
docker compose pull
docker compose up -d
```

The `latest` image follows main. To stay on one build, set
`NOOK_IMAGE=ghcr.io/alisterpineda/nook:<commit SHA>` in `.env`, with the commit's full SHA.

## Going back

To return to the build Nook was on before an upgrade, restore the backup made before the upgrade, with one
step added: before the restore's last command, `docker compose up -d`, set `NOOK_IMAGE` in `.env` to that
build. Its value is the line noted before the upgrade, or the `NOOK_IMAGE` the backup's `.env` already holds.
The restore puts back the `.env` from the backup, so a `NOOK_IMAGE` set any earlier is lost, and without it
the last command starts the newer build, which migrates the database again.

Changing the image alone is not enough either, because the database has already been migrated.
