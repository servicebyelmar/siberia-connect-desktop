using System.Diagnostics;
using System.Net;
using System.Text;

namespace SiberiaConnect;

/// <summary>
/// 1.2.0: режим «⚡️ Северное Сияние (Авто)».
/// Перед подключением одна временная копия Xray поднимает по входу на каждый сервер подписки,
/// и через каждый идёт реальный запрос (generate_204) — это честная проверка: проходит ли туннель
/// у текущего провайдера, а не только пинг. Побеждает самый быстрый; при почти равной скорости —
/// тот, что выше в подписке (сервер ставит первыми менее загруженные ноды).
/// Пока VPN включён, раз в минуту проверяем связь; два провала подряд и рядом есть живой сервер — переключаемся.
/// </summary>
public sealed partial class VpnCore
{
    public const string AutoGuid = "auto";
    public const string AutoName = "⚡️ Северное Сияние (Авто)";
    private const string ProbeUrl = "https://www.gstatic.com/generate_204";

    /// <summary>Сервер, через который VPN подключён сейчас.</summary>
    public string ActiveGuid { get; private set; } = "";

    private readonly Dictionary<string, int> _pings = new();
    private System.Threading.Timer _health;
    private int _healthBusy;
    private int _healthFails;
    private DateTime _lastFailover = DateTime.MinValue;

    /// <summary>Задержка последней проверки, мс. 0 — не проверяли, -1 — сервер не ответил.</summary>
    public int PingOf(string guid)
    {
        lock (_pings) return _pings.TryGetValue(guid, out var v) ? v : 0;
    }

    private ServerItem PickAuto()
    {
        var list = State.Servers.ToList();
        var res = Probe(list);
        Log.Write("auto: " + string.Join(", ", list.Select(s => $"{s.Name} = {(res[s.Guid] > 0 ? res[s.Guid] + " мс" : "нет связи")}")));
        var ok = list.Select((s, i) => (s, i, ms: res[s.Guid])).Where(x => x.ms > 0).ToList();
        if (ok.Count == 0)
        {
            // никто не ответил — скорее всего, пропал сам интернет; берём прежний сервер
            return list.FirstOrDefault(s => s.Guid == ActiveGuid)
                   ?? list.FirstOrDefault(s => s.Guid == State.Selected)
                   ?? list[0];
        }
        var best = ok.Min(x => x.ms);
        var slack = Math.Max(20, best * 15 / 100);
        return ok.Where(x => x.ms <= best + slack).OrderBy(x => x.i).First().s;
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
            while (sw.ElapsedMilliseconds < 5000 && !p.HasExited && !PortOpen(ports[first])) Thread.Sleep(100);
            try { File.Delete(file); } catch { }   // ключи на диске не держим
            if (p.HasExited) return res;
            Thread.Sleep(150);

            var tasks = new Task<int>[list.Count];
            for (var i = 0; i < list.Count; i++) tasks[i] = valid[i] ? Measure(ports[i]) : Task.FromResult(-1);
            Task.WaitAll(tasks, 9000);
            var snapshot = new Dictionary<string, int>();
            for (var i = 0; i < list.Count; i++)
            {
                var ms = tasks[i].IsCompletedSuccessfully ? tasks[i].Result : -1;
                res[list[i].Guid] = ms;
                snapshot[list[i].Guid] = ms;
            }
            lock (_pings)
            {
                _pings.Clear();
                foreach (var kv in snapshot) _pings[kv.Key] = kv.Value;
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

    /// <summary>Реальная задержка через туннель: первый запрос поднимает соединение, второй меряет время ответа.</summary>
    private static async Task<int> Measure(int port)
    {
        try
        {
            using var h = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{port}"), UseProxy = true };
            using var http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(6) };
            var sw = Stopwatch.StartNew();
            using (var r1 = await http.GetAsync(ProbeUrl))
                if ((int)r1.StatusCode >= 400) return -1;
            var firstMs = (int)sw.ElapsedMilliseconds;
            sw.Restart();
            try
            {
                using var r2 = await http.GetAsync(ProbeUrl);
                if ((int)r2.StatusCode >= 400) return Math.Max(1, firstMs);
            }
            catch { return Math.Max(1, firstMs); }
            return Math.Max(1, (int)sw.ElapsedMilliseconds);
        }
        catch { return -1; }
    }

    // ───────────── контроль связи, пока VPN включён (только в режиме «Авто») ─────────────

    private void StartHealth()
    {
        StopHealth();
        if (!State.AutoSelect || State.Servers.Count < 2) return;
        _healthFails = 0;
        _health = new System.Threading.Timer(_ => HealthTick(), null, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(60));
    }

    private void StopHealth()
    {
        var t = _health;
        _health = null;
        try { t?.Dispose(); } catch { }
    }

    private void HealthTick()
    {
        if (Interlocked.Exchange(ref _healthBusy, 1) == 1) return;
        try
        {
            if (!Running || Busy || !State.AutoSelect) return;
            if (Measure(_httpPort).GetAwaiter().GetResult() > 0) { _healthFails = 0; return; }
            if (++_healthFails < 2) return;
            _healthFails = 0;
            var cur = ActiveGuid;
            var res = Probe(State.Servers.ToList());
            if (res.TryGetValue(cur, out var c) && c > 0) return;            // текущий сервер жив
            if (!res.Any(kv => kv.Key != cur && kv.Value > 0)) return;       // связи нет нигде — пропал интернет, ждём
            Log.Write("auto: текущий сервер перестал пропускать трафик — переключаюсь на другой");
            Reconnect();
        }
        catch (Exception ex) { Log.Write("auto health: " + ex.Message); }
        finally { Interlocked.Exchange(ref _healthBusy, 0); }
    }

    /// <summary>Ядро упало в режиме «Авто» — один раз за 3 минуты сами подключаемся заново (с новым выбором).</summary>
    private bool TryFailover()
    {
        if (!State.AutoSelect || State.Servers.Count < 2) return false;
        if (DateTime.UtcNow - _lastFailover < TimeSpan.FromMinutes(3)) return false;
        _lastFailover = DateTime.UtcNow;
        Log.Write("auto: движок остановился — подключаюсь заново с новым выбором сервера");
        Task.Run(Connect);
        return true;
    }
}
