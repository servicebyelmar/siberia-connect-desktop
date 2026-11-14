#!/usr/bin/env bash
# Публикация нового Windows-клиента Siberia Connect на сервере.
#
#   scripts/publish_win.sh <папка-с-артефактом> [--notes "что нового"] [--min] [--force]
#
# В папке — то, что скачано из GitHub Actions («SiberiaConnect-Windows-X.Y.Z»):
#   SiberiaConnect-Setup-X.Y.Z.exe и update.json (version, sha256, sig).
#
# Что делает:
#   1. сверяет SHA-256 установщика с update.json;
#   2. проверяет подпись ECDSA (та же, что проверяет приложение) открытым ключом scripts/update_pub.pem;
#   3. не даёт откатить версию назад (без --force);
#   4. кладёт резервную копию текущих файлов в dl.bak_win_<время>;
#   5. атомарно заменяет SiberiaConnect-Setup.exe, кладёт SiberiaConnect-Setup-X.Y.Z.exe и ссылку SiberiaConnect-Setup-latest.exe;
#   6. в самом конце атомарно пишет win.json — с этого момента сайт, /app/dl/, бот и приложения видят новую версию;
#   7. проверяет раздачу по публичным адресам.
# Перезапуск бота не нужен: win.json и установщик читаются с диска при каждом запросе.
# version.json (Android) не трогается. min.json меняется только с --min (принудительное обновление до этой версии).
set -euo pipefail

DL="${DL_DIR:-/opt/sibirconnect/webapp/dl}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUB="${UPDATE_PUBKEY:-$HERE/update_pub.pem}"
CHECK_URLS="${CHECK_URLS:-https://sibirapp.duckdns.org/app/dl https://sibir-cloud-api.site/dl}"

SRC=""; NOTES=""; SET_MIN=0; FORCE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --notes) NOTES="${2:-}"; shift 2 ;;
    --min)   SET_MIN=1; shift ;;
    --force) FORCE=1; shift ;;
    -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
    *) if [ -z "$SRC" ]; then SRC="$1"; shift; else echo "лишний аргумент: $1" >&2; exit 2; fi ;;
  esac
done
[ -n "$SRC" ] && [ -d "$SRC" ] || { echo "Укажи папку с артефактом: $0 <папка>" >&2; exit 2; }
for c in openssl python3 sha256sum base64 curl; do command -v "$c" >/dev/null || { echo "нет команды $c" >&2; exit 2; }; done
[ -r "$PUB" ] || { echo "нет открытого ключа: $PUB" >&2; exit 2; }
[ -d "$DL" ] || { echo "нет каталога раздачи: $DL" >&2; exit 2; }

JSON="$SRC/update.json"
[ -r "$JSON" ] || { echo "нет $JSON" >&2; exit 2; }
EXES=("$SRC"/SiberiaConnect-Setup-*.exe)
[ "${#EXES[@]}" -eq 1 ] && [ -f "${EXES[0]}" ] || { echo "в $SRC должен быть ровно один SiberiaConnect-Setup-*.exe" >&2; exit 2; }
EXE="${EXES[0]}"

