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

    /// <summary>(HTTP-код, ответ). Код 0 — нет связи.</summary>
    public static (int code, JsonObject body) Post(string path, JsonObject body, string token = null)
    {
        foreach (var http in new[] { Direct, ViaSystem })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Base + path)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("User-Agent", $"SiberiaConnectWin/{AppInfo.Version}");
                if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-App-Token", token);
                using var resp = http.Send(req);
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
        if (code != 200 || r == null || r["ok"]?.GetValue<bool>() != true)
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
        Log.Write("auth: вход по устройству" + (r["new"]?.GetValue<bool>() == true ? " — создан новый аккаунт" : ""));
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
        var body = new JsonObject { ["did"] = DeviceId, ["platform"] = "windows", ["device_name"] = Environment.MachineName };
        var (code, r) = Api.Post("status", body, State.AuthToken);
        if (code == 401)
        {
            // сессия истекла или аккаунт связали с Telegram: заходим заново по секрету установки
            if (State.TokenFromInstall && !string.IsNullOrEmpty(State.InstallSecret) && AutoRegister()) return Account;
            Logout("сессия закончилась");
            return null;
        }
        if (code != 200 || r?["status"] is not JsonObject st) return null;
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
        if (st == null) return;
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

    /// <summary>Код для связки с Telegram: (код, ссылка на бота) или текст ошибки.</summary>
    public (string code, string url, string error) LinkCode()
    {
        if (string.IsNullOrEmpty(State.AuthToken)) return (null, null, "Сначала войди в приложение");
        var (c, r) = Api.Post("link", new JsonObject(), State.AuthToken);
        if (c == 200 && r?["ok"]?.GetValue<bool>() == true) return (Api.S(r, "code"), Api.S(r, "url"), null);
        return (null, null, Api.S(r, "error") is { Length: > 0 } e ? e : "Нет связи с сервером");
    }
}
