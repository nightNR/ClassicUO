#!/usr/bin/env bash
###############################################################################
# build-cuo-linux.sh — Dockerized wrapper around build-cuo.sh that pins the
# NativeAOT build to Ubuntu 22.04 (glibc 2.35), so cuo.so runs on any target
# glibc >= 2.35 (glibc is backward-compatible: a binary linked against an
# older glibc runs fine on newer ones, never the other way around).
#
# Why this exists: building cuo.so on a newer host distro (glibc 2.38+, e.g.
# Ubuntu 24.04/23.10+) links it against symbols Ubuntu 22.04's glibc 2.35
# doesn't have yet -> "GLIBC_2.38 not found" at dlopen on 22.04 targets.
# Building inside a pinned 22.04 container avoids that entirely.
#
# Installs .NET 10 SDK + NativeAOT prereqs (clang, zlib1g-dev, build-essential)
# inside an ubuntu:22.04 container, then runs the repo's build-cuo.sh exactly
# as it would run natively. All args are passed through unchanged.
#
# Usage:
#   bash build-cuo-linux.sh [--rid <rid>] [--config <cfg>] [--deploy-to <dir>] [--custom-login-scene]
# Defaults: --rid linux-x64  --config Release   (see build-cuo.sh for details)
#
# Requires: docker (or podman via CONTAINER_ENGINE=podman env var).
###############################################################################
set -euo pipefail

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mFAIL: %s\033[0m\n' "$*" >&2; exit 1; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[ -f "$REPO_ROOT/build-cuo.sh" ] || die "build-cuo.sh not found at repo root ($REPO_ROOT) — run from the ClassicUO repo."

ENGINE="${CONTAINER_ENGINE:-docker}"
command -v "$ENGINE" >/dev/null 2>&1 || die "'$ENGINE' not found. Install docker, or set CONTAINER_ENGINE=podman."

# --deploy-to is a host path, but only /src (== REPO_ROOT) is bind-mounted
# into the container by default. Rewrite a REPO_ROOT-relative path to its
# /src equivalent; bind-mount any other absolute path separately so `cp`
# inside the container actually lands on the host.
ARGS=("$@")
DEPLOY_MOUNT=()
for i in "${!ARGS[@]}"; do
  if [ "${ARGS[$i]}" = "--deploy-to" ] && [ $((i + 1)) -lt ${#ARGS[@]} ]; then
    HOST_DEPLOY="${ARGS[$((i + 1))]}"
    case "$HOST_DEPLOY" in
      "$REPO_ROOT"/*)
        ARGS[$((i + 1))]="/src/${HOST_DEPLOY#"$REPO_ROOT"/}"
        ;;
      "$REPO_ROOT")
        ARGS[$((i + 1))]="/src"
        ;;
      /*)
        mkdir -p "$HOST_DEPLOY"
        DEPLOY_MOUNT=(-v "$HOST_DEPLOY:$HOST_DEPLOY")
        ;;
    esac
  fi
done

BUILD_IMAGE="phoenix-cuo-build-jammy"

say "Building container image ($BUILD_IMAGE) pinned to Ubuntu 22.04 / glibc 2.35"

DOCKERFILE_CONTENT='FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        wget ca-certificates clang zlib1g-dev build-essential libicu-dev \
    && rm -rf /var/lib/apt/lists/*
RUN wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh
'

TMP_DOCKERFILE="$(mktemp)"
trap 'rm -f "$TMP_DOCKERFILE"' EXIT
printf '%s' "$DOCKERFILE_CONTENT" > "$TMP_DOCKERFILE"

"$ENGINE" build -f "$TMP_DOCKERFILE" -t "$BUILD_IMAGE" "$REPO_ROOT" \
  || die "container image build failed."

say "Running build-cuo.sh inside $BUILD_IMAGE (args: $*)"

# Build as a non-root uid matching the host so output files aren't root-owned.
HOST_UID="$(id -u)"
HOST_GID="$(id -g)"

"$ENGINE" run --rm \
  -v "$REPO_ROOT:/src" \
  "${DEPLOY_MOUNT[@]}" \
  -w /src \
  -e HOME=/tmp \
  "$BUILD_IMAGE" \
  bash -c "
    set -e
    dotnet --info >/dev/null
    bash build-cuo.sh ${ARGS[*]}
    chown -R $HOST_UID:$HOST_GID bin/dist 2>/dev/null || true
  " \
  || die "build inside container failed."

say "Done. Output in $REPO_ROOT/bin/dist (built against glibc 2.35 — Ubuntu 22.04+ compatible)."
