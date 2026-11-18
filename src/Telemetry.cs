using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SiberiaConnect;

/// <summary>
/// 1.7.0: чистая логика фоновой телеметрии (без сети, файлов и состояния приложения) — её проверяет CI (--selftest-telemetry).
/// Правило одно: отчёт уходит только о РЕАЛЬНОМ сбое VPN. Обычного интернета нет, пользователь сам нажал «Отключить»,
/// ПК только что проснулся, та же причина уже отправлялась недавно — молчим.
/// </summary>
internal static class TelemetryGate
{
    public const string TunnelTimeout = "TUNNEL_TIMEOUT";     // туннель не поднялся / не отдал трафик при подключении
    public const string DeviceLocked = "DEVICE_LOCKED";       // 409: ключ привязан к другому устройству
    public const string NoTraffic = "NO_TRAFFIC";             // туннель поднят, трафика нет (заморозка / блокировка протокола)
    public const string SessionDropped = "SESSION_DROPPED";   // движок неожиданно остановился
    // AUTH_FAILED с Windows не шлём: при 401 токен уже недействителен, а отчёт без токена сервер не принимает.

    public static readonly string[] Reasons = { TunnelTimeout, DeviceLocked, NoTraffic, SessionDropped };

    public const double DedupSeconds = 600;           // не чаще 1 отчёта по одной причине раз в 10 минут
    public const double AfterSleepSeconds = 10;       // первые 10 секунд после выхода из сна — шум сети
    public const double UserDisconnectQuietSeconds = 30;
    public const int MaxLogLines = 25;
    public const int MaxLineLength = 300;

    /// <summary>
    /// null — отправлять можно; иначе короткий код причины отказа (в лог приложения, без секретов).
    /// secSince* — секунд с события; double.PositiveInfinity — события не было.
    /// </summary>
    public static string Decide(string reason, bool baseNetOk, double secSinceWake, double secSinceUserDisconnect, double secSinceLastSame)
    {
        if (Array.IndexOf(Reasons, reason ?? "") < 0) return "bad_reason";
        if (!baseNetOk) return "no_base_net";
        if (secSinceUserDisconnect < UserDisconnectQuietSeconds) return "user_disconnect";
        if (secSinceWake < AfterSleepSeconds) return "after_sleep";
        if (secSinceLastSame < DedupSeconds) return "duplicate";
        return null;
    }

    /// <summary>Журнал отправок по причинам (в памяти процесса; сервер дедуплицирует ещё раз).</summary>
    public sealed class Dedup
    {
        private readonly Dictionary<string, DateTime> _last = new Dictionary<string, DateTime>();
        public double SecondsSince(string reason, DateTime now)
        {
            DateTime t;
            return _last.TryGetValue(reason ?? "", out t) ? (now - t).TotalSeconds : double.PositiveInfinity;
        }

        public void Mark(string reason, DateTime now)
        {
            _last[reason ?? ""] = now;
        }
    }

    // Шаблоны и подстановки (индекс в индекс); регистр не важен. Порядок важен: сначала ссылки и ключи, потом общие маски.
    private static readonly string[] RedactPatterns =
    {
        @"\b(?:vless|vmess|trojan|ss|hysteria2?|tuic)://\S+",
        @"https?://\S*(?:sub|key|token)\S*",
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        @"\bSC-[A-Z0-9]{4,5}(?:-[A-Z0-9]{4,5}){1,2}\b",
        @"(x-app-token|authorization|bearer|token|secret|password|pass|key)\s*[:=]\s*\S+",
        @"[\w.+-]+@[\w-]+\.[\w.-]+",
        @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
        @"\b(?:[0-9a-f]{1,4}:){3,7}[0-9a-f]{1,4}\b",
        @"([a-z]:\\Users\\)[^\\\s]+",
        @"\b[0-9a-f]{20,}\b",
        @"\b[A-Za-z0-9_-]{32,}\b",
    };
    private static readonly string[] RedactReplacements =
    {
        "[link]", "[url]", "[uuid]", "[sc-key]", "$1=[hidden]", "[email]", "[ip]", "[ipv6]", "$1[user]", "[hex]", "[token]",
    };
    private static readonly Regex[] RedactRx = RedactPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase)).ToArray();

    /// <summary>Последние ≤25 строк, каждая ≤300 символов, без ссылок/UUID/токенов/ключей/e-mail/IP/имени пользователя Windows.</summary>
    public static string[] CleanLog(IEnumerable<string> lines)
    {
        var all = (lines ?? Array.Empty<string>()).ToList();
        var tail = all.Skip(Math.Max(0, all.Count - MaxLogLines));
        var res = new List<string>();
        foreach (var raw in tail)
        {
            var s = (raw ?? "");
            if (s.Length > 2000) s = s.Substring(0, 2000);
            for (var i = 0; i < RedactRx.Length; i++) s = RedactRx[i].Replace(s, RedactReplacements[i]);
            if (s.Length > MaxLineLength) s = s.Substring(0, MaxLineLength);
            res.Add(s);
        }
        return res.ToArray();
    }
}

