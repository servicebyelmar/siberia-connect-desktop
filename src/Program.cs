namespace SiberiaConnect;

internal static class Program
{
    private const string MutexName = "SiberiaConnect.SingleInstance.v1";
    private const string ShowEventName = "SiberiaConnect.Show.v1";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--cleanup"))
        {
            Cleanup.Run(args.Contains("--update"));
            return;
        }
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            SelfTest.Run(args[1]);
            return;
        }

        var link = args.FirstOrDefault(a => a.StartsWith("siberiaconnect:", StringComparison.OrdinalIgnoreCase));
        if (link != null) DeepLink.Save(link);

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first && args.Contains("--takeover"))
        {
            // перезапуск с правами администратора: ждём, пока старая копия закроется
            try { first = mutex.WaitOne(15000); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            // Приложение уже запущено — просто показываем его окно.
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch { }
            return;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Log.Write($"Start v{AppInfo.Version} args=[{string.Join(" ", args)}] os={Environment.OSVersion.VersionString}");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("UI error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write("FATAL: " + e.ExceptionObject);
            try { MainForm.Instance?.Core.Shutdown(); } catch { }
        };

        ApplicationConfiguration.Initialize();
        var form = new MainForm(args.Contains("--autostart"), args.Contains("--connect"));

        var watcher = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                try { form.BeginInvoke(new Action(form.ShowFromTray)); } catch { }
            }
        }) { IsBackground = true, Name = "show-watcher" };
        watcher.Start();

        Application.Run(form);
        GC.KeepAlive(mutex);
    }
}

/// <summary>link_v1: вход по ссылке из бота. Код кладём в файл — его подхватит уже запущенное приложение
/// (в том числе запущенное с правами администратора, куда обычный сигнал не дойдёт).</summary>
internal static class DeepLink
{
    public static void Save(string link)
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(link, @"[?&]c=([A-Za-z0-9_-]{8,64})");
            if (!m.Success) return;
            Directory.CreateDirectory(Paths.Data);
            var tmp = Paths.PendingLogin + ".tmp";
            File.WriteAllText(tmp, m.Groups[1].Value);
            File.Move(tmp, Paths.PendingLogin, true);
        }
        catch (Exception ex) { Log.Write("deeplink: " + ex.Message); }
    }

    public static string Take()
    {
        try
        {
            if (!File.Exists(Paths.PendingLogin)) return null;
            var c = File.ReadAllText(Paths.PendingLogin).Trim();
            File.Delete(Paths.PendingLogin);
            return System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Za-z0-9_-]{8,64}$") ? c : null;
        }
        catch { return null; }
    }
}

/// <summary>Удаление/обновление: остановить VPN, вернуть настройки прокси, убрать автозапуск.</summary>
internal static class Cleanup
{
    public static void Run(bool update = false)
    {
        try
        {
            var me = Environment.ProcessId;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("SiberiaConnect"))
            {
                try { if (p.Id != me) { p.Kill(true); p.WaitForExit(3000); } } catch { }
            }
            VpnCore.KillOrphans();
            var st = AppState.Load();
            SysProxy.Restore(st);
            if (!update) AutoStart.Set(false);   // при обновлении автозапуск оставляем
            Log.Write("Cleanup done");
        }
        catch (Exception ex) { Log.Write("Cleanup error: " + ex.Message); }
    }
}

/// <summary>Проверка в сборке GitHub: приложение пишет конфиги Xray и sing-box, а CI проверяет их самими движками.</summary>
internal static class SelfTest
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        try { Write(dir); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
    }

    private static void Write(string dir)
    {
        const string reality = "vless://11111111-2222-3333-4444-555555555555@example.com:443?type=tcp&security=reality&pbk=Z84J2IelR9ch3k8VtlVhhs5ycBUlXA7wHBWcBrjqnAw&fp=safari&sni=www.example.com&sid=6ba85179e30d4fc2&flow=xtls-rprx-vision#Test%20Reality";
        const string xhttp = "vless://11111111-2222-3333-4444-555555555555@example.com:8443?type=xhttp&security=reality&pbk=Z84J2IelR9ch3k8VtlVhhs5ycBUlXA7wHBWcBrjqnAw&fp=safari&sni=www.example.com&sid=6ba85179e30d4fc2&path=%2Fsc&mode=auto#Test%20XHTTP";
        File.WriteAllText(Path.Combine(dir, "xray-reality.json"), XrayConfig.Build(reality, 20808, 20809, true));
        File.WriteAllText(Path.Combine(dir, "xray-xhttp.json"), XrayConfig.Build(xhttp, 20808, 20809, true));
        File.WriteAllText(Path.Combine(dir, "xray-tun.json"), XrayConfig.Build(reality, 20808, 20809, true, true));
        File.WriteAllText(Path.Combine(dir, "tun.json"), TunConfigBuilder.Build(20808, new[] { "203.0.113.7/32" }));
    }
}
