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
            Cleanup.Run();
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool first);
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
        var form = new MainForm(args.Contains("--autostart"));

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

/// <summary>Удаление/обновление: остановить VPN, вернуть настройки прокси, убрать автозапуск.</summary>
internal static class Cleanup
{
    public static void Run()
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
            AutoStart.Set(false);
            Log.Write("Cleanup done");
        }
        catch (Exception ex) { Log.Write("Cleanup error: " + ex.Message); }
    }
}