/// <summary>1.7.0: фоновая отправка отчётов о сбоях VPN. Ничего не показывает пользователю, не блокирует подключение и не бросает исключений.</summary>
internal static class Telemetry
{
    private static readonly string[] Hosts = { MainForm.Host, "sibir-cloud-api.site" };   // основной и резервный адреса

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = Pinning.Check
    }) { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly object Gate = new();
    private static readonly TelemetryGate.Dedup Sent = new();
    private static DateTime _resumeUtc = DateTime.MinValue;
    private static DateTime _userDisconnectUtc = DateTime.MinValue;
    private static readonly HashSet<string> InFlight = new();

    /// <summary>Сервер принял отчёт: код обращения INC-XXXXXX (для кнопки «Написать в поддержку»).</summary>
    public static event Action<string> Incident;

    public static void NoteResume() { lock (Gate) _resumeUtc = DateTime.UtcNow; }
    public static void NoteUserDisconnect() { lock (Gate) _userDisconnectUtc = DateTime.UtcNow; }

    private static double Secs(DateTime t) => t == DateTime.MinValue ? double.PositiveInfinity : (DateTime.UtcNow - t).TotalSeconds;

    /// <summary>
    /// Поставить отчёт в очередь. delayMs — для сбоев при уже поднятом туннеле: ждём, пока watchdog переподключится,
    /// чтобы проверка «обычный интернет есть» шла мимо мёртвого туннеля.
    /// </summary>
    public static void Report(VpnCore core, string reason, string location, int delayMs)
    {
        try
        {
            if (core == null || string.IsNullOrEmpty(core.State.AuthToken)) return;
            var token = core.State.AuthToken;
            var did = core.DeviceId ?? "";
            lock (Gate) { if (!InFlight.Add(reason)) return; }
            Task.Run(async () =>
            {
                try { await Send(token, did, reason, location, delayMs); }
                catch (Exception ex) { Log.Write("telemetry: " + ex.Message); }
                finally { lock (Gate) InFlight.Remove(reason); }
            });
        }
        catch (Exception ex) { Log.Write("telemetry: " + ex.Message); }
    }

    private static async Task Send(string token, string did, string reason, string location, int delayMs)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        double wake, user, last;
        lock (Gate) { wake = Secs(_resumeUtc); user = Secs(_userDisconnectUtc); last = Sent.SecondsSince(reason, DateTime.UtcNow); }
        // дешёвые проверки до сетевых (base_net_ok считаем истинным, чтобы отсеять остальное)
        var pre = TelemetryGate.Decide(reason, true, wake, user, last);
        if (pre != null) { Log.Write("telemetry: " + reason + " не отправлен (" + pre + ")"); return; }
        var net = await BaseNetOk();
        lock (Gate) { wake = Secs(_resumeUtc); user = Secs(_userDisconnectUtc); last = Sent.SecondsSince(reason, DateTime.UtcNow); }
        var why = TelemetryGate.Decide(reason, net, wake, user, last);
        if (why != null) { Log.Write("telemetry: " + reason + " не отправлен (" + why + ")"); return; }
        lock (Gate) Sent.Mark(reason, DateTime.UtcNow);

        var body = new JsonObject
        {
            ["reason"] = reason,
            ["base_net_ok"] = true,
            ["user_initiated"] = false,
            ["did"] = did,
            ["platform"] = "windows",
            ["app_ver"] = AppInfo.Version,
            ["location"] = location ?? "",
            ["log"] = new JsonArray(TelemetryGate.CleanLog(TailLog(TelemetryGate.MaxLogLines)).Select(l => (JsonNode)JsonValue.Create(l)).ToArray())
        };
        if (!double.IsPositiveInfinity(wake) && wake < 3600) body["since_wake_s"] = Math.Round(wake);

        foreach (var host in Hosts)
        {
            if (AppInstance.DenyRemote("telemetry", host)) continue;     // режим экземпляра: боевой сервер не трогаем
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/app/api/v1/telemetry")
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("User-Agent", $"SiberiaConnectWin/{AppInfo.Version}");
                req.Headers.TryAddWithoutValidation("X-App-Token", token);
                using var resp = await Http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                if ((int)resp.StatusCode == 401) return;                  // токен недействителен — повторять незачем
                if (!resp.IsSuccessStatusCode) continue;                  // другой адрес может ответить
                var obj = JsonNode.Parse(text) as JsonObject;
                var code = Api.S(obj, "incident");
                Log.Write("telemetry: " + reason + " принят" + (string.IsNullOrEmpty(code) ? "" : " " + code));
                if (Api.B(obj, "accepted") && !string.IsNullOrEmpty(code)) Incident?.Invoke(code);
                return;
            }
            catch (Exception ex) { Log.Write("telemetry " + host + ": " + ex.Message); }
        }
    }

    /// <summary>
    /// Работает ли обычный интернет: TCP 443 к нейтральным 1.1.1.1 / 8.8.8.8 и connecttest Microsoft (без прокси). Достаточно одного ответа.
    /// В режиме «Весь компьютер» при мёртвом туннеле эти запросы тоже упираются в туннель — тогда отчёт не уходит (это безопасная сторона).
    /// </summary>
    internal static async Task<bool> BaseNetOk()
    {
        var pending = new List<Task<bool>> { ProbeTcp("1.1.1.1", 443), ProbeTcp("8.8.8.8", 443), ProbeHttp() };
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            if (done.Result) return true;
        }
        return false;
    }

    private static async Task<bool> ProbeTcp(string ip, int port)
    {
        try
        {
            using var c = new TcpClient();
            using var cts = new CancellationTokenSource(2500);
            await c.ConnectAsync(IPAddress.Parse(ip), port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> ProbeHttp()
    {
        try
        {
            using var h = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(h) { Timeout = TimeSpan.FromMilliseconds(2500) };
            using var r = await http.GetAsync("http://www.msftconnecttest.com/connecttest.txt");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string[] TailLog(int n)
    {
        try
        {
            var path = Path.Combine(Paths.Logs, "app.log");
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var q = new Queue<string>();
            string line;
            while ((line = sr.ReadLine()) != null) { q.Enqueue(line); if (q.Count > n) q.Dequeue(); }
            return q.ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
}

public sealed partial class VpnCore
{
    /// <summary>Название сервера для отчёта: текущий активный, иначе выбранный, иначе «Авто».</summary>
    internal string LocationName()
    {
        var s = State.Servers.FirstOrDefault(x => x.Guid == ActiveGuid)
                ?? State.Servers.FirstOrDefault(x => x.Guid == State.Selected);
        return s?.Name ?? (State.AutoSelect ? "Авто" : "");
    }

    /// <summary>Сообщить о реальном сбое VPN (фильтры и отправка — в Telemetry; вызов дешёвый и не блокирует).</summary>
    internal void ReportIncident(string reason, int delayMs) => Telemetry.Report(this, reason, LocationName(), delayMs);

    /// <summary>Отключение по действию пользователя (кнопка, трей): ближайшие секунды сбоем не считаются.</summary>
    public void UserDisconnect()
    {
        Telemetry.NoteUserDisconnect();
        Disconnect();
    }
}
