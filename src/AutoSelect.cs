using System.Diagnostics;
using System.Net;
using System.Text;

namespace SiberiaConnect;

/// <summary>
/// 1.2.0: режим «⚡️ Северное Сияние (Авто)».
/// Перед подключением одна временная копия Xray поднимает по входу на каждый сервер подписки,
/// и через каждый идёт реальный запрос — это честная проверка: проходит ли туннель у текущего провайдера, а не только пинг.
/// 1.4.1: три проверки на каждый узел идут одновременно за 1–3 секунды: generate_204 (внешний интернет), YouTube (204)
/// и cdn-cgi/trace Cloudflare (узел выпускает наружу — страна выхода не RU). Рабочий важнее быстрого.
/// Пока VPN включён (в любом режиме), каждые 15 секунд идёт проверка трафика; два провала подряд — ядро перезапускается
/// на том же сервере, не помогло — выбираем другой рабочий узел.
/// </summary>
public sealed partial class VpnCore
{
    public const string AutoGuid = "auto";
    public const string AutoName = "⚡️ Северное Сияние (Авто)";
    private const string ProbeUrl = "https://www.gstatic.com/generate_204";          // внешний интернет через узел
    private const string VerifyUrl = "https://www.youtube.com/generate_204";         // узел пропускает и YouTube
    private const string TraceUrl = "https://1.1.1.1/cdn-cgi/trace";                 // страна выхода (loc=…): не RU — трафик реально ушёл за границу
    private const int ProbeTimeoutMs = 2500;                                         // жёсткий таймаут запроса при выборе узла
    private const int HealthTimeoutMs = 3000;                                        // таймаут проверки живого туннеля
    private const int HealthPeriodSec = 15;                                          // как часто проверяем туннель

    /// <summary>Сервер, через который VPN подключён сейчас.</summary>
    public string ActiveGuid { get; private set; } = "";

    private readonly Dictionary<string, int> _pings = new();
    private readonly HashSet<string> _verified = new();   // узлы, где открывается и YouTube
    private readonly HashSet<string> _foreign = new();    // узлы, у которых выход за границей (loc != RU)
    private System.Threading.Timer _health;
    private int _healthBusy;
    private int _healthFails;
    private DateTime _lastFailover = DateTime.MinValue;

    /// <summary>Задержка последней проверки, мс. 0 — не проверяли, -1 — сервер не ответил.</summary>
    public int PingOf(string guid)
    {
        lock (_pings) return _pings.TryGetValue(guid, out var v) ? v : 0;
    }

    /// <summary>
    /// «Рабочий важнее быстрого». Рабочий узел — тот, через который generate_204 вернул именно 204 за 2,5 секунды.
    /// Среди рабочих лучшие — с выходом за границу и открытым YouTube, затем с выходом за границу, затем любые;
    /// внутри группы — самый быстрый (при почти равной скорости — тот, что выше в подписке).
    /// null — ни один узел не отдал 204: подключаться нельзя, интернета нет или узлы перегружены.
    /// </summary>
    private ServerItem PickAuto()
    {
        var list = State.Servers.ToList();
        if (list.Count == 0) return null;
        var res = Probe(list);
        HashSet<string> yt, abroad;
        lock (_pings) { yt = new HashSet<string>(_verified); abroad = new HashSet<string>(_foreign); }
        Log.Write("auto: " + string.Join(", ", list.Select(s =>
            $"{s.Name} = {(res[s.Guid] > 0 ? res[s.Guid] + " мс" + (abroad.Contains(s.Guid) ? " +вне RU" : "") + (yt.Contains(s.Guid) ? " +yt" : "") : "нет 204")}")));
        var ok = list.Select((s, i) => (s, i, ms: res[s.Guid])).Where(x => x.ms > 0).ToList();
        if (ok.Count == 0) return null;
        var best3 = ok.Where(x => abroad.Contains(x.s.Guid) && yt.Contains(x.s.Guid)).ToList();
        var best2 = ok.Where(x => abroad.Contains(x.s.Guid)).ToList();
        if (best3.Count > 0) ok = best3; else if (best2.Count > 0) ok = best2;
        var best = ok.Min(x => x.ms);
        var slack = Math.Max(20, best * 15 / 100);
        return ok.Where(x => x.ms <= best + slack).OrderBy(x => x.i).First().s;
    }

    /// <summary>Подключаемся к одному серверу: после старта ядра проверяем, что 204 действительно проходит (две попытки).</summary>
    private bool VerifyCore()
    {
        for (var i = 0; i < 2; i++)
            if (Measure(_httpPort, HealthTimeoutMs).GetAwaiter().GetResult() > 0) return true;
        return false;
    }

