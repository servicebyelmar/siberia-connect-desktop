using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace SiberiaConnect;

// ───────────────────────── 1.1.0: защита от сбоев ─────────────────────────
// 1) Job object: xray и sing-box завершаются вместе с приложением, даже если его «убили» или оно упало.
//    Когда sing-box умирает, Windows сама убирает адаптер Wintun и его маршруты.
// 2) Сторож (--guard): маленький процесс, который ждёт завершения приложения. Если VPN был включён,
//    а приложение закрылось не штатно — возвращает прокси Windows и убирает оставшийся адаптер.
// 3) При каждом запуске и перед включением «Весь компьютер» — уборка забытого адаптера.

/// <summary>Ядра VPN живут не дольше приложения (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE).</summary>
internal static class CoreJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimit info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    // Дескриптор не закрываем: его закроет Windows при завершении приложения — и ядра завершатся вместе с ним.
    private static readonly IntPtr Job = Create();

    private static IntPtr Create()
    {
        try
        {
            var h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) { Log.Write("job: create " + Marshal.GetLastWin32Error()); return IntPtr.Zero; }
            var info = new ExtendedLimit();
            info.Basic.LimitFlags = KillOnJobClose;
            if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<ExtendedLimit>()))
            {
                Log.Write("job: limits " + Marshal.GetLastWin32Error());
                return IntPtr.Zero;
            }
            return h;
        }
        catch (Exception ex) { Log.Write("job: " + ex.Message); return IntPtr.Zero; }
    }

    public static void Add(Process p)
    {
        if (Job == IntPtr.Zero || p == null) return;
        try
        {
            if (!AssignProcessToJobObject(Job, p.Handle)) Log.Write("job: assign " + Marshal.GetLastWin32Error());
        }
        catch (Exception ex) { Log.Write("job: " + ex.Message); }
    }
}

/// <summary>Метка «VPN включён» с номером процесса-владельца. Нет метки — приложение вышло штатно.</summary>
internal static class Session
{
    public static void Begin(bool tun)
    {
        try
        {
            var tmp = Paths.Session + ".tmp";
            File.WriteAllText(tmp, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["tun"] = tun,
                ["at"] = DateTime.UtcNow.ToString("o")
            }.ToJsonString());
            File.Move(tmp, Paths.Session, true);
        }
        catch (Exception ex) { Log.Write("session: " + ex.Message); }
    }

    public static void End()
    {
        try { if (File.Exists(Paths.Session)) File.Delete(Paths.Session); } catch { }
    }

    public static int OwnerPid()
    {
        try
        {
            if (!File.Exists(Paths.Session)) return 0;
            return JsonNode.Parse(File.ReadAllText(Paths.Session))?["pid"]?.GetValue<int>() ?? 0;
        }
        catch { return 0; }
    }
}

/// <summary>Сторож: тот же SiberiaConnect.exe с ключом --guard PID. Окна нет, памяти почти не занимает.</summary>
internal static class Guard
{
    private static Process _proc;

    public static void Ensure()
    {
        if (AppInstance.Forbid("запуск сторожа")) return;
        try
        {
            if (_proc != null && !_proc.HasExited) return;
            _proc?.Dispose();
            _proc = Process.Start(new ProcessStartInfo(Environment.ProcessPath, $"--guard {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex) { Log.Write("guard start: " + ex.Message); }
    }

    public static void Run(string pidArg)
    {
        if (AppInstance.Forbid("сторож")) return;
        if (!int.TryParse(pidArg, out var pid) || pid <= 0) return;
        try
        {
            using var parent = Process.GetProcessById(pid);
            parent.WaitForExit();
        }
        catch (ArgumentException) { }      // уже завершилось
        catch (Exception ex) { Log.Write("guard: " + ex.Message); return; }

        if (Session.OwnerPid() != pid) return;          // метки нет — вышли штатно
        Thread.Sleep(1500);                              // даём Windows закрыть адаптер Wintun
        if (Session.OwnerPid() != pid) return;          // новая копия приложения уже всё сделала сама
        Log.Write($"guard: приложение (pid {pid}) закрылось аварийно при включённом VPN — возвращаю интернет");
        try { VpnCore.KillOrphans(); } catch { }
        try { SysProxy.Restore(AppState.Load()); } catch { }
        Recovery.Sweep();
        if (Session.OwnerPid() == pid) Session.End();
        Log.Write("guard: восстановление завершено");
    }
}

/// <summary>
/// 1.6.0: метка «этот адаптер «Весь компьютер» поднят вот этой установкой». Пишется перед запуском sing-box,
/// снимается при штатной остановке. Остаётся только после сбоя — тогда Recovery знает, что адаптер можно убрать.
/// Имя адаптера у всех версий одинаковое, поэтому по имени понять, чей он, нельзя.
/// </summary>
internal static class TunOwner
{
    internal sealed record Info(string Exe, string Adapter, int Pid);

    public static void Write()
    {
        try
        {
            File.WriteAllText(Paths.TunOwner, new JsonObject
            {
                ["exe"] = Environment.ProcessPath,
                ["adapter"] = TunConfigBuilder.InterfaceName,
                ["pid"] = Environment.ProcessId,
                ["build"] = AppInfo.Version,
                ["at"] = DateTime.UtcNow.ToString("o")
            }.ToJsonString());
        }
        catch (Exception ex) { Log.Write("tun owner: " + ex.Message); }
    }

    public static Info Read()
    {
        try
        {
            if (!File.Exists(Paths.TunOwner)) return null;
            var j = JsonNode.Parse(File.ReadAllText(Paths.TunOwner));
            var exe = j?["exe"]?.GetValue<string>();
            var adapter = j?["adapter"]?.GetValue<string>();
            if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(adapter)) return null;
            return new Info(exe, adapter, j["pid"]?.GetValue<int>() ?? 0);
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(Paths.TunOwner)) File.Delete(Paths.TunOwner); } catch { }
    }
}

/// <summary>Уборка адаптера «Весь компьютер» этой установки, если он остался без sing-box (сбой, выключение питания).</summary>
internal static class Recovery
{
    private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    // адреса нашего адаптера «Весь компьютер» (TunConfig.cs). Чужие VPN-адаптеры (Happ и т. п.) не трогаем.
    private const string TunIp4 = TunConfigBuilder.TunIp4, TunIp6 = TunConfigBuilder.TunIp6;

