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

    /// <summary>Кабинет вошёл или вышел — запоминаем его токен (один аккаунт на всё приложение).</summary>
    public void AdoptToken(string token)
    {
        token = (token ?? "").Trim();
        if (token.Length > 100 || token == (State.AuthToken ?? "")) return;
        State.AuthToken = token;
        State.AutoRegDone = true;
        State.TokenFromInstall = false;   // вход из кабинета (Telegram или код) — по секрету установки его не подменяем
        if (token == "") Account = null;
        State.Save();
        Log.Write(token == "" ? "auth: выход из кабинета" : "auth: вход из кабинета");
    }

    /// <summary>Единый статус. null — проверить не удалось (нет связи или нет входа): подключаемся как раньше.</summary>
    public JsonObject CheckStatus()
    {
        if (string.IsNullOrEmpty(State.AuthToken)) return null;
        var body = new JsonObject { ["did"] = DeviceId, ["platform"] = "windows" };
        var (code, r) = Api.Post("status", body, State.AuthToken);
        if (code == 401)
        {
            // сессия истекла или аккаунт связали с Telegram: заходим заново по секрету установки
            if (State.TokenFromInstall && !string.IsNullOrEmpty(State.InstallSecret) && AutoRegister()) return Account;
            State.AuthToken = "";
            State.Save();
            TokenChanged?.Invoke("");
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
        try { st = CheckStatus(); }
        catch (Exception ex) { Log.Write("auth: " + ex.Message); return null; }
        if (st == null) return null;
        var state = Api.S(st, "state");
        if (state is "expired" or "blocked" or "device_limit" or "no_account")
        {
            var msg = Api.S(st, "message");
            Changed?.Invoke(new JsonObject { ["type"] = "account", ["state"] = state, ["message"] = msg });
            return string.IsNullOrEmpty(msg) ? "Нет активной подписки" : msg;
        }
        return null;
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
