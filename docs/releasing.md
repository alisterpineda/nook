# Releasing

A release is a tag `vX.Y.Z` pushed by hand. CI builds the image with that version and publishes it, and the
release's notes are written by hand on GitHub. [Upgrading](upgrading.md) says what the image's tags are and
what a version promises to someone who installs it.

There are no prereleases. The `main` image, which every push to main publishes, is the build to try first.

## Choose the number

The first release is `0.1.0`. After it, the number says what an upgrade asks of an install:

- Major, such as `1.0.0`: the upgrade needs manual steps.
- Minor, such as `0.2.0`: the release may migrate the database.
- Patch, such as `0.1.1`: the release never migrates the database. A release that adds a migration is at
  least a minor one.

Releases only go forward: a number lower than the newest release, such as `0.1.3` after `0.2.0`, is refused.

## Tag the release

Main must be green in CI and pushed, because the tag has to name a commit that is on main. Then tag that
commit and push the tag:

```sh
git tag -a vX.Y.Z -m "Nook X.Y.Z"
git push origin vX.Y.Z
```

No commit sets the version before the release or moves it on afterwards: the tag alone names it.

## What CI does

The push starts a CI run on the tag. Follow it with:

```sh
gh run watch
```

The run tests the code, then refuses the tag, and publishes nothing, when:

- the tag is not `vX.Y.Z` with plain numbers;
- the tagged commit is not on main;
- another `vX.Y.Z` tag is higher;
- the registry already holds an image tagged `X.Y.Z`.

Otherwise it builds the image with version `X.Y.Z`, starts it and checks that it reports that version, and
publishes it under `X.Y.Z`, `X.Y`, `X` and `latest`. A release never moves the `main` tag.

Until the first release, `latest` stays on the last main build that was published under it.

## Write the notes

List what went in since the previous release:

```sh
git log --oneline v<previous>..vX.Y.Z
```

Write the notes from that list into a file. Leave out commits that fix what no release shipped, and say
whether the release migrates the database. For a major release, give the manual steps. Then make the GitHub
Release:

```sh
gh release create vX.Y.Z --title "Nook X.Y.Z" --notes-file <file>
```

## When a release is broken

Fix it on main and release the next version. A tag is never moved or pushed again, and a version's image is
never published twice. This also holds for a run that failed after publishing some of its tags: the next
version follows.
