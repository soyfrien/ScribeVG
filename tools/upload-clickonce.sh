#!/usr/bin/env bash
# Uploads a ClickOnce build (clickonce.tgz from tools/publish-clickonce.ps1) to
# https://ppdac.ltd/scribevg/ on cosmic2. Needs an `az login` with access to the
# 'cosmic' subscription. The SAS URL is generated at run time and never written to git.
#   tools/upload-clickonce.sh path/to/clickonce.tgz
set -euo pipefail
TGZ=$(realpath "$1")
REPO=$(cd "$(dirname "$0")/.." && pwd)
ACCOUNT=${ACCOUNT:-magneticstorage}
RG=${RG:-cosmic}
VM=${VM:-cosmic2}
CONTAINER=scribevg-transfer
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

mkdir "$WORK/site"
tar -xzf "$TGZ" -C "$WORK/site"
VER=$(grep -o '<assemblyIdentity name="ScribeVG.application" version="[^"]*"' "$WORK/site/ScribeVG.application" | sed 's/.*version="//; s/"//')
echo "Deployment version $VER"
sed "s/__VERSION__/$VER/" "$REPO/pages/index.html" > "$WORK/site/index.html"
cp "$REPO/ScribeVG/Assets/ScribeVG.svg" "$WORK/site/icon.svg"
BLOB="scribevg-site-$VER.tgz"
tar -czf "$WORK/$BLOB" -C "$WORK/site" .

az storage container create --account-name "$ACCOUNT" -n "$CONTAINER" --public-access off --auth-mode key -o none --only-show-errors
az storage blob upload --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" -f "$WORK/$BLOB" --overwrite --auth-mode key -o none --only-show-errors
EXP=$(date -u -d '+45 minutes' +%Y-%m-%dT%H:%MZ)
SAS=$(az storage blob generate-sas --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" --permissions r --expiry "$EXP" --https-only --auth-mode key -o tsv --only-show-errors)

cat > "$WORK/deploy.sh" <<SCRIPT
cd /tmp
set -e
curl -fsS -o /tmp/scribevg-site.tgz 'https://$ACCOUNT.blob.core.windows.net/$CONTAINER/$BLOB?$SAS'
rm -rf /tmp/scribevg-new
mkdir /tmp/scribevg-new
tar -xzf /tmp/scribevg-site.tgz -C /tmp/scribevg-new
test -f /tmp/scribevg-new/ScribeVG.application
rm -rf /var/www/scribevg.old
if [ -d /var/www/scribevg ]; then mv /var/www/scribevg /var/www/scribevg.old; fi
mv /tmp/scribevg-new /var/www/scribevg
chown -R root:root /var/www/scribevg
find /var/www/scribevg -type d -exec chmod 755 {} +
find /var/www/scribevg -type f -exec chmod 644 {} +
rm -rf /var/www/scribevg.old /tmp/scribevg-site.tgz
du -sh /var/www/scribevg
SCRIPT
az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript --scripts @"$WORK/deploy.sh" --query "value[0].message" -o tsv
az storage blob delete --account-name "$ACCOUNT" -c "$CONTAINER" -n "$BLOB" --auth-mode key -o none --only-show-errors
az storage container delete --account-name "$ACCOUNT" -n "$CONTAINER" --auth-mode key -o none --only-show-errors

curl -s -o /dev/null -w '%{http_code} %{content_type} index\n' https://ppdac.ltd/scribevg/
curl -s https://ppdac.ltd/scribevg/ScribeVG.application | grep -o 'name="ScribeVG.application" version="[^"]*"'
