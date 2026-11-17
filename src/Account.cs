using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>1.3.0: запросы к центру авторизации (/app/api/v1/…). Только наш сервер, TLS с закреплением корня.</summary>
internal static class Api
{
    private static readonly string Base = $"https://{MainForm.Host}/app/api/v1/";

    private static HttpClient Make(bool proxy) => new(new HttpClientHandler
    {
        UseProxy = proxy,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = Pinning.Check
    }) { Timeout = TimeSpan.FromSeconds(6) };

    private static readonly HttpClient Direct = Make(false);
    private static readonly HttpClient ViaSystem = Make(true);

    /// <summary>(HTTP-код, ответ). Код 0 — нет связи (таймаут / сеть). Тело читается и при 4xx/5xx (409 DEVICE_LOCKED несёт поля).</summary>
    public static (int code, JsonObject body) Post(string path, JsonObject body, string token = null) =>
        Send(HttpMethod.Post, path, body, token, 0);

    /// <summary>
    /// 1.6.0: общий запрос. totalMs &gt; 0 — жёсткий общий срок на все попытки (прямой путь и системный прокси), иначе таймаут клиента (6 с на попытку).
    /// </summary>
    public static (int code, JsonObject body) Send(HttpMethod method, string path, JsonObject body, string token, int totalMs)
    {
        using var cts = totalMs > 0 ? new CancellationTokenSource(totalMs) : null;
        foreach (var http in new[] { Direct, ViaSystem })
        {
            if (cts != null && cts.IsCancellationRequested) break;
            try
            {
                using var req = new HttpRequestMessage(method, Base + path);
                if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("User-Agent", $"SiberiaConnectWin/{AppInfo.Version}");
                if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-App-Token", token);
                using var resp = cts != null ? http.Send(req, cts.Token) : http.Send(req);
                using var s = resp.Content.ReadAsStream();
                using var r = new StreamReader(s, Encoding.UTF8);
                var text = r.ReadToEnd();
                JsonObject obj = null;
                try { obj = JsonNode.Parse(text) as JsonObject; } catch { }
                return ((int)resp.StatusCode, obj ?? new JsonObject());
            }
            catch (Exception ex) { Log.Write($"api {path}: {ex.Message}"); }
        }
        return (0, null);
    }

    public static string S(JsonNode n, string k)
    {
        try { return n?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""; } catch { return ""; }
    }

    /// <summary>Число из JSON (целое или дробное); нет поля — 0.</summary>
    public static long L(JsonNode n, string k)
    {
        try
        {
            if (n?[k] is not JsonValue v) return 0;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<double>(out var d)) return (long)d;
        }
        catch { }
        return 0;
    }

    public static bool B(JsonNode n, string k)
    {
        try { return n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b; } catch { return false; }
    }
}

/// <summary>
/// 1.3.0: «ВАУ-вход». Первый запуск без ключа — аккаунт создаётся сам (секрет установки хранится только здесь,
/// зашифрован DPAPI), пробный день — сразу. Перед подключением приложение спрашивает единый статус:
/// подписка кончилась / заблокирован / нет мест — понятное сообщение вместо бесконечного «Подключаю»;
/// ключ сбросили — новый подтягивается сам.
/// </summary>
public sealed partial class VpnCore
{
    /// <summary>Последний ответ статуса (state, message, app_only, …) — для меню и интерфейса.</summary>
    public JsonObject Account { get; private set; }

    /// <summary>Токен входа поменялся (вход/выход/связка) — интерфейс кладёт его в кабинет.</summary>
    public event Action<string> TokenChanged;

    public bool NeedsAutoRegister => !HasSub && string.IsNullOrEmpty(State.AuthToken) && !State.AutoRegDone;

    // ───────────── 1.6.0: привязка ключа к устройству (ACTIVATION_V2) ─────────────
    /// <summary>
    /// Ключ привязан к другому ПК/телефону (сервер ответил 409 DEVICE_LOCKED или REBIND_AVAILABLE).
    /// Это НЕ отзыв: токен, ключ и серверы остаются, пока сервер явно не скажет 401/403.
    /// Поля: code, deviceName, canRebind, retryAfter (сек), rebindAt (unix), message.
    /// </summary>
    public JsonObject Lock { get; private set; }

    private static bool IsLockCode(string c) => c is "DEVICE_LOCKED" or "REBIND_AVAILABLE";