read -r VER SHA SIG < <(python3 - "$JSON" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
print(d["version"], d["sha256"].lower(), d["sig"])
PY
)
[[ "$VER" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "странная версия: $VER" >&2; exit 1; }
[[ "$SHA" =~ ^[0-9a-f]{64}$ ]] || { echo "странный sha256 в update.json" >&2; exit 1; }
[ "$(basename "$EXE")" = "SiberiaConnect-Setup-$VER.exe" ] || { echo "имя файла не совпадает с версией $VER" >&2; exit 1; }

echo "== 1. SHA-256"
HAVE="$(sha256sum "$EXE" | cut -d' ' -f1)"
[ "$HAVE" = "$SHA" ] || { echo "FAIL: sha256 файла $HAVE ≠ $SHA из update.json" >&2; exit 1; }
SIZE="$(stat -c %s "$EXE")"
echo "OK  $VER  $SIZE байт  $SHA"

echo "== 2. подпись ECDSA"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
printf 'SiberiaConnect-Windows|%s|%s' "$VER" "$SHA" > "$T/msg"
printf '%s' "$SIG" | base64 -d > "$T/sig.bin"
openssl dgst -sha256 -verify "$PUB" -signature "$T/sig.bin" "$T/msg" >/dev/null \
  || { echo "FAIL: подпись не подходит к открытому ключу — публикация отменена" >&2; exit 1; }
echo "OK  подпись верна"

echo "== 3. версия"
CUR=""; [ -f "$DL/win.json" ] && CUR="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("version",""))' "$DL/win.json" 2>/dev/null || true)"
echo "сейчас на сервере: ${CUR:-—}, публикуем: $VER"
if [ -n "$CUR" ] && [ "$FORCE" -eq 0 ]; then
  [ "$(printf '%s\n%s\n' "$CUR" "$VER" | sort -V | tail -1)" = "$VER" ] && [ "$CUR" != "$VER" ] \
    || { echo "FAIL: $VER не новее $CUR (нужен --force для отката или повторной выкладки)" >&2; exit 1; }
fi

echo "== 4. резервная копия"
BK="${DL}.bak_win_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$BK"
for f in win.json SiberiaConnect-Setup.exe min.json; do [ -e "$DL/$f" ] && cp -a "$DL/$f" "$BK/" || true; done
echo "OK  $BK"

echo "== 5. файлы"
REF="$DL/win.json"; [ -e "$REF" ] || REF="$DL"
cp "$EXE" "$DL/.SiberiaConnect-Setup-$VER.exe.tmp"
cp "$EXE" "$DL/.SiberiaConnect-Setup.exe.tmp"
chmod 644 "$DL/.SiberiaConnect-Setup-$VER.exe.tmp" "$DL/.SiberiaConnect-Setup.exe.tmp"
chown --reference="$REF" "$DL/.SiberiaConnect-Setup-$VER.exe.tmp" "$DL/.SiberiaConnect-Setup.exe.tmp" 2>/dev/null || true
mv -f "$DL/.SiberiaConnect-Setup-$VER.exe.tmp" "$DL/SiberiaConnect-Setup-$VER.exe"
mv -f "$DL/.SiberiaConnect-Setup.exe.tmp" "$DL/SiberiaConnect-Setup.exe"
ln -sfn "SiberiaConnect-Setup-$VER.exe" "$DL/.latest.tmp"
mv -T -f "$DL/.latest.tmp" "$DL/SiberiaConnect-Setup-latest.exe"
# оставляем три последних версионных файла для отката
ls -1t "$DL"/SiberiaConnect-Setup-[0-9]*.exe 2>/dev/null | tail -n +4 | xargs -r rm -f --
echo "OK  SiberiaConnect-Setup.exe, SiberiaConnect-Setup-$VER.exe, SiberiaConnect-Setup-latest.exe"

echo "== 6. win.json (последним: после него версию видят все)"
python3 - "$DL" "$VER" "$SHA" "$SIG" "$SIZE" "$NOTES" "$SET_MIN" <<'PY'
import json, os, sys
dl, ver, sha, sig, size, notes, set_min = sys.argv[1:8]
d = {"version": ver, "sha256": sha, "sig": sig, "size": int(size)}
if notes:
    d["notes"] = notes
tmp = os.path.join(dl, ".win.json.tmp")
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(d, f, ensure_ascii=False)
os.chmod(tmp, 0o644)
os.replace(tmp, os.path.join(dl, "win.json"))
if set_min == "1":
    p = os.path.join(dl, "min.json")
    try:
        m = json.load(open(p, encoding="utf-8"))
    except Exception:
        m = {}
    m["win"] = ver
    t2 = p + ".tmp"
    with open(t2, "w", encoding="utf-8") as f:
        json.dump(m, f, ensure_ascii=False)
    os.chmod(t2, 0o644)
    os.replace(t2, p)
PY
chown --reference="$REF" "$DL/win.json" 2>/dev/null || true
echo "OK  win.json → $VER$( [ "$SET_MIN" -eq 1 ] && echo ', min.json → win='"$VER" )"

echo "== 7. проверка раздачи"
BAD=0
for base in $CHECK_URLS; do
  got="$(curl -fsS --max-time 15 "$base/win.json?$(date +%s)" 2>/dev/null | python3 -c 'import json,sys;print(json.load(sys.stdin).get("version",""))' 2>/dev/null || true)"
  if [ "$got" = "$VER" ]; then echo "OK  $base/win.json → $got"; else echo "WARN $base/win.json → '${got:-нет ответа}' (ожидали $VER)"; BAD=1; fi
  csum="$(curl -fsS --max-time 300 "$base/SiberiaConnect-Setup.exe" 2>/dev/null | sha256sum | cut -d' ' -f1 || true)"
  if [ "$csum" = "$SHA" ]; then echo "OK  $base/SiberiaConnect-Setup.exe sha256 совпал"; else echo "WARN $base/SiberiaConnect-Setup.exe sha256 ≠ ожидаемому"; BAD=1; fi
done
if [ "$BAD" -ne 0 ]; then
  echo "Часть адресов не показала новую версию. Откат: cp -a $BK/win.json $BK/SiberiaConnect-Setup.exe $DL/" >&2
  exit 3
fi
echo "ГОТОВО: Windows $VER опубликован. Бот перезапускать не нужно."
