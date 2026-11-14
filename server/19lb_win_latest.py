# ───────────── 19lb_win_latest (win_latest_v1): постоянная ссылка на свежий Windows-установщик ─────────────
# GET /app/dl/SiberiaConnect-Setup-latest.exe — тот же файл и те же заголовки, что у /app/dl/SiberiaConnect-Setup.exe
# (имя при скачивании — SiberiaConnect-Setup-<версия из win.json>.exe). Раздача читает файл с диска при каждом запросе,
# поэтому после scripts/publish_win.sh ссылка сразу отдаёт новую версию — перезапуск бота не нужен.
# Модуль кладётся в /opt/sibirconnect/app/ после 19l_win_beta.py (порядок по имени файла); dl_win объявлен в 19a_download.py.

_wl_routes0 = miniapp_routes


def miniapp_routes(app):     # noqa: F811
    _wl_routes0(app)
    app.router.add_get("/app/dl/SiberiaConnect-Setup-latest.exe", dl_win)


log.info("19lb_win_latest: /app/dl/SiberiaConnect-Setup-latest.exe")