    public static void Sweep()
    {
        if (AppInstance.Forbid("recovery адаптера")) return;
        try
        {
            if (!Elevation.IsAdmin) return;              // без прав администратора адаптер не создавался и не удалится
            if (VpnCore.OurCoreRunning(Paths.SingBox)) return;

            // 1.6.0: убираем только адаптер, который по метке поднят ЭТОЙ установкой (тот же путь exe) и чей владелец уже не работает.
            // Раньше адаптер выбирался по префиксу имени — так можно было удалить адаптер другой версии, работающей рядом.
            var owner = TunOwner.Read();
            if (owner == null) return;   // метки нет: адаптер (если есть) чужой или от версии без метки — не трогаем
            if (!Cleanup.SamePath(owner.Exe, Environment.ProcessPath))
            {
                Log.Write($"recovery: метка адаптера принадлежит другой установке ({owner.Exe}) — не трогаю");
                return;
            }
            if (owner.Pid > 0 && owner.Pid != Environment.ProcessId && IsAlive(owner.Pid))
            {
                Log.Write($"recovery: владелец адаптера (pid {owner.Pid}) ещё работает — не трогаю");
                return;
            }
            var adapterName = owner.Adapter;
            var stale = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => string.Equals(n.Name, adapterName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var n in stale)
            {
                Log.Write($"recovery: остался адаптер «{n.Name}» ({n.OperationalStatus}) — убираю вместе с адресом и маршрутами");
                // 1.3.1: сначала снимаем адрес (даже с выключенного адаптера) — иначе «The object already exists»
                Run("netsh.exe", $"interface ipv4 delete address \"{n.Name}\" {TunIp4}");
                Run("netsh.exe", $"interface ipv6 delete address \"{n.Name}\" {TunIp6}");
                RemoveDevice(n.Id);
            }
            // выключенные и скрытые адаптеры в списке выше не видны — убираем их через PowerShell (только адаптер с точным именем из метки)
            if (!adapterName.Any(c => c is '\'' or '"' or '`' or '$' or '*' or '?' or '[' or ']'))
                Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" +
                    "Get-NetAdapter -Name '" + adapterName + "' -IncludeHidden -ErrorAction SilentlyContinue | " +
                    "Where-Object { $_.Status -ne 'Up' } | ForEach-Object { pnputil /remove-device $_.PnPDeviceID | Out-Null }\"");
            TunOwner.Clear();   // всё убрано — метка больше не нужна
        }
        catch (Exception ex) { Log.Write("recovery: " + ex.Message); }
    }

    private static bool IsAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch { return true; }   // не удалось определить — считаем работающим (безопаснее не трогать)
    }

    private static bool RemoveDevice(string netCfgId)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(NetClass);
            if (cls == null) return false;
            foreach (var sub in cls.GetSubKeyNames())
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k == null) continue;
                    if (!string.Equals(k.GetValue("NetCfgInstanceId") as string, netCfgId, StringComparison.OrdinalIgnoreCase)) continue;
                    var inst = k.GetValue("DeviceInstanceID") as string;
                    if (string.IsNullOrEmpty(inst)) return false;
                    var code = Run("pnputil.exe", $"/remove-device \"{inst}\"");
                    Log.Write($"recovery: pnputil {inst} → {code}");
                    return code == 0;
                }
                catch { }   // ключ Properties закрыт даже для администратора
            }
        }
        catch (Exception ex) { Log.Write("recovery reg: " + ex.Message); }
        return false;
    }

    private static int Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (p == null) return -1;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            return p.WaitForExit(15000) ? p.ExitCode : -2;
        }
        catch (Exception ex) { Log.Write($"recovery {exe}: {ex.Message}"); return -1; }
    }
}
