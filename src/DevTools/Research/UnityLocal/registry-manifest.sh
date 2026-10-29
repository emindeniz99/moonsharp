#!/usr/bin/env bash
# Prints the manifest JSON of a unityci/editor image from Docker Hub, without Docker: the layer digests
# to download when no Docker daemon is available (README.md).
#
# Usage: registry-manifest.sh <tag or sha256 digest>
set -euo pipefail
if [ $# -ne 1 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
TOKEN=$(curl -sS --max-time 30 "https://auth.docker.io/token?service=registry.docker.io&scope=repository:unityci/editor:pull" | python3 -I -c "import json,sys;print(json.load(sys.stdin)['token'])")
curl -sS --max-time 60 -H "Authorization: Bearer $TOKEN" -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json" "https://registry-1.docker.io/v2/unityci/editor/manifests/$1"
