#!/usr/bin/env bash
# Автоматическая выкладка Windows-клиента: GitHub-релиз → сервер.
#
# Каждые 5 минут (sc-win-sync.timer) смотрит последний релиз репозитория. Если версия новее той, что в dl/win.json,
# скачивает установщик и update.json и передаёт их в publish_win.sh, который сам проверяет SHA-256 и подпись ECDSA
# закрытым ключом UPDATE_KEY (он есть только в GitHub Secrets). Подделанный или чужой релиз не пройдёт проверку.
# Секретов и входа на сервер из GitHub не требуется: сервер сам забирает публичный релиз.
#
#   scripts/sync_win_from_github.sh            — один проход (его и запускает таймер)
# Переменные: GH_REPO (по умолчанию servicebyelmar/siberia-connect-desktop), DL_DIR.
set -euo pipefail

REPO="${GH_REPO:-servicebyelmar/siberia-connect-desktop}"
DL="${DL_DIR:-/opt/sibirconnect/webapp/dl}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

mkdir -p /run/lock
exec 9>/run/lock/sc-win-sync.lock
flock -n 9 || exit 0

API="$(curl -fsS --max-time 20 -H 'Accept: application/vnd.github+json' "https://api.github.com/repos/$REPO/releases/latest" 2>/dev/null || true)"
[ -n "$API" ] || exit 0   # GitHub недоступен или релизов ещё нет — до следующего раза

# tag; затем три ссылки (установщик, update.json, portable) — только с github.com/<repo>/releases/download/
read -r TAG EXE_URL JSON_URL PORT_URL < <(printf '%s' "$API" | REPO="$REPO" python3 -c '
import json, os, re, sys
d = json.load(sys.stdin)
tag = d.get("tag_name", "")
pre = "https://github.com/" + os.environ["REPO"] + "/releases/download/"
exe = js = port = "-"
for a in d.get("assets", []):
    n, u = a.get("name", ""), a.get("browser_download_url", "")
    if not u.startswith(pre):
        continue
    if re.fullmatch(r"SiberiaConnect-Setup-\d+\.\d+\.\d+\.exe", n): exe = u
    elif n == "update.json": js = u
    elif re.fullmatch(r"SiberiaConnect-Portable-\d+\.\d+\.\d+\.zip", n): port = u
print(tag or "-", exe, js, port)
')
VER="${TAG#v}"
[[ "$VER" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || exit 0
[ "$EXE_URL" != "-" ] && [ "$JSON_URL" != "-" ] || { echo "релиз $TAG без установщика или update.json — пропускаю"; exit 0; }

CUR=""
[ -f "$DL/win.json" ] && CUR="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("version",""))' "$DL/win.json" 2>/dev/null || true)"
if [ -n "$CUR" ] && [ "$(printf '%s\n%s\n' "$CUR" "$VER" | sort -V | tail -1)" = "$CUR" ]; then
  exit 0   # на сервере уже такая же или более новая версия
fi

echo "найден релиз $TAG (на сервере: ${CUR:-—}) — скачиваю"
T="$(mktemp -d /root/sc_sync.XXXXXX)"; trap 'rm -rf "$T"' EXIT
curl -fL --max-time 900 -o "$T/SiberiaConnect-Setup-$VER.exe" "$EXE_URL"
curl -fL --max-time 60  -o "$T/update.json" "$JSON_URL"
[ "$PORT_URL" = "-" ] || curl -fL --max-time 900 -o "$T/SiberiaConnect-Portable-$VER.zip" "$PORT_URL"

bash "$HERE/publish_win.sh" "$T" --if-newer
