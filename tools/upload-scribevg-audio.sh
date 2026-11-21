#!/usr/bin/env bash
# Uploads a packed audio tree (m4a files, manifest.json, CREDITS.txt) to
# https://ppdac.ltd/scribevg-audio/ on cosmic2. Needs an `az login` with access
# to the 'cosmic' subscription. The SAS URL is generated at run time and never
# written to git.
#   tools/upload-scribevg-audio.sh path/to/scribevg-audio.tgz
set -euo pipefail
TGZ=$(realpath "$1")
ACCOUNT=${ACCOUNT:-magneticstorage}
RG=${RG:-cosmic}
VM=${VM:-cosmic2}
CONTAINER=scribevg-transfer
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

BLOB="scribevg-audio-$(date -u +%Y%m%d%H%M%S).tgz"
cp "$TGZ" "$WORK/$BLOB"

az storage container create --account-name "$ACCOUNT" -n "$CONTAINER" --public-access off --auth-mode key -o none --only-show-errors
az storage blob upload --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" -f "$WORK/$BLOB" --overwrite --auth-mode key -o none --only-show-errors
EXP=$(date -u -d '+45 minutes' +%Y-%m-%dT%H:%MZ)
SAS=$(az storage blob generate-sas --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" --permissions r --expiry "$EXP" --https-only --auth-mode key -o tsv --only-show-errors)

# Run-command scripts run under sh: no brace expansion, start with cd /tmp.
# The SAS is only interpolated into this remote script, never printed here.
cat > "$WORK/deploy.sh" <<SCRIPT
cd /tmp
set -e
curl -fsS -o /tmp/scribevg-audio.tgz 'https://$ACCOUNT.blob.core.windows.net/$CONTAINER/$BLOB?$SAS'
rm -rf /tmp/scribevg-audio-new
mkdir /tmp/scribevg-audio-new
tar -xzf /tmp/scribevg-audio.tgz -C /tmp/scribevg-audio-new
test -f /tmp/scribevg-audio-new/manifest.json
test -f /tmp/scribevg-audio-new/CREDITS.txt
rm -rf /var/www/scribevg-audio.old
if [ -d /var/www/scribevg-audio ]; then mv /var/www/scribevg-audio /var/www/scribevg-audio.old; fi
mv /tmp/scribevg-audio-new /var/www/scribevg-audio
chown -R root:root /var/www/scribevg-audio
find /var/www/scribevg-audio -type d -exec chmod 755 {} +
find /var/www/scribevg-audio -type f -exec chmod 644 {} +
rm -rf /var/www/scribevg-audio.old /tmp/scribevg-audio.tgz
du -sh /var/www/scribevg-audio
find /var/www/scribevg-audio -name '*.m4a' | wc -l
SCRIPT
az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript --scripts @"$WORK/deploy.sh" --query "value[0].message" -o tsv
az storage blob delete --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" --auth-mode key -o none --only-show-errors
az storage container delete --account-name "$ACCOUNT" -n "$CONTAINER" --auth-mode key -o none --only-show-errors

echo "Upload finished. Smoke-test https://ppdac.ltd/scribevg-audio/ after nginx is configured."
