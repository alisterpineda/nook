# Backup and restore

A backup of Nook is two things taken at the same moment: a dump of the database, and the `data` folder, which
holds the library and Nook's keys. The database records where each file is inside the library, so a dump and a
`data` folder from different moments can disagree: a book the database lists may have no file. That is why
Nook is stopped while a backup is made.

Do not back up the database by copying the `postgres-data` volume while Postgres is running. A copy made that
way may not start. Use the dump below.

## Back up

Run these in the folder that holds `compose.yaml`:

```sh
docker compose stop nook
docker compose exec -T postgres pg_dump -U nook -d nook --format=custom > nook.dump
sudo tar -czf nook-backup-$(date +%F).tar.gz --exclude=data/cache nook.dump data .env compose.yaml
docker compose start nook
```

Nook is down from the first command to the last. Postgres stays up, because the dump needs it.

The archive holds everything a restore needs: the database, the library, the keys, uploads still waiting, the
settings file if there is one, the compose file and the database's password. The cache is left out, because
Nook makes it again. `sudo` is there because the `data` folder belongs to the user Nook runs as, and it keeps
that owner in the archive.

Keep the archive on another disk or machine, and keep it private: it holds every book, the database's
password and the keys that protect sign-ins.

A library that was moved, as [Storage](storage.md) describes, is not in the archive, whether it was moved
with a mount or with `Nook:LibraryPath`. With a mount over `/data/library`, the `data/library` folder beside
the compose file is empty, and that empty folder is what the archive gets. Add the library's real folder to
the `tar` command, or back it up some other way while Nook is still stopped:

```sh
sudo tar -czf nook-backup-$(date +%F).tar.gz --exclude=data/cache nook.dump data .env compose.yaml /mnt/books
```

To check a backup, list what the archive and the dump hold:

```sh
tar -tzf nook-backup-<date>.tar.gz
docker compose exec -T postgres pg_restore --list < nook.dump
```

The first should show the book files. The second should show a list of tables. It prints an error instead when
the dump is empty or cut short, which is what a failed `pg_dump` leaves behind, and the archive then holds no
usable database: make the backup again.

## Restore

A restore puts Nook back exactly as it was when the backup was made. Everything added or changed since then
is gone.

On a new machine, start with an empty folder that holds only the archive. On a machine where Nook is already
installed, stop Nook and move the current `data` folder out of the way first:

```sh
docker compose stop nook
sudo mv data data.old
```

That is enough for the install the backup came from. An install that was set up separately, such as a fresh
one on a new machine, has a database with a password of its own, and the restore brings back the backup's
`.env` with the backup's password. Nook then cannot reach the database. Remove that install's database
instead, which the restore would replace anyway:

```sh
docker compose down -v
sudo mv data data.old
```

`down -v` deletes the `postgres-data` volume. The restore makes it again, with the backup's password.

Then, in every case:

```sh
sudo tar -xzf nook-backup-<date>.tar.gz
docker compose up -d --wait postgres
docker compose exec -T postgres pg_restore -U nook -d postgres --clean --if-exists --create < nook.dump
docker compose up -d
```

The first command puts back the `data` folder, `nook.dump`, `.env` and `compose.yaml`, replacing a `.env` or
`compose.yaml` already in the folder. The third drops the whole `nook` database, if there is one, and creates
it again from the dump. It needs Nook to be stopped.

Put a library that was moved back in its own folder before the last command. `tar` leaves the leading `/` off
the paths it stores, so a library added as `/mnt/books` comes out as `mnt/books` in the current folder.

People who were signed in when the backup was made are still signed in, because the keys came back with the
`data` folder.

A backup can be restored into the version of Nook that made it or a newer one, which migrates the database
when it starts. An older Nook cannot use a database from a newer one. The last command starts whichever version
`.env` and `compose.yaml` name once the archive is extracted, so to go back to an older version after an
upgrade, follow [Going back](upgrading.md#going-back).