    private Dictionary<string, int> Probe(List<ServerItem> list)
    {
        var res = new Dictionary<string, int>();
        foreach (var s in list) res[s.Guid] = -1;
        if (list.Count == 0 || !File.Exists(Paths.Xray)) return res;

        var ports = new int[list.Count];
        var next = SocksPortBase + 300;
        for (var i = 0; i < list.Count; i++) { ports[i] = FreePort(next); next = ports[i] + 1; }

        string cfg;
        bool[] valid;
        try { cfg = XrayConfig.BuildProbe(list.Select(s => s.Uri).ToArray(), ports, out valid); }
        catch (Exception ex) { Log.Write("auto config: " + ex.Message); return res; }
        var first = Array.IndexOf(valid, true);
        if (first < 0) return res;

        var file = Path.Combine(Paths.Data, "probe-config.json");
        Process p = null;
        try
        {
            File.WriteAllText(file, cfg, new UTF8Encoding(false));
            var psi = new ProcessStartInfo(Paths.Xray, $"run -c \"{file}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Paths.CoreDir
            };
            psi.Environment["XRAY_LOCATION_ASSET"] = Paths.CoreDir;
            p = Process.Start(psi);
            if (p == null) return res;
            CoreJob.Add(p);
            p.OutputDataReceived += (_, _) => { };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Core("[auto] " + e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000 && !p.HasExited && !PortOpen(ports[first])) Thread.Sleep(50);
            try { File.Delete(file); } catch { }   // ключи на диске не держим
            if (p.HasExited) return res;

            // три проверки на каждый узел — все сразу, ничего не ждёт друг друга
            var n = list.Count;
            var tMs = new Task<int>[n];
            var tYt = new Task<bool>[n];
            var tGeo = new Task<bool>[n];
            for (var i = 0; i < n; i++)
            {
                tMs[i] = valid[i] ? Measure(ports[i], ProbeTimeoutMs) : Task.FromResult(-1);
                tYt[i] = valid[i] ? Verify(ports[i]) : Task.FromResult(false);
                tGeo[i] = valid[i] ? Trace(ports[i]) : Task.FromResult(false);
            }
            bool Done(int i) => tMs[i].IsCompleted && tYt[i].IsCompleted && tGeo[i].IsCompleted;
            bool Full(int i) => Done(i) && tMs[i].IsCompletedSuccessfully && tMs[i].Result > 0 && tYt[i].Result && tGeo[i].Result;

            // ждём все ответы (≤ 4 с), но как только есть узел, прошедший всё, даём остальным ещё 350 мс и идём дальше
            var wait = Stopwatch.StartNew();
            long goodAt = -1;
            while (wait.ElapsedMilliseconds < 4000)
            {
                var all = true;
                for (var i = 0; i < n; i++) if (!Done(i)) { all = false; break; }
                if (all) break;
                if (goodAt < 0) for (var i = 0; i < n; i++) if (Full(i)) { goodAt = wait.ElapsedMilliseconds; break; }
                if (goodAt >= 0 && wait.ElapsedMilliseconds - goodAt > 350) break;
                Thread.Sleep(30);
            }

            var snapshot = new Dictionary<string, int>();
            var yt = new HashSet<string>();
            var abroad = new HashSet<string>();
            for (var i = 0; i < n; i++)
            {
                var ms = tMs[i].IsCompletedSuccessfully ? tMs[i].Result : -1;
                res[list[i].Guid] = ms;
                snapshot[list[i].Guid] = ms;
                if (ms > 0 && tYt[i].IsCompletedSuccessfully && tYt[i].Result) yt.Add(list[i].Guid);
                if (ms > 0 && tGeo[i].IsCompletedSuccessfully && tGeo[i].Result) abroad.Add(list[i].Guid);
            }

            lock (_pings)
            {
                _pings.Clear();
                foreach (var kv in snapshot) _pings[kv.Key] = kv.Value;
                _verified.Clear();
                foreach (var g in yt) _verified.Add(g);
                _foreign.Clear();
                foreach (var g in abroad) _foreign.Add(g);
            }
        }
        catch (Exception ex) { Log.Write("auto probe: " + ex.Message); }
        finally
        {
            try { if (p != null && !p.HasExited) p.Kill(true); } catch { }
            try { p?.Dispose(); } catch { }
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        return res;
    }

    private static HttpClient ViaPort(int port, int timeoutMs)
    {
        var h = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{port}"), UseProxy = true };
        return new HttpClient(h, true) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
    }

    /// <summary>
    /// Задержка через туннель одним запросом (новое соединение: рукопожатие Reality + ответ) — одинаково честно для всех узлов.
    /// Рабочим считается только ответ 204 (страница-заглушка или перехват провайдера дают 200/302 — это провал).
    /// </summary>
    private static async Task<int> Measure(int port, int timeoutMs)
    {
        try
        {
            using var http = ViaPort(port, timeoutMs);
            var sw = Stopwatch.StartNew();
            using var r = await http.GetAsync(ProbeUrl);
            if (r.StatusCode != HttpStatusCode.NoContent) return -1;
            return Math.Max(1, (int)sw.ElapsedMilliseconds);
        }
        catch { return -1; }
    }

    /// <summary>Через узел открывается YouTube (generate_204 → 204).</summary>
    private static async Task<bool> Verify(int port)
    {
        try
        {
            using var http = ViaPort(port, ProbeTimeoutMs);
            using var r = await http.GetAsync(VerifyUrl);
            return r.StatusCode == HttpStatusCode.NoContent;
        }
        catch { return false; }
    }

    /// <summary>Трафик реально вышел за границу: cdn-cgi/trace отвечает с loc ≠ RU.</summary>
    private static async Task<bool> Trace(int port)
    {
        try
        {
            using var http = ViaPort(port, ProbeTimeoutMs);
            using var r = await http.GetAsync(TraceUrl);
            if (!r.IsSuccessStatusCode) return false;
            var text = await r.Content.ReadAsStringAsync();
            foreach (var line in text.Split('\n'))
            {
                if (!line.StartsWith("loc=", StringComparison.Ordinal)) continue;
                var loc = line[4..].Trim();
                return loc.Length == 2 && !loc.Equals("RU", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>Живой туннель пропускает трафик? Одно новое соединение через локальный прокси ядра, 204 за 3 секунды.</summary>
    private static async Task<bool> Ping204(int port)
    {
        try
        {
            using var http = ViaPort(port, HealthTimeoutMs);
            using var r = await http.GetAsync(ProbeUrl);
            return r.StatusCode == HttpStatusCode.NoContent;
        }
        catch { return false; }
    }

    /// <summary>Есть ли вообще интернет (мимо VPN, прямой запрос приложения): нужно, чтобы не рвать туннель, когда пропал Wi-Fi.</summary>
    private static async Task<bool> InternetUp()
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

    // ───────────── сторож туннеля, пока VPN включён (любой режим) ─────────────

    private void StartHealth()
    {
        StopHealth();
        _healthFails = 0;
        _health = new System.Threading.Timer(_ => HealthTick(), null, TimeSpan.FromSeconds(HealthPeriodSec), TimeSpan.FromSeconds(HealthPeriodSec));
    }

    private void StopHealth()
    {
        var t = _health;
        _health = null;
        try { t?.Dispose(); } catch { }
    }

    /// <summary>
    /// Каждые 15 секунд: новое соединение через туннель, 204 за 3 с. Два провала подряд (~30 с) — восстановление:
    /// 1) ядро перезапускается на том же сервере (1–2 с, адаптер и системный прокси остаются на месте);
    /// 2) не помогло и интернет есть — полное переподключение (в «Авто» с новым выбором рабочего узла).
    /// Нет интернета вообще (Wi-Fi пропал) — ждём, туннель не трогаем.
    /// </summary>
    private void HealthTick()
    {
        if (Interlocked.Exchange(ref _healthBusy, 1) == 1) return;
        try
        {
            if (!Running || Busy) return;
            if (Ping204(_httpPort).GetAwaiter().GetResult()) { _healthFails = 0; return; }
            if (++_healthFails < 2) return;
            _healthFails = 0;
            Log.Write("watchdog: туннель не пропускает трафик (2 проверки подряд) — восстанавливаю");
            if (SoftRestartCore() && Ping204(_httpPort).GetAwaiter().GetResult())
            {
                Log.Write("watchdog: ядро перезапущено на том же сервере — трафик идёт");
                return;
            }
            if (!InternetUp().GetAwaiter().GetResult())
            {
                Log.Write("watchdog: интернета нет совсем — жду, туннель не трогаю");
                return;
            }
            Log.Write(State.AutoSelect && State.Servers.Count > 1
                ? "watchdog: перезапуск не помог — выбираю другой рабочий узел"
                : "watchdog: перезапуск не помог — подключаюсь заново");
            Reconnect();
        }
        catch (Exception ex) { Log.Write("watchdog: " + ex.Message); }
        finally { Interlocked.Exchange(ref _healthBusy, 0); }
    }

    /// <summary>
    /// Перезапуск только ядра Xray на том же сервере и на тех же локальных портах — адаптер «Весь компьютер»
    /// и системный прокси смотрят туда же и ничего не замечают. false — не вышло (или порты сменились), нужен полный перезапуск.
    /// </summary>
    private bool SoftRestartCore()
    {
        lock (_gate)
        {
            if (!Running || Busy) return false;
            var srv = State.Servers.FirstOrDefault(s => s.Guid == ActiveGuid);
            if (srv == null) return false;
            var socks0 = _socksPort; var http0 = _httpPort;
            Busy = true;
            try
            {
                var err = StartCore(srv, true);
                if (err != null) err = StartCore(srv, false);
                if (err != null) { Log.Write("watchdog: мягкий перезапуск ядра не удался: " + err); return false; }
                if (_socksPort != socks0 || _httpPort != http0)
                {
                    Log.Write("watchdog: локальные порты ядра сменились — нужен полный перезапуск");
                    return false;
                }
                return true;
            }
            catch (Exception ex) { Log.Write("watchdog: " + ex.Message); return false; }
            finally { Busy = false; }
        }
    }

    /// <summary>
    /// Ядро упало само — не чаще раза в минуту подключаемся заново (в «Авто» — с новым выбором узла, иначе к выбранному серверу).
    /// </summary>
    private bool TryFailover()
    {
        if (DateTime.UtcNow - _lastFailover < TimeSpan.FromMinutes(1)) return false;
        _lastFailover = DateTime.UtcNow;
        Log.Write("watchdog: движок остановился — подключаюсь заново");
        Task.Run(Connect);
        return true;
    }
}
