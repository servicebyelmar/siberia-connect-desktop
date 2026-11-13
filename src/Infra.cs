using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SiberiaConnect;

internal static class AppInfo
{
    public static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var v = typeof(AppInfo).Assembly.GetName().Version;
        return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static Stream Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

    public static string ResourceText(string name)
    {
        using var s = Resource(name);
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public static Bitmap LoadBitmap()
    {
        try
        {
            using var s = Resource("app.ico");
            using var ic = new Icon(s, 256, 256);
            return ic.ToBitmap();
        }
        catch { return null; }
    }

    public static Icon LoadIcon(bool on)
    {
        try
        {
            using var s = Resource(on ? "app.ico" : "app_off.ico");
            return new Icon(s);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}

internal static class Paths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiberiaConnect");
    public static readonly string Logs = Path.Combine(Data, "logs");
    public static readonly string StateFile = Path.Combine(Data, "state.json");      // старый формат (до 1.0.1)
    public static readonly string StateDat = Path.Combine(Data, "state.dat");        // зашифрован DPAPI
    public static readonly string StateBak = Path.Combine(Data, "state.bak");        // 1.1.0: запасная копия state.dat (тоже DPAPI)
    public static readonly string Session = Path.Combine(Data, "session.lock");      // 1.1.0: «VPN включён» — для сторожа
    public static readonly string CoreConfig = Path.Combine(Data, "core-config.json");
    public static readonly string WebData = Path.Combine(Data, "WebView2");
    public static readonly string AppDir = AppContext.BaseDirectory;
    public static readonly string CoreDir = Path.Combine(AppDir, "core");
    public static readonly string Xray = Path.Combine(CoreDir, "xray.exe");
    public static readonly string SingBox = Path.Combine(CoreDir, "sing-box.exe");
    public static readonly string TunConfig = Path.Combine(Data, "tun-config.json");
    public static readonly string PendingLogin = Path.Combine(Data, "login.pending");   // link_v1: код входа из ссылки бота

    static Paths()
    {
        try
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Logs);
        }
        catch { }
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string msg) => Append("app.log", msg);
    public static void Core(string msg) => Append("core.log", msg);

    private static void Append(string file, string msg)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(Paths.Logs, file);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = Path.Combine(Paths.Logs, Path.GetFileNameWithoutExtension(file) + ".old.log");
                    File.Copy(path, old, true);
                    File.Delete(path);
                }
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }
}

public sealed class ServerItem
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Uri { get; set; } = "";
}

public sealed class AppState
{
    public string SubUrl { get; set; } = "";
    public List<ServerItem> Servers { get; set; } = new();
    public string Selected { get; set; } = "";
    public DateTime LastUpdate { get; set; }
    public bool AutoStart { get; set; }
    public bool TunMode { get; set; }   // 1.0.2: «Весь компьютер»
    public string Backdrop { get; set; } = WindowFx.Mica;   // 1.1.0: mica | acrylic | off
    public bool AutoSelect { get; set; } = true;            // 1.2.0: «Северное Сияние (Авто)» — по умолчанию
    public string InstallSecret { get; set; } = "";         // 1.3.0: секрет установки (вход без Telegram)
    public string AuthToken { get; set; } = "";             // 1.3.0: общий вход приложения и кабинета
    public bool AutoRegDone { get; set; }
    public bool TokenFromInstall { get; set; }              // 1.3.0: вход получен по секрету установки (а не из кабинета)                   // 1.3.0: автовход уже пробовали — повторно не создаём аккаунт
    public string LoginCode { get; set; } = "";             // 1.3.0: код доступа SC-… (показывается в меню)

    // Настройки прокси Windows до включения VPN (чтобы вернуть как было)
    public bool ProxySaved { get; set; }
    public int PrevProxyEnable { get; set; }
    public string PrevProxyServer { get; set; } = "";
    public string PrevProxyOverride { get; set; } = "";
    public string OurProxy { get; set; } = "";

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static readonly object Gate = new();

