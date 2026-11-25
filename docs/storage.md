# Storage

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

[Settings](settings.md) says where a setting can be given.

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

A library that was moved, by a mount or by `Nook:LibraryPath`, needs its own place in a backup. See
[Backup and restore](backup-and-restore.md).
