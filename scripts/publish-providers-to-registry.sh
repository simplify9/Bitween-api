#!/usr/bin/env bash
# Builds the data source providers (RabbitMQ, SQS, PostgreSQL, MySQL, SQL Server, Oracle) and
# publishes each to the Bitween Registry, where it waits for review and then appears on every
# Bitween's Marketplace tab. They are published as Simplify9 under their own ids (bitween.db.oracle),
# which data sources refer to.
#
#   scripts/publish-providers-to-registry.sh <version> [bitween]
#
# <version>       the version to publish, as the CLI release's
# [bitween]       the bitween CLI to build with; bitween on the PATH when left out
# REGISTRY_URL    the registry, https://registry.bitween.io
# REGISTRY_TOKEN  a publish token for the s9 publisher
# SIGNING_KEY     the s9 Ed25519 private key in PEM (or SIGNING_KEY_FILE, a path to it)
#
# A version already in the registry is skipped, so the script can run again after a failure.
# Needs openssl 3, curl, unzip and jq.
set -euo pipefail

version="${1:?usage: publish-providers-to-registry.sh <version> [bitween]}"
cli="${2:-bitween}"
url="${REGISTRY_URL:?set REGISTRY_URL}"
token="${REGISTRY_TOKEN:?set REGISTRY_TOKEN}"
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [[ -n "${SIGNING_KEY:-}" ]]; then
  printf '%s\n' "$SIGNING_KEY" > "$work/key.pem"
  key="$work/key.pem"
else
  key="${SIGNING_KEY_FILE:?set SIGNING_KEY or SIGNING_KEY_FILE}"
fi
key_id="$(openssl pkey -in "$key" -pubout -outform DER | openssl dgst -sha256 -r | cut -c1-16)"

failed=0
for project in SW.Bitween.Adapters.Bus.RabbitMq SW.Bitween.Adapters.Bus.Sqs SW.Bitween.Adapters.Db.PostgreSql \
               SW.Bitween.Adapters.Db.MySql SW.Bitween.Adapters.Db.SqlServer SW.Bitween.Adapters.Db.Oracle; do
  manifest="$root/$project/adapter.json"
  id="$(jq -r .id "$manifest")"
  cp "$manifest" "$work/adapter.json.original"
  jq --arg v "$version" '.version = $v' "$work/adapter.json.original" > "$manifest"
  "$cli" adapter build "$root/$project" -o "$work/dist" > /dev/null
  cp "$work/adapter.json.original" "$manifest"

  package="$work/dist/$id-$version.zip"
  sha256="$(openssl dgst -sha256 -r "$package" | cut -d' ' -f1)"
  printf 'bitween-registry/v1/publish\n%s\n%s\n%s' "$id" "$version" "$sha256" > "$work/message"
  signature="$(openssl pkeyutl -sign -inkey "$key" -rawin -in "$work/message" | openssl base64 -A)"

  status="$(curl -sS -o "$work/response" -w '%{http_code}' -H "Authorization: Bearer $token" \
    -F "package=@$package;type=application/zip" -F "keyId=$key_id" -F "signature=$signature" \
    "${url%/}/v1/packages/$id/versions/$version")"
  case "$status" in
    202) echo "$id $version is in review." ;;
    409) echo "$id $version is already in the registry." ;;
    422) echo "$id $version was refused:" >&2
         jq -r '.checks[] | select(.outcome == "Failed") | "  \(.name): \(.detail)"' "$work/response" >&2
         failed=1 ;;
    *)   echo "$id $version: the registry answered $status" >&2; cat "$work/response" >&2; failed=1 ;;
  esac
done
exit "$failed"
