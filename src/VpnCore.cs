using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>
/// VPN-движок: подписка Siberia Connect → серверы → Xray → системный прокси.
/// Замок: принимается только подписка с наших адресов, других серверов в приложении нет.
/// </summary>
public sealed partial class VpnCore
{
    public static readonly string[] AllowedHosts = { "snezhnet.duckdns.org", "sibirapp.duckdns.org" };
    private const int SocksPortBase = 20808;

    public AppState State { get; }
    public string DeviceId { get; }
    public bool Running { get; private set; }
    public bool Busy { get; private set; }
    public long Since { get; private set; }

    /// <summary>События для интерфейса: {type:'state', state:'connecting|on|off|error', error}</summary>
    public event Action<JsonObject> Changed;

    private readonly object _gate = new();
    private Process _proc;
    private volatile bool _stopping;
    private int _httpPort;
    private int _socksPort;
    private Process _tun;
    public bool TunActive { get; private set; }

    private static readonly HttpClient Direct = new(new HttpClientHandler
    {
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = Pinning.Check
    }) { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly HttpClient ViaSystem = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = Pinning.Check
    }) { Timeout = TimeSpan.FromSeconds(20) };

    public VpnCore()
    {
        State = AppState.Load();
        DeviceId = Device.Id();
        KillOrphans();
        if (State.ProxySaved) SysProxy.Restore(State); // после сбоя/выключения ПК — вернуть интернет
        Session.End();                                 // 1.1.0: метка от прошлого запуска больше не нужна
        Recovery.Sweep();                              // 1.1.0: забытый адаптер «Весь компьютер»
    }

    public bool HasSub => State.Servers.Count > 0;

    public static bool IsAllowedSubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttps
               && string.IsNullOrEmpty(u.UserInfo)
               && AllowedHosts.Contains(u.IdnHost.ToLowerInvariant());
    }

    // ───────────────────────── Подписка ─────────────────────────

    public JsonObject UpdateSub(string url)
    {
        var o = new JsonObject { ["type"] = "sub" };
        if (!IsAllowedSubUrl(url))
        {
            o["ok"] = false; o["error"] = "Это не ключ Siberia Connect";
            return o;
        }
        url = url.Trim();
        try
        {
            string body;
            try { body = Fetch(Direct, url); }
            catch (Exception e1)
            {
                Log.Write("sub direct: " + e1.Message);
                body = Fetch(ViaSystem, url);
            }
            var links = ParseSub(body);
            if (links.Count == 0)
            {
                o["ok"] = false; o["error"] = "Сервер не выдал список. Проверь интернет или лимит устройств";
                return o;
            }
            var list = links
                .Select(l => new ServerItem { Guid = Hash(l), Name = NameOf(l), Uri = l })
                .GroupBy(s => s.Guid).Select(g => g.First()).ToList();
            bool restart;
            lock (_gate)
            {
                var prev = State.Servers.FirstOrDefault(s => s.Guid == State.Selected);
                State.SubUrl = url;
                State.Servers = list;
                if (!list.Any(s => s.Guid == State.Selected))
                    State.Selected = (list.FirstOrDefault(s => prev != null && s.Name == prev.Name) ?? list[0]).Guid;
                State.LastUpdate = DateTime.UtcNow;
                State.Save();
                restart = Running && (State.AutoSelect
                    ? !list.Any(s => s.Guid == ActiveGuid)                 // 1.2.0: в «Авто» — только если текущего сервера больше нет
                    : prev != null && prev.Guid != State.Selected);
            }
            Log.Write($"sub ok: {list.Count} servers");
            if (restart) Task.Run(Reconnect);
            o["ok"] = true; o["count"] = list.Count; o["updated"] = 1; o["error"] = "";
        }
        catch (Exception ex)
        {
            Log.Write("sub error: " + ex.Message);
            o["ok"] = false; o["error"] = "Не удалось обновить ключ — нет связи с сервером";
        }
        return o;
    }

    /// <summary>Тихое обновление списка серверов раз в несколько часов.</summary>
    public void RefreshIfDue()
    {
        if (!HasSub || string.IsNullOrEmpty(State.SubUrl) || Busy) return;
        if (DateTime.UtcNow - State.LastUpdate < TimeSpan.FromHours(3)) return;
        UpdateSub(State.SubUrl);
    }

    private string Fetch(HttpClient http, string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", $"v2rayN/7.0 SiberiaConnect/{AppInfo.Version} (Windows)");
        req.Headers.TryAddWithoutValidation("x-hwid", DeviceId);
        req.Headers.TryAddWithoutValidation("x-device-os", "Windows");
        req.Headers.TryAddWithoutValidation("x-ver-os", Environment.OSVersion.Version.ToString());
        req.Headers.TryAddWithoutValidation("x-device-model", "PC");
        using var resp = http.Send(req);
        resp.EnsureSuccessStatusCode();
        using var s = resp.Content.ReadAsStream();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    internal static List<string> ParseSub(string body)
    {
        var text = (body ?? "").Trim();
        if (!text.Contains("://"))
        {
            var dec = TryB64(text);
            if (dec != null) text = dec;
        }
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string TryB64(string s)
    {
        try
        {
            var t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
            t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(t));
        }
        catch { return null; }
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)), 0, 8).ToLowerInvariant();

    private static string NameOf(string link)
    {
        var i = link.IndexOf('#');
        if (i < 0 || i == link.Length - 1) return "Siberia Connect";
        try { return Uri.UnescapeDataString(link[(i + 1)..]).Trim(); }
        catch { return link[(i + 1)..]; }
    }

    // ───────────────────────── Подключение ─────────────────────────

    public void Connect()
    {
        lock (_gate)
        {
            if (Running) { Fire("on"); return; }
            var srv = State.Servers.FirstOrDefault(s => s.Guid == State.Selected) ?? State.Servers.FirstOrDefault();
            if (srv == null) { FireError("Сначала обнови ключ"); return; }
            Busy = true;
            Fire("connecting");
            try
            {
                var auto = State.AutoSelect && State.Servers.Count > 1;
                if (auto) srv = PickAuto();   // 1.2.0: «Северное Сияние» — реальная проверка всех серверов
                var err = StartCore(srv, true);
                if (err != null)
                {
                    Log.Write("start with geo failed: " + err + " — retry without geo");
                    err = StartCore(srv, false);
                }
                if (err != null) { FireError(err); return; }
                if (State.TunMode && Elevation.IsAdmin)
                {
                    var terr = StartTun(TunConfigBuilder.ServerIps(srv.Uri));
                    if (terr != null) { StopProc(); FireError(terr); return; }
                }
                else SysProxy.Enable($"127.0.0.1:{_httpPort}", State);
                Running = true;
                ActiveGuid = srv.Guid;
                Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Session.Begin(TunActive);   // 1.1.0: при аварийном выходе сторож вернёт интернет
                Guard.Ensure();
                StartHealth();              // 1.2.0
                Fire("on");
                Log.Write($"connected: {srv.Name}" + (auto ? " (авто)" : ""));
            }
            catch (Exception ex)
            {
                Log.Write("connect: " + ex);
                StopProc();
                FireError("Не удалось подключиться: " + ex.Message);
            }
            finally { Busy = false; }
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            StopHealth();
            StopTun();
            StopProc();
            SysProxy.Restore(State);
            Session.End();
            Running = false;
            Since = 0;
            Fire("off");
        }
    }

    public void Reconnect()
    {
        Disconnect();
        Connect();
    }

    public void Select(string guid)
    {
        bool reconnect;
        lock (_gate)
        {
            if (guid == AutoGuid)
            {
                if (State.AutoSelect) return;
                State.AutoSelect = true;
                reconnect = Running;
            }
            else
            {
                if (!State.Servers.Any(s => s.Guid == guid)) return;
                State.AutoSelect = false;
                State.Selected = guid;
                reconnect = Running && ActiveGuid != guid;   // уже на нём — переподключаться незачем
                if (Running && !reconnect) StopHealth();
            }
            State.Save();
        }
        if (reconnect) Reconnect();
    }

    /// <summary>Выход из приложения / выключение ПК.</summary>
    public void Shutdown()
    {
        try
        {
            _stopping = true;
            StopHealth();
            try { if (_tun != null && !_tun.HasExited) _tun.Kill(true); } catch { }
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(true); } catch { }
            SysProxy.Restore(State);
            Session.End();
            Running = false;
        }
        catch { }
    }

    /// <summary>1.1.0: восстановление из зашифрованной копии. Потом список серверов обновляется с сервера.</summary>
    public JsonObject RestoreBackup(Backup.Data d)
    {
        var was = Running;
        if (was) Disconnect();
        lock (_gate)
        {
            State.SubUrl = d.SubUrl;
            State.Servers = d.Servers;
            State.Selected = d.Servers.Any(s => s.Guid == d.Selected) ? d.Selected : d.Servers.FirstOrDefault()?.Guid ?? "";
            State.AutoStart = d.AutoStart;
            State.TunMode = d.TunMode;
            State.Backdrop = WindowFx.Normalize(d.Backdrop);
            State.AutoSelect = d.AutoSelect;
            State.LastUpdate = DateTime.MinValue;
            State.Save();
        }
        Log.Write($"backup: восстановлено ({d.Servers.Count} серверов в копии)");
        var r = UpdateSub(State.SubUrl);   // нет связи — останутся серверы из копии
        if (was && HasSub) Connect();
        return r;
    }

    private string StartCore(ServerItem srv, bool useGeo)
    {
        if (!File.Exists(Paths.Xray)) return "Не найден движок VPN. Переустанови Siberia Connect";
        StopProc();
        int socks = FreePort(SocksPortBase);
        _socksPort = socks;
        _httpPort = FreePort(socks + 1);
        string cfg;
        try { cfg = XrayConfig.Build(srv.Uri, socks, _httpPort, useGeo, State.TunMode && Elevation.IsAdmin); }
        catch (Exception ex) { return "Неверные данные сервера: " + ex.Message; }
        File.WriteAllText(Paths.CoreConfig, cfg, new UTF8Encoding(false));

        var psi = new ProcessStartInfo(Paths.Xray, $"run -c \"{Paths.CoreConfig}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Paths.CoreDir
        };
        psi.Environment["XRAY_LOCATION_ASSET"] = Paths.CoreDir;
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new StringBuilder();
        DataReceivedEventHandler h = (_, e) =>
        {
            if (e.Data == null) return;
            Log.Core(e.Data);
            lock (tail)
            {
                tail.AppendLine(e.Data);
                if (tail.Length > 4000) tail.Remove(0, tail.Length - 4000);
            }
        };
        p.OutputDataReceived += h;
        p.ErrorDataReceived += h;
        _stopping = false;
        p.Start();
        CoreJob.Add(p);   // 1.1.0: ядро не переживёт приложение
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 8000)
        {
            if (p.HasExited)
            {
                DeleteConfig();
                string last;
                lock (tail) last = tail.ToString().Trim().Split('\n').LastOrDefault() ?? "";
                return "Движок VPN остановился: " + last.Trim();
            }
            if (PortOpen(_httpPort))
            {
                DeleteConfig();   // sec_v4: ядро уже прочитало конфиг — не держим ключ на диске
                _proc = p;
                p.Exited += OnCoreExited;
                if (p.HasExited) return "Движок VPN остановился сразу после запуска";
                return null;
            }
            Thread.Sleep(150);
        }
        try { p.Kill(true); } catch { }
        DeleteConfig();
        return "Движок VPN долго запускается — попробуй ещё раз";
    }

    private static void DeleteConfig()
    {
        try { if (File.Exists(Paths.CoreConfig)) File.Delete(Paths.CoreConfig); } catch { }
    }

    private void OnCoreExited(object sender, EventArgs e)
    {
        if (_stopping) return;
        Log.Write("core exited unexpectedly");
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _proc) && !ReferenceEquals(sender, _tun)) return;
            StopHealth();
            StopTun();
            StopProc();
            SysProxy.Restore(State);
            Session.End();
            Running = false;
            Since = 0;
        }
        if (TryFailover()) return;   // 1.2.0: в «Авто» сами подключаемся к другому серверу
        FireError("VPN отключился. Нажми на снежинку, чтобы подключиться снова");
    }

    private void StopProc()
    {
        var p = _proc;
        _proc = null;
        if (p == null) return;
        _stopping = true;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(true);
                p.WaitForExit(3000);
            }
        }
        catch { }
        finally { try { p.Dispose(); } catch { } }
    }

    public static void KillOrphans()
    {
        foreach (var path in new[] { Paths.Xray, Paths.SingBox })
            foreach (var p in OurProcesses(path))
                try { using (p) p.Kill(true); } catch { }
    }

    /// <summary>1.1.0: запущен ли наш xray/sing-box (из папки приложения, а не чужой).</summary>
    public static bool OurCoreRunning(string path)
    {
        var any = false;
        foreach (var p in OurProcesses(path)) { any = true; p.Dispose(); }
        return any;
    }

    private static List<Process> OurProcesses(string path)
    {
        var list = new List<Process>();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            try
            {
                var f = p.MainModule?.FileName;
                if (f != null && string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(p);
                    continue;
                }
            }
            catch { }
            p.Dispose();
        }
        return list;
    }

    // ───────────────────────── «Весь компьютер» (TUN) ─────────────────────────
    // sing-box поднимает виртуальный адаптер (wintun) и отдаёт весь трафик в локальный SOCKS Xray.
    // Трафик самого Xray и локальная сеть (принтеры, роутер) идут мимо адаптера.

    private string StartTun(string[] serverIps)
    {
        if (!File.Exists(Paths.SingBox)) return "Не найден компонент «Весь компьютер». Переустанови Siberia Connect";
        StopTun();
        Recovery.Sweep();   // 1.1.0: адаптер, оставшийся после сбоя, мешал бы новому
        Log.Write("tun: мимо адаптера " + (serverIps.Length > 0 ? string.Join(", ", serverIps) : "— (адрес сервера не определён)"));
        try { File.WriteAllText(Paths.TunConfig, TunConfigBuilder.Build(_socksPort, serverIps), new UTF8Encoding(false)); }
        catch (Exception ex) { return "Не удалось подготовить режим «Весь компьютер»: " + ex.Message; }
        var psi = new ProcessStartInfo(Paths.SingBox, $"run -c \"{Paths.TunConfig}\" -D \"{Paths.Data}\" --disable-color")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Paths.CoreDir
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new StringBuilder();
        DataReceivedEventHandler h = (_, e) =>
        {
            if (e.Data == null) return;
            Log.Core("[tun] " + e.Data);
            lock (tail) { tail.AppendLine(e.Data); if (tail.Length > 3000) tail.Remove(0, tail.Length - 3000); }
        };
        p.OutputDataReceived += h;
        p.ErrorDataReceived += h;
        _stopping = false;
        p.Start();
        CoreJob.Add(p);   // 1.1.0: упало приложение → sing-box завершится, Windows уберёт адаптер и маршруты
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 10000)
        {
            if (p.HasExited)
            {
                string last;
                lock (tail) last = tail.ToString().Trim().Split('\n').LastOrDefault() ?? "";
                try { File.Delete(Paths.TunConfig); } catch { }
                return "Режим «Весь компьютер» не запустился: " + last.Trim();
            }
            if (TunUp())
            {
                Thread.Sleep(300);
                if (p.HasExited) continue;
                _tun = p;
                p.Exited += OnCoreExited;
                TunActive = true;
                try { File.Delete(Paths.TunConfig); } catch { }
                return null;
            }
            Thread.Sleep(200);
        }
        try { p.Kill(true); } catch { }
        try { File.Delete(Paths.TunConfig); } catch { }
        return "Режим «Весь компьютер» долго запускается — попробуй ещё раз";
    }

    private static bool TunUp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.Name.StartsWith(TunConfigBuilder.InterfaceName, StringComparison.OrdinalIgnoreCase) &&
                n.OperationalStatus == OperationalStatus.Up);
        }
        catch { return false; }
    }

    private void StopTun()
    {
        var p = _tun;
        _tun = null;
        TunActive = false;
        if (p == null) return;
        _stopping = true;
        try { if (!p.HasExited) { p.Kill(true); p.WaitForExit(4000); } } catch { }
        finally { try { p.Dispose(); } catch { } }
    }

    private static int FreePort(int start)
    {
        for (int port = start; port < start + 200; port++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return port;
            }
            catch { }
        }
        return start;
    }

    private static bool PortOpen(int port)
    {
        try
        {
            using var c = new TcpClient();
            var t = c.ConnectAsync(IPAddress.Loopback, port);
            return t.Wait(300) && c.Connected;
        }
        catch { return false; }
    }

    // ───────────────────────── Статистика ─────────────────────────

    public static (long rx, long tx) Traffic()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (ni.Name.Equals(TunConfigBuilder.InterfaceName, StringComparison.OrdinalIgnoreCase)) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch { }
        return (rx, tx);
    }

    private void Fire(string state) => Changed?.Invoke(new JsonObject { ["type"] = "state", ["state"] = state });

    private void FireError(string err) =>
        Changed?.Invoke(new JsonObject { ["type"] = "state", ["state"] = "error", ["error"] = err });
}