    /// <summary>Сессия отозвана явно: 401/403 или коды KEY_EXPIRED / DEVICE_MISMATCH. Сеть, таймаут, 5xx, 429 — НЕ отзыв.</summary>
    private static bool IsRevoked(int code, JsonObject r)
    {
        if (code is 401 or 403) return true;
        var c = Api.S(r, "code");
        return c is "KEY_EXPIRED" or "DEVICE_MISMATCH";
    }

    /// <summary>«через 2 дн. 22 ч», «через 5 ч 10 мин», «через 12 мин».</summary>
    internal static string FormatLeft(long sec)
    {
        if (sec <= 0) return "несколько минут";
        var d = sec / 86400; var h = sec % 86400 / 3600; var m = sec % 3600 / 60;
        if (d > 0) return h > 0 ? $"{d} дн. {h} ч" : $"{d} дн.";
        if (h > 0) return m > 0 ? $"{h} ч {m} мин" : $"{h} ч";
        return $"{Math.Max(1, m)} мин";
    }

    /// <summary>Разобрать ответ как блокировку привязки (поля на верхнем уровне или в status). true — это блокировка, Lock выставлен.</summary>
    private bool TryLock(int code, JsonObject r)
    {
        if (r == null) return false;
        JsonNode src = r;
        var c = Api.S(r, "code");
        if (!IsLockCode(c) && r["status"] is JsonObject s && IsLockCode(Api.S(s, "code"))) { src = s; c = Api.S(s, "code"); }
        if (!IsLockCode(c)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var retry = Api.L(src, "retry_after");
        var at = Api.L(src, "rebind_available_at");
        var can = c == "REBIND_AVAILABLE" || Api.B(src, "confirm_required") || (retry <= 0 && at > 0 && at <= now);
        var left = retry > 0 ? retry : Math.Max(0, at - now);
        var name = Api.S(src, "bound_device_name");
        var msg = can
            ? "Ключ привязан к другому устройству. Его можно перенести на этот ПК."
            : "Ключ привязан к другому устройству. Перенос на этот ПК станет доступен через " + FormatLeft(left);
        Lock = new JsonObject
        {
            ["code"] = c, ["deviceName"] = name, ["canRebind"] = can, ["retryAfter"] = left,
            ["rebindAt"] = at > 0 ? at : now + left, ["message"] = msg
        };
        Log.Write($"auth: {c} — ключ привязан к «{name}», перенос {(can ? "доступен" : "через " + FormatLeft(left))}; вход и ключ не трогаю");
        return true;
    }

    private JsonObject LockEvent() => new()
    {
        ["type"] = "account", ["state"] = "device_locked", ["message"] = Api.S(Lock, "message"),
        ["canRebind"] = Api.B(Lock, "canRebind"), ["retryAfter"] = Api.L(Lock, "retryAfter"),
        ["rebindAt"] = Api.L(Lock, "rebindAt"), ["deviceName"] = Api.S(Lock, "deviceName")
    };

    /// <summary>Снимок блокировки для страницы (null — нет).</summary>
    public JsonNode LockSnapshot() => Lock?.DeepClone();

    private string InstallSecret()
    {
        if (string.IsNullOrEmpty(State.InstallSecret))
        {
            State.InstallSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            State.Save();
        }
        return State.InstallSecret;
    }

    /// <summary>Вход по секрету установки: новый аккаунт (первый запуск) или тот же/связанный. true — получен токен.</summary>
    public bool AutoRegister()
    {
        var (code, r) = Api.Post("device", new JsonObject
        {
            ["secret"] = InstallSecret(), ["did"] = DeviceId, ["platform"] = "windows"
        });
        if (TryLock(code, r)) return false;     // 409 DEVICE_LOCKED: ключ привязан к другому устройству — ничего не стираем
        if (code != 200 || r == null || !Api.B(r, "ok"))
        {
            Log.Write($"auth: вход по устройству не удался ({code}) {Api.S(r, "error")}");
            if (code == 429 || code == 400) { State.AutoRegDone = true; State.Save(); }   // не долбим сервер
            return false;
        }
        var token = Api.S(r, "token");
        if (string.IsNullOrEmpty(token)) return false;
        State.AuthToken = token;
        State.AutoRegDone = true;
        State.TokenFromInstall = true;
        var lc = Api.S(r, "code");
        if (!string.IsNullOrEmpty(lc) && !lc.Contains('•')) State.LoginCode = lc;
        State.Save();
        Lock = null;
        Log.Write("auth: вход по устройству" + (Api.B(r, "new") ? " — создан новый аккаунт" : ""));
        TokenChanged?.Invoke(token);
        if (r["status"] is JsonObject st) ApplyStatus(st);
        return true;
    }

    /// <summary>1.5.0: полный выход (ручной, 401, аккаунт удалён) — для интерфейса: закрыть окна, очистить страницу.</summary>
    public event Action<string> LoggedOut;

    /// <summary>
    /// Кабинет вошёл или вышел. 1.5.0: приложение — источник истины. Отозванный токен (DeadToken) обратно не принимаем,
    /// выход кабинета = полный выход приложения.
    /// </summary>
    public void AdoptToken(string token)
    {
        token = (token ?? "").Trim();
        if (token.Length > 100 || token == (State.AuthToken ?? "")) return;
        if (token == "") { Logout("кабинет"); return; }
        if (token == State.DeadToken) { Log.Write("auth: страница вернула отозванный токен — игнорирую"); return; }
        State.AuthToken = token;
        State.AutoRegDone = true;
        State.TokenFromInstall = false;   // вход из кабинета (Telegram или код) — по секрету установки его не подменяем
        State.DeadToken = "";
        State.Save();
        Log.Write("auth: вход из кабинета");
        Task.Run(() => { try { CheckStatus(); } catch (Exception ex) { Log.Write("auth: " + ex.Message); } });   // сразу берём ключ этого входа
    }

    /// <summary>
    /// 1.5.0: атомарный выход — VPN выключается, токен, ключ, список серверов и конфиги ядра стираются одним сохранением.
    /// Секрет установки остаётся (это не вход), автосоздание гостя не повторяется.
    /// </summary>
    public void Logout(string reason)
    {
        var old = State.AuthToken ?? "";
        if (Running) Disconnect();
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(old)) State.DeadToken = old;
            State.AuthToken = "";
            State.TokenFromInstall = false;
            State.AutoRegDone = true;
            State.LoginCode = "";
            State.SubUrl = "";
            State.Servers = new List<ServerItem>();
            State.Selected = "";
            State.LastUpdate = DateTime.MinValue;
            State.Save();
            Account = null;
            Lock = null;
            ActiveGuid = "";
        }
        lock (_pings) { _pings.Clear(); _verified.Clear(); _foreign.Clear(); }
        lock (_frozen) _frozen.Clear();
        foreach (var f in new[] { Paths.CoreConfig, Paths.TunConfig }) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        Log.Write("auth: выход (" + reason + ") — вход, ключ и серверы очищены");
        LoggedOut?.Invoke(reason);
    }

    /// <summary>Единый статус. null — проверить не удалось (нет связи или нет входа): подключаемся как раньше.</summary>
    public JsonObject CheckStatus()
    {
        if (string.IsNullOrEmpty(State.AuthToken)) return null;
        Lock = null;    // блокировку определяет только свежий ответ сервера: при сетевой ошибке старую не держим
        var body = new JsonObject { ["did"] = DeviceId, ["platform"] = "windows", ["device_name"] = Environment.MachineName };
        var (code, r) = Api.Post("status", body, State.AuthToken);
        if (TryLock(code, r)) return null;      // 409 DEVICE_LOCKED / REBIND_AVAILABLE: сессию, ключ и серверы не стираем
        if (IsRevoked(code, r))
        {
            // явный отказ сервера (401/403, KEY_EXPIRED, DEVICE_MISMATCH): сессия истекла или аккаунт связали с Telegram —
            // заходим заново по секрету установки
            if (State.TokenFromInstall && !string.IsNullOrEmpty(State.InstallSecret) && AutoRegister()) return Account;
            if (Lock != null) return null;     // автовход упёрся в привязку устройства — это не отзыв
            Logout("сессия закончилась");
            return null;
        }
        // нет связи (0), таймаут, 5xx, 429 и прочее — временная ошибка: ничего не стираем, подключаемся как раньше
        if (code != 200 || r?["status"] is not JsonObject st) return null;
        Lock = null;
        if (Api.S(st, "state") == "relogin" && State.TokenFromInstall && !string.IsNullOrEmpty(State.InstallSecret))
            return AutoRegister() ? Account : null;
        ApplyStatus(st);
        return st;
    }

    private void ApplyStatus(JsonObject st)
    {
        Account = st;
        if (Api.S(st, "state") != "active") return;
        var link = Api.S(st, "link");
        if (!string.IsNullOrEmpty(link) && !IsAllowedSubUrl(link))
            Log.Write("auth: сервер выдал ссылку на неизвестный адрес — игнорирую: " + (Uri.TryCreate(link, UriKind.Absolute, out var bad) ? bad.Host : "?"));
        if (IsAllowedSubUrl(link) && link != State.SubUrl)
        {
            Log.Write("auth: сервер выдал новый ключ устройства — обновляю");
            UpdateSub(link);
        }
    }

    /// <summary>Перед подключением: null — можно подключаться, иначе текст, почему нельзя.</summary>
    private string AccountGate()
    {
        JsonObject st;
        var had = !string.IsNullOrEmpty(State.AuthToken);
        try { st = CheckStatus(); }
        catch (Exception ex) { Log.Write("auth: " + ex.Message); return null; }
        if (had && string.IsNullOrEmpty(State.AuthToken)) return "Вход закончился — войди снова";   // 1.5.0: 401 → выход, старым ключом не подключаемся
        if (Lock != null)       // 1.6.0: ключ привязан к другому устройству — не подключаемся, показываем перенос
        {
            Changed?.Invoke(LockEvent());
            return Api.S(Lock, "message");
        }
        if (st == null) return null;
        var state = Api.S(st, "state");
        if (state is "expired" or "blocked" or "device_limit" or "no_account")
        {
            var msg = Api.S(st, "message");
            Changed?.Invoke(new JsonObject { ["type"] = "account", ["state"] = state, ["message"] = msg });
            if (state == "no_account") Logout("аккаунт не найден");
            return string.IsNullOrEmpty(msg) ? "Нет активной подписки" : msg;
        }
        return null;
    }

    /// <summary>
    /// 1.5.0: пока VPN включён — раз в 3 минуты спрашиваем статус. Ключ перевыпустили — ставим новый сами (UpdateSub
    /// переподключит, если старого сервера больше нет); подписка кончилась / заблокирован — выключаем VPN с понятной причиной,
    /// а не держим «Подключено» без трафика; 401 — полный выход.
    /// </summary>
    internal void WatchStatus()
    {
        if (string.IsNullOrEmpty(State.AuthToken) || Busy) return;
        var st = CheckStatus();
        if (st == null)
        {
            if (Lock != null)       // 1.6.0: ключ перенесли на другое устройство — этот ПК больше не привязан
            {
                Log.Write("status: ключ привязан к другому устройству — выключаю VPN");
                if (Running) Disconnect();
                Changed?.Invoke(LockEvent());
                FireError(Api.S(Lock, "message"));
            }
            return;
        }
        var state = Api.S(st, "state");
        if (state is "expired" or "blocked" or "device_limit" or "no_account")
        {
            var msg = Api.S(st, "message");
            Log.Write($"status: {state} — выключаю VPN");
            if (Running) Disconnect();
            Changed?.Invoke(new JsonObject { ["type"] = "account", ["state"] = state, ["message"] = msg });
            FireError(string.IsNullOrEmpty(msg) ? "Нет активной подписки" : msg);
            if (state == "no_account") Logout("аккаунт не найден");
        }
    }

    /// <summary>
    /// 1.5.0: умный ввод — ключ vless://, ссылка подписки, код VPN-XXXX-XXXX, код восстановления SC-XXXXX-XXXXX или номер подписки.
    /// Сервер сам определяет тип (/v1/redeem). Номер — с подтверждением в Telegram (опрос до 5 минут).
    /// progress: {type:'redeem', stage:'busy|wait_tg|ok|error', message}.
    /// </summary>
    public void Redeem(string input, Action<JsonObject> progress)
    {
        JsonObject Ev(string stage, string msg) => new() { ["type"] = "redeem", ["stage"] = stage, ["message"] = msg ?? "" };
        input = (input ?? "").Trim();
        if (input.Length == 0) { progress(Ev("error", "Вставь ключ, код или номер подписки")); return; }
        progress(Ev("busy", ""));
        var body = new JsonObject { ["input"] = input, ["did"] = DeviceId, ["platform"] = "windows", ["device_name"] = Environment.MachineName };
        var (code, r) = Api.Post("redeem", body);
        if (code == 0) { progress(Ev("error", "Нет связи с сервером. Проверь интернет и повтори")); return; }
        if (r?["need"] is JsonValue && Api.S(r, "need") == "telegram")
        {
            var req = Api.S(r, "req");
            progress(Ev("wait_tg", Api.S(r, "message")));
            var until = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(2000);
                var (c2, r2) = Api.Post("redeem/poll", new JsonObject { ["req"] = req });
                if (c2 == 0 || r2 == null || r2["wait"] is JsonValue) continue;
                code = c2; r = r2;
                break;
            }
            if (r?["wait"] is JsonValue) { progress(Ev("error", "Время на подтверждение вышло — попробуй ещё раз")); return; }
        }
        var tok = Api.S(r, "token");
        if (code != 200 || string.IsNullOrEmpty(tok))
        {
            var err = Api.S(r, "error");
            progress(Ev("error", string.IsNullOrEmpty(err) ? "Не удалось войти — проверь, что вставил ключ или код целиком" : err));
            return;
        }
        lock (_gate)
        {
            State.AuthToken = tok;
            State.TokenFromInstall = false;
            State.AutoRegDone = true;
            State.DeadToken = "";
            State.Save();
        }
        Log.Write($"auth: вход через умный ввод ({Api.S(r, "kind")})");
        TokenChanged?.Invoke(tok);
        var link = Api.S(r, "link");
        if (IsAllowedSubUrl(link) && link != State.SubUrl) UpdateSub(link);
        else { try { CheckStatus(); } catch { } }
        var st = Api.S(r, "state");
        progress(Ev("ok", st is "" or "active" ? "Готово — можно подключаться" : Api.S(r, "message")));
    }

    /// <summary>
    /// 1.6.0: «Перенести ключ на этот ПК» — POST /v1/rebind {confirm:true, device_id, device_name}. Сервер отзывает конфиг старого
    /// устройства и выдаёт ссылку подписки этому. true — перенесено (вызывающий включает подключение).
    /// progress: {type:'rebind', stage:'busy|ok|error', message}.
    /// </summary>
    public bool Rebind(Action<JsonObject> progress)
    {
        JsonObject Ev(string stage, string msg) => new() { ["type"] = "rebind", ["stage"] = stage, ["message"] = msg ?? "" };
        if (string.IsNullOrEmpty(State.AuthToken)) { progress(Ev("error", "Сначала войди в приложение")); return false; }
        progress(Ev("busy", ""));
        var body = new JsonObject
        {
            ["confirm"] = true, ["device_id"] = DeviceId, ["device_name"] = Environment.MachineName, ["platform"] = "windows"
        };
        var (code, r) = Api.Post("rebind", body, State.AuthToken);
        if (code == 0) { progress(Ev("error", "Нет связи с сервером. Проверь интернет и повтори")); return false; }
        if (TryLock(code, r))       // ещё рано (DEVICE_LOCKED): 72 часа не прошли
        {
            progress(Ev("error", Api.S(Lock, "message")));
            Changed?.Invoke(LockEvent());
            return false;
        }
        if (IsRevoked(code, r))
        {
            progress(Ev("error", "Сессия закончилась — войди снова"));
            Logout("сессия закончилась");
            return false;
        }
        var link = Api.S(r, "link");
        if (code != 200 || !Api.B(r, "ok") || string.IsNullOrEmpty(link))
        {
            var err = Api.S(r, "error");
            Log.Write($"rebind: не удалось ({code}) {Api.S(r, "code")} {err}");
            progress(Ev("error", string.IsNullOrEmpty(err) ? "Не удалось перенести ключ — попробуй ещё раз через минуту" : err));
            return false;
        }
        Lock = null;
        Log.Write("rebind: ключ перенесён на этот ПК");
        if (IsAllowedSubUrl(link))
        {
            var u = UpdateSub(link);
            if (!Api.B(u, "ok"))
            {
                progress(Ev("error", Api.S(u, "error") is { Length: > 0 } e ? e : "Ключ перенесён, но список серверов не загрузился — нажми «Обновить ключ»"));
                return false;
            }
        }
        else { try { CheckStatus(); } catch { } }
        progress(Ev("ok", "Ключ перенесён на этот ПК"));
        return true;
    }

    /// <summary>Код для связки с Telegram: (код, ссылка на бота) или текст ошибки.</summary>
    public (string code, string url, string error) LinkCode()
    {
        if (string.IsNullOrEmpty(State.AuthToken)) return (null, null, "Сначала войди в приложение");
        var (c, r) = Api.Post("link", new JsonObject(), State.AuthToken);
        if (c == 200 && r?["ok"]?.GetValue<bool>() == true) return (Api.S(r, "code"), Api.S(r, "url"), null);
        return (null, null, Api.S(r, "error") is { Length: > 0 } e ? e : "Нет связи с сервером");
    }
}
