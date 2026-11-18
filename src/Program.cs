using System.Security.Cryptography;

namespace SiberiaConnect;

internal static class Program
{
    // имена мьютекса и события — AppInstance.MutexName / ShowEventName: без экземпляра прежние (…v1), с ним — с суффиксом id
    private static string MutexName => AppInstance.MutexName;
    private static string ShowEventName => AppInstance.ShowEventName;

    [STAThread]
    private static void Main(string[] args)
    {
        if (AppInstance.Error != null)
        {
            MessageBox.Show(AppInstance.Error, "Siberia Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (args.Contains("--cleanup"))
        {
            Cleanup.Run(args.Contains("--update"));
            return;
        }
        if (args.Length >= 2 && args[0] == "--guard")
        {
            Guard.Run(args[1]);   // 1.1.0: сторож — без окна и без проверки «уже запущено»
            return;
        }
        if (args.Length >= 3 && args[0] == "--selftest-api")
        {
            ApiSelfTest.Run(args[1], args[2]);   // CI: nodes/rank и его сбои на локальном сервере (только в режиме экземпляра)
            return;
        }
        if (args.Length >= 2 && args[0] == "--selftest-telemetry")
        {
            TelemetrySelfTest.Run(args[1]);   // CI: фильтр ложных срабатываний, дедупликация и очистка лога (чистая логика, без сети)
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

        Log.Write($"Start v{AppInfo.Version} args=[{string.Join(" ", args)}] os={Environment.OSVersion.VersionString}"
                  + (AppInstance.Active ? $" instance={AppInstance.Id} data={Paths.Data}" : ""));
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
    /// <summary>Один и тот же файл (без учёта регистра и «..»). Пустой или некорректный путь — не совпадает.</summary>
    internal static bool SamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static void Run(bool update = false)
    {
        if (AppInstance.Forbid("cleanup")) return;
        try
        {
            var me = Environment.ProcessId;
            // Убиваем только процессы из ЭТОЙ установки: другая версия рядом (например, стабильная) остаётся нетронутой.
            // Путь не прочитался (процесс с повышенными правами и т. п.) — считаем чужим и не трогаем.
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("SiberiaConnect"))
            {
                try
                {
                    if (p.Id == me) continue;
                    string path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    if (!SamePath(path, Environment.ProcessPath))
                    {
                        Log.Write($"cleanup: пропускаю чужой процесс {p.Id} ({path ?? "путь недоступен"})");
                        continue;
                    }
                    p.Kill(true);
                    p.WaitForExit(3000);
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            VpnCore.KillOrphans();
            var st = AppState.Load();
            SysProxy.Restore(st);
            Session.End();
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
        File.WriteAllText(Path.Combine(dir, "xray-probe.json"),
            XrayConfig.BuildProbe(new[] { reality, xhttp }, new[] { 21100, 21101 }, out var probeOk));   // 1.2.0: «Авто»
        if (!probeOk.All(x => x)) throw new Exception("probe: сервер из подписки не распознан");

        // 1.1.0: резервная копия — шифруется, расшифровывается верным паролем и не открывается неверным
        var st = new AppState { SubUrl = "https://sibirapp.duckdns.org/sub/selftest", Selected = "g1", TunMode = true, Backdrop = WindowFx.Acrylic };
        st.Servers.Add(new ServerItem { Guid = "g1", Name = "Тест", Uri = reality });
        var box = Backup.Seal(Backup.ToJson(st), "снежинка-123");
        var back = Backup.FromJson(Backup.Open(box, "снежинка-123"));
        if (back.SubUrl != st.SubUrl || back.Selected != "g1" || back.Servers.Count != 1 || back.Servers[0].Uri != reality
            || !back.TunMode || back.Backdrop != WindowFx.Acrylic)
            throw new Exception("backup: данные не совпали после расшифровки");
        var rejected = false;
        try { Backup.Open(box, "не-тот-пароль"); } catch (CryptographicException) { rejected = true; }
        if (!rejected) throw new Exception("backup: неверный пароль не отклонён");
        box[box.Length - 1] ^= 1;
        rejected = false;
        try { Backup.Open(box, "снежинка-123"); } catch (CryptographicException) { rejected = true; }
        if (!rejected) throw new Exception("backup: испорченный файл не отклонён");
        File.WriteAllText(Path.Combine(dir, "backup-ok.txt"), "ok");
        InstanceChecks(dir);
    }

    /// <summary>Без SC_INSTANCE имена и профиль прежние (совместимость обновления); с ним — с суффиксом, действия запрещены.</summary>
    private static void InstanceChecks(string dir)
    {
        const string baseMutex = "SiberiaConnect.SingleInstance.v1", baseShow = "SiberiaConnect.Show.v1";
        if (AppInstance.Error != null) throw new Exception("instance: " + AppInstance.Error);
        if (!AppInstance.Active)
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiberiaConnect");
            if (AppInstance.MutexName != baseMutex || AppInstance.ShowEventName != baseShow)
                throw new Exception("instance: без SC_INSTANCE имена мьютекса/события изменились");
            if (!string.Equals(Paths.Data, legacy, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Paths.StateDat, Path.Combine(legacy, "state.dat"), StringComparison.OrdinalIgnoreCase))
                throw new Exception("instance: без SC_INSTANCE изменился путь профиля");
            if (AppInstance.Forbid("selftest")) throw new Exception("instance: без SC_INSTANCE действия не должны запрещаться");
            if (AppInstance.DenyRemote("selftest", "sibirapp.duckdns.org")) throw new Exception("instance: без SC_INSTANCE запросы к серверу не должны запрещаться");
            File.WriteAllText(Path.Combine(dir, "instance-default-ok.txt"), "ok");
            return;
        }
        var id = AppInstance.Id;
        if (AppInstance.MutexName != baseMutex + "." + id || AppInstance.ShowEventName != baseShow + "." + id)
            throw new Exception("instance: имена мьютекса/события без суффикса");
        if (!Paths.Data.EndsWith("SiberiaConnect." + id, StringComparison.OrdinalIgnoreCase))
            throw new Exception("instance: профиль без суффикса: " + Paths.Data);
        if (!Paths.StateDat.StartsWith(Paths.Data, StringComparison.OrdinalIgnoreCase)
            || !Paths.TunOwner.StartsWith(Paths.Data, StringComparison.OrdinalIgnoreCase)
            || !Paths.WebData.StartsWith(Paths.Data, StringComparison.OrdinalIgnoreCase))
            throw new Exception("instance: файлы состояния вне профиля экземпляра");
        if (!AppInstance.Forbid("selftest")) throw new Exception("instance: Forbid не сработал");
        if (!AppInstance.DenyRemote("selftest", "sibirapp.duckdns.org") || !AppInstance.DenyRemote("selftest", "sibir-cloud-api.ru"))
            throw new Exception("instance: запросы к боевому серверу не запрещены");
        if (AppInstance.DenyRemote("selftest", "127.0.0.1") || AppInstance.DenyRemote("selftest", "localhost") || AppInstance.DenyRemote("selftest", "[::1]"))
            throw new Exception("instance: loopback должен быть разрешён");
        File.WriteAllText(Path.Combine(dir, "instance-ok.txt"), id + "|" + Paths.Data);
    }
}