    // sec_v4: состояние (ссылка подписки, серверы) шифруется DPAPI — прочитать может только этот пользователь Windows
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SiberiaConnect.state.v1");

    public static AppState Load()
    {
        // 1.1.0: основной файл, а если он повреждён или пропал — запасная копия
        foreach (var (path, isBak) in new[] { (Paths.StateDat, false), (Paths.StateBak, true) })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                var st = JsonSerializer.Deserialize<AppState>(Encoding.UTF8.GetString(raw), Opts);
                if (st == null) continue;
                st.Servers ??= new List<ServerItem>();
                st.Backdrop = WindowFx.Normalize(st.Backdrop);
                if (isBak)
                {
                    Log.Write("state: state.dat повреждён или пропал — настройки восстановлены из state.bak");
                    st.Save();
                }
                return st;
            }
            catch (Exception ex) { Log.Write($"state load ({Path.GetFileName(path)}): {ex.Message}"); }
        }
        try
        {
            if (File.Exists(Paths.StateFile))
            {
                var st = JsonSerializer.Deserialize<AppState>(File.ReadAllText(Paths.StateFile), Opts);
                if (st != null)
                {
                    st.Servers ??= new List<ServerItem>();
                    st.Save();
                    try { File.Delete(Paths.StateFile); } catch { }
                    return st;
                }
            }
        }
        catch (Exception ex) { Log.Write("state load: " + ex.Message); }
        return new AppState();
    }

    public void Save()
    {
        try
        {
            lock (Gate)
            {
                var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Opts)), Entropy,
                    DataProtectionScope.CurrentUser);
                var tmp = Paths.StateDat + ".tmp";
                File.WriteAllBytes(tmp, data);
                File.Move(tmp, Paths.StateDat, true);
                // 1.1.0: запасная копия только что записанных (заведомо целых) данных
                var bakTmp = Paths.StateBak + ".tmp";
                File.WriteAllBytes(bakTmp, data);
                File.Move(bakTmp, Paths.StateBak, true);
            }
        }
        catch (Exception ex) { Log.Write("state save: " + ex.Message); }
    }
}

internal static class Device
{
    public static string Id()
    {
        string raw = "";
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            raw = k?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }
        if (string.IsNullOrEmpty(raw)) raw = Environment.MachineName + "|" + Environment.UserName;
        var h = SHA256.HashData(Encoding.UTF8.GetBytes("sc:" + raw + ":com.siberiaconnect.win"));
        return "win-" + Convert.ToHexString(h, 0, 12).ToLowerInvariant();
    }
}

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SiberiaConnect";

    /// <summary>Обычный режим — ключ Run. «Весь компьютер» — задача планировщика с наивысшими правами
    /// (иначе при входе в Windows каждый раз появлялось бы окно UAC).</summary>
    public static void Set(bool on, bool tun = false)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on && !(tun && Elevation.IsAdmin)) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --autostart");
            else k.DeleteValue(Name, false);
        }
        catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
        if (!Elevation.IsAdmin) return;
        try
        {
            var args = on && tun
                ? $"/Create /F /TN SiberiaConnect /TR \"\\\"{Environment.ProcessPath}\\\" --autostart\" /SC ONLOGON /RL HIGHEST"
                : "/Delete /F /TN SiberiaConnect";
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            p?.WaitForExit(8000);
        }
        catch (Exception ex) { Log.Write("schtasks: " + ex.Message); }
    }
}

/// <summary>Права администратора — нужны только режиму «Весь компьютер» (виртуальный адаптер).</summary>
internal static class Elevation
{
    public static readonly bool IsAdmin = Check();

    private static bool Check()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>Перезапуск с правами администратора (окно UAC). false — пользователь отказался.</summary>
    public static bool Relaunch(string extraArgs)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--takeover " + extraArgs)
                { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Exception ex) { Log.Write("elevate: " + ex.Message); return false; }
    }
}

