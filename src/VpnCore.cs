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
public sealed class VpnCore
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

    private static readonly HttpClient Direct = new(new HttpClientHandler
    {
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly HttpClient ViaSystem = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(20) };

    public VpnCore()
    {
        State = AppState.Load();
        DeviceId = Device.Id();
        KillOrphans();
        if (State.ProxySaved) SysProxy.Restore(State); // после сбоя/выключения ПК — вернуть интернет
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
                restart = Running && prev != null && prev.Guid != State.Selected;
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
                var err = StartCore(srv, true);
                if (err != null)
                {
                    Log.Write("start with geo failed: " + err + " — retry without geo");
                    err = StartCore(srv, false);
                }
                if (err != null) { FireError(err); return; }
                SysProxy.Enable($"127.0.0.1:{_httpPort}", State);
                Running = true;
                Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Fire("on");
                Log.Write($"connected: {srv.Name}");
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
            StopProc();
            SysProxy.Restore(State);
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
        bool was;
        lock (_gate)
        {
            if (!State.Servers.Any(s => s.Guid == guid)) return;
            State.Selected = guid;
            State.Save();
            was = Running;
        }
        if (was) Reconnect();
    }

    /// <summary>Выход из приложения / выключение ПК.</summary>
    public void Shutdown()
    {
        try
        {
            _stopping = true;
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(true); } catch { }
            SysProxy.Restore(State);
            Running = false;
        }
        catch { }
    }

    private string StartCore(ServerItem srv, bool useGeo)
    {
        if (!File.Exists(Paths.Xray)) return "Не найден движок VPN. Переустанови Siberia Connect";
        StopProc();
        int socks = FreePort(SocksPortBase);
        _httpPort = FreePort(socks + 1);
        string cfg;
        try { cfg = XrayConfig.Build(srv.Uri, socks, _httpPort, useGeo); }
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
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 8000)
        {
            if (p.HasExited)
            {
                string last;
                lock (tail) last = tail.ToString().Trim().Split('\n').LastOrDefault() ?? "";
                return "Движок VPN остановился: " + last.Trim();
            }
            if (PortOpen(_httpPort))
            {
                _proc = p;
                p.Exited += OnCoreExited;
                if (p.HasExited) return "Движок VPN остановился сразу после запуска";
                return null;
            }
            Thread.Sleep(150);
        }
        try { p.Kill(true); } catch { }
        return "Движок VPN долго запускается — попробуй ещё раз";
    }

    private void OnCoreExited(object sender, EventArgs e)
    {
        if (_stopping) return;
        Log.Write("core exited unexpectedly");
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _proc)) return;
            _proc = null;
            SysProxy.Restore(State);
            Running = false;
            Since = 0;
        }
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
        foreach (var p in Process.GetProcessesByName("xray"))
        {
            try
            {
                var f = p.MainModule?.FileName;
                if (f != null && string.Equals(Path.GetFullPath(f), Path.GetFullPath(Paths.Xray), StringComparison.OrdinalIgnoreCase))
                    p.Kill(true);
            }
            catch { }
        }
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
