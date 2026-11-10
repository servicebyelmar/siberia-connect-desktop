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

/// <summary>Уборка адаптера «Siberia Connect», если он остался без sing-box (сбой, выключение питания).</summary>
internal static class Recovery
{
    private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public static void Sweep()
    {
        try
        {
            if (!Elevation.IsAdmin) return;              // без прав администратора адаптер не создавался и не удалится
            if (VpnCore.OurCoreRunning(Paths.SingBox)) return;
            var stale = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.Name.StartsWith(TunConfigBuilder.InterfaceName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var n in stale)
            {
                Log.Write($"recovery: остался адаптер «{n.Name}» ({n.OperationalStatus}) — убираю вместе с маршрутами");
                if (!RemoveDevice(n.Id))
                    Run("netsh.exe", $"interface set interface name=\"{n.Name}\" admin=disabled");
            }
        }
        catch (Exception ex) { Log.Write("recovery: " + ex.Message); }
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