/// <summary>Системный прокси Windows (WinINet): включаем на время VPN и возвращаем как было.</summary>
internal static class SysProxy
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string Bypass = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;*.local;<local>";

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

    private static void Refresh()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); // SETTINGS_CHANGED
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); // REFRESH
        }
        catch { }
    }

    public static void Enable(string address, AppState st)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key, true);
            if (!st.ProxySaved)
            {
                var cur = k.GetValue("ProxyServer") as string ?? "";
                st.PrevProxyEnable = Convert.ToInt32(k.GetValue("ProxyEnable") ?? 0);
                st.PrevProxyServer = cur == address ? "" : cur;
                st.PrevProxyOverride = k.GetValue("ProxyOverride") as string ?? "";
                st.ProxySaved = true;
            }
            st.OurProxy = address;
            st.Save();
            k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            k.SetValue("ProxyServer", address, RegistryValueKind.String);
            k.SetValue("ProxyOverride", Bypass, RegistryValueKind.String);
            Refresh();
        }
        catch (Exception ex) { Log.Write("proxy on: " + ex.Message); }
    }

    public static void Restore(AppState st)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key, true);
            var cur = k.GetValue("ProxyServer") as string ?? "";
            bool ours = !string.IsNullOrEmpty(st.OurProxy) && cur == st.OurProxy;
            if (st.ProxySaved)
            {
                if (st.PrevProxyEnable == 1 && !string.IsNullOrEmpty(st.PrevProxyServer))
                {
                    k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    k.SetValue("ProxyServer", st.PrevProxyServer, RegistryValueKind.String);
                    k.SetValue("ProxyOverride", st.PrevProxyOverride ?? "", RegistryValueKind.String);
                }
                else
                {
                    k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    if (ours)
                    {
                        if (string.IsNullOrEmpty(st.PrevProxyServer)) k.DeleteValue("ProxyServer", false);
                        else k.SetValue("ProxyServer", st.PrevProxyServer, RegistryValueKind.String);
                        k.SetValue("ProxyOverride", st.PrevProxyOverride ?? "", RegistryValueKind.String);
                    }
                }
            }
            else if (ours)
            {
                k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
            st.ProxySaved = false;
            st.Save();
            Refresh();
        }
        catch (Exception ex) { Log.Write("proxy off: " + ex.Message); }
    }
}

/// <summary>sec_v4: TLS к нашим серверам принимается, только если цепочка ведёт к корню Let's Encrypt (ISRG Root X1/X2).
/// Поддельный корневой сертификат, установленный в систему, не поможет подменить ответы.</summary>
internal static class Pinning
{
    private static readonly string[] Roots =
    {
        "96BCEC06264976F37460779ACF28C5A7CFE8A3C0AAE11A8FFCEE05C0BDDF08C6", // ISRG Root X1
        "69729B8E15A86EFC177A57AFB7171DFC64ADD28C2FCA8CF1507E34453CCB1470", // ISRG Root X2
        "0FC0901CCA2BAE9E9FDBB02D50D02F1094F7B36672086991B9E897626DC485F0", // ISRG Root YE (с 11.2026)
        "072639D0B140D5BFFAE16AD9C3F6CC6086040621F51EE61A6D46A8915C07CF76"  // ISRG Root YR
    };
    private static readonly DateTime Until = new(2030, 1, 1);

    public static bool Check(HttpRequestMessage req, X509Certificate2 cert, X509Chain chain, SslPolicyErrors errors)
    {
        if (errors != SslPolicyErrors.None) return false;
        if (DateTime.UtcNow > Until) return true;
        if (chain != null)
        {
            foreach (var el in chain.ChainElements)
            {
                var h = Convert.ToHexString(el.Certificate.GetCertHash(HashAlgorithmName.SHA256));
                if (Roots.Contains(h)) return true;
            }
        }
        Log.Write("TLS pin failed: " + req?.RequestUri?.Host);
        return false;
    }
}
