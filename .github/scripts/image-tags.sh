#!/usr/bin/env bash
# Works out what a CI run builds and publishes, and writes it as name=value lines on standard output.
# Reads GITHUB_REF_TYPE, GITHUB_REF_NAME and GITHUB_SHA.
#
# A branch run writes version=0.0.0-dev and tags=<tags>. The tags are the commit's SHA, plus main when the
# commit is still the head of main, so a run that finishes late, or an old run started again, never puts
# main back on an older commit. Nook migrates the database when it starts, and older code must not meet a
# newer one.
#
# A tag run fails, and writes nothing, unless the tag is vX.Y.Z with plain numbers, the tagged commit is on
# main, and no other vX.Y.Z tag is higher. Otherwise it writes version=X.Y.Z, release=X.Y.Z and
# tags=X.Y.Z;X.Y;X;latest.
#
# MAIN_REF names main for the check that the tagged commit is on it (default origin/main). It exists so the
# check can be tried by hand with sample inputs.
#
# With --check-unpublished <image>:<tag> it only checks that the registry holds no image under that name,
# and fails when it does. Sign in to the registry first.
set -euo pipefail

if [ "${1:-}" = "--check-unpublished" ]; then
  if [ "$#" -ne 2 ]; then
    echo "Usage: $0 --check-unpublished <image>:<tag>" >&2
    exit 2
  fi
  # Exit 0 means the registry has the tag. Any other exit reads as not published; a network or sign-in
  # failure then fails the publish that follows in the same way.
  if docker buildx imagetools inspect "$2" > /dev/null 2>&1; then
    echo "$2 is already published." >&2
    exit 1
  fi
  exit 0
fi

if [ "$#" -ne 0 ]; then
  echo "Usage: $0 [--check-unpublished <image>:<tag>]" >&2
  exit 2
fi

ref_type="${GITHUB_REF_TYPE:?GITHUB_REF_TYPE is not set}"
ref_name="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"
sha="${GITHUB_SHA:?GITHUB_SHA is not set}"

if [ "$ref_type" = "branch" ]; then
  head="$(git ls-remote origin refs/heads/main | cut -f1)"
  if [ -z "$head" ]; then
    echo "Could not read the head of main." >&2
    exit 1
  fi
  if [ "$head" = "$sha" ]; then
    tags="main;$sha"
  else
    echo "main is now at $head, so this run leaves main alone." >&2
    tags="$sha"
  fi
  echo "version=0.0.0-dev"
  echo "tags=$tags"
  exit 0
fi

if [ "$ref_type" != "tag" ]; then
  echo "Cannot publish from a $ref_type." >&2
  exit 1
fi

pattern='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
if ! [[ "$ref_name" =~ $pattern ]]; then
  echo "The tag $ref_name is not vX.Y.Z with plain numbers." >&2
  exit 1
fi

# GITHUB_SHA may name the tag object rather than the commit, so resolve it to the commit.
commit="$(git rev-parse "$sha^{commit}")"
main_ref="${MAIN_REF:-origin/main}"
if ! git merge-base --is-ancestor "$commit" "$main_ref"; then
  echo "The tagged commit $commit is not on $main_ref." >&2
  exit 1
fi

highest="$( { echo "$ref_name"; git tag --list | grep -E "$pattern" || true; } | sort -V | tail -n 1)"
if [ "$highest" != "$ref_name" ]; then
  echo "The tag $highest is higher than $ref_name, and releases only go forward." >&2
  exit 1
fi

release="${ref_name#v}"
major="${release%%.*}"
minor="${release#*.}"
minor="${major}.${minor%%.*}"

echo "version=$release"
echo "release=$release"
echo "tags=$release;$minor;$major;latest"
