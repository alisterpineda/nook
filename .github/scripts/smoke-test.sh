#!/usr/bin/env bash
# Starts a Nook image with compose.yaml, the way the README's "Installing" section says to, and checks
# that it answers and that it is the version expected, such as 0.1.0 or 0.0.0-dev.
# Usage: smoke-test.sh <image> <version>
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <image> <version>" >&2
  exit 2
fi

image="$1"
version="$2"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="nook-smoke"
url="http://localhost:8080"

work_dir="$(mktemp -d)"

cleanup() {
  status=$?
  cd "$work_dir"
  if [ "$status" -ne 0 ]; then
    docker compose -p "$project" logs || true
  fi
  docker compose -p "$project" down -v || true
  cd /
  # On Linux the data folder belongs to the image's user, so removing it takes sudo.
  if [ "$(uname -s)" = "Linux" ]; then
    sudo rm -rf "$work_dir"
  else
    rm -rf "$work_dir"
  fi
  exit "$status"
}
trap cleanup EXIT

cp "$repo_root/compose.yaml" "$work_dir/"
cd "$work_dir"
printf 'POSTGRES_PASSWORD=smoke-test-password\nNOOK_IMAGE=%s\n' "$image" > .env

mkdir data
# Nook runs as the user with id 1654. Docker Desktop on macOS lets any container user write to a bind
# mount, so the folder's owner only matters on Linux.
if [ "$(uname -s)" = "Linux" ]; then
  sudo chown 1654:1654 data
fi

docker compose -p "$project" up -d --wait --wait-timeout 120

healthy=false
for _ in $(seq 1 30); do
  if curl -fsS -o /dev/null "$url/health"; then
    healthy=true
    break
  fi
  sleep 2
done
if [ "$healthy" != true ]; then
  echo "$url/health did not answer 200 within 60 seconds." >&2
  exit 1
fi

index="$(curl -fsS "$url/")"
if [[ "$index" != *"<app-root>"* ]]; then
  echo "$url/ did not return the web app's index page." >&2
  exit 1
fi

# Nook logs its version when it starts: the version, then the first 7 characters of the commit it was built from.
logs="$(docker compose -p "$project" logs nook)"
if ! [[ "$logs" =~ "Nook $version+"[0-9a-f]{7}" is starting." ]]; then
  echo "The log of $image does not say that Nook $version is starting." >&2
  exit 1
fi

echo "Smoke test passed for $image, version $version."
