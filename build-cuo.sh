#!/usr/bin/env bash
###############################################################################
# build-cuo.sh — Linux twin of build-cuo.ps1.
#
# Builds the cuo native shared library (NativeAOT) for the Phoenix BootstrapHost
# use case: publishes src/ClassicUO.Client with -p:BootstrapHostMode=true, which
# flips the project to OutputType=Library / NativeLib=Shared / PublishAot=true /
# AssemblyName=cuo, producing a NativeAOT shared library whose
# [UnmanagedCallersOnly] 'Initialize' export publishes the HostBindings/
# ClientBindings tables. Output -> bin/dist (with SDL3/FAudio/FNA3D/zlib natives).
#
# The library name is platform-specific: cuo.so (linux), cuo.dll (win),
# cuo.dylib (osx). Default RID linux-x64.
#
# Prereqs (NativeAOT on Linux): clang, zlib1g-dev, and the usual build toolchain.
#
# Usage:
#   bash build-cuo.sh [--rid <rid>] [--config <cfg>] [--deploy-to <dir>] [--custom-login-scene]
# Defaults: --rid linux-x64  --config Release
# CUSTOM_LOGIN_SCENE=1 env var (or --custom-login-scene) opts into the custom
# 1280x720 login scene by passing -p:CustomLoginScene=true through to publish.
###############################################################################
set -euo pipefail

RID="linux-x64"
CONFIG="Release"
DEPLOY=""
CUSTOM_LOGIN_SCENE="${CUSTOM_LOGIN_SCENE:-0}"

while [ $# -gt 0 ]; do
  case "$1" in
    --rid)       RID="$2"; shift 2 ;;
    --config)    CONFIG="$2"; shift 2 ;;
    --deploy-to) DEPLOY="$2"; shift 2 ;;
    --custom-login-scene) CUSTOM_LOGIN_SCENE=1; shift 1 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mFAIL: %s\033[0m\n' "$*" >&2; exit 1; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_PROJECT="$REPO_ROOT/src/ClassicUO.Client/ClassicUO.Client.csproj"
OUTPUT_DIR="$REPO_ROOT/bin/dist"

[ -f "$CLIENT_PROJECT" ] || die "Client project not found at $CLIENT_PROJECT — run from the ClassicUO repo root."

# NativeAOT shared-lib name per platform.
case "$RID" in
  win-*) LIB="cuo.dll" ;;
  osx-*) LIB="cuo.dylib" ;;
  *)     LIB="cuo.so" ;;
esac
CUO_LIB="$OUTPUT_DIR/$LIB"

say "Building $LIB (BootstrapHostMode)"
echo "    project : $CLIENT_PROJECT"
echo "    rid     : $RID"
echo "    config  : $CONFIG"
echo "    output  : $OUTPUT_DIR"

PUBLISH_ARGS=(-c "$CONFIG" -r "$RID" -p:BootstrapHostMode=true -p:StripSymbols=true -o "$OUTPUT_DIR")

if [ "$CUSTOM_LOGIN_SCENE" = "1" ]; then
  echo "    login   : custom (CustomLoginScene=true)"
  PUBLISH_ARGS+=(-p:CustomLoginScene=true)
fi

dotnet publish "$CLIENT_PROJECT" "${PUBLISH_ARGS[@]}" \
  || die "dotnet publish failed."

[ -f "$CUO_LIB" ] || die "build reported success but $CUO_LIB was not produced."

say "$LIB built"
ls -la "$CUO_LIB"

if [ -n "$DEPLOY" ]; then
  mkdir -p "$DEPLOY"
  cp "$CUO_LIB" "$DEPLOY/"
  say "Deployed $LIB -> $DEPLOY"
fi
