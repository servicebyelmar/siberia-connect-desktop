using System.Text;

namespace SiberiaConnect;

/// <summary>
/// CI: <c>SiberiaConnect.exe --selftest-telemetry &lt;каталог результатов&gt;</c>.
/// Чистая логика фоновой телеметрии (TelemetryGate): фильтр ложных срабатываний, дедупликация, очистка лога.
/// Без сети, без файлов приложения, без SC_INSTANCE. Итог: telemetry-ok.txt либо error.txt.
/// </summary>
internal static class TelemetrySelfTest
{
    private static readonly double Never = double.PositiveInfinity;

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = new List<string>();
        try
        {
            Execute(report);
            File.WriteAllText(Path.Combine(dir, "telemetry-ok.txt"), string.Join(Environment.NewLine, report));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "error.txt"), string.Join(Environment.NewLine, report) + Environment.NewLine + ex);
        }
    }

    private static void Check(List<string> report, string name, bool ok)
    {
        report.Add((ok ? "PASS " : "FAIL ") + name);
        if (!ok) throw new Exception("не прошла проверка: " + name);
    }

    private static void Execute(List<string> r)
    {
        // ── решение «слать / не слать» ──
        Check(r, "реальный сбой: интернет есть, ничего лишнего → слать",
            TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, Never, Never, Never) == null);
        foreach (var reason in TelemetryGate.Reasons)
            Check(r, "причина допустима: " + reason, TelemetryGate.Decide(reason, true, Never, Never, Never) == null);
        Check(r, "обычного интернета нет → молчим", TelemetryGate.Decide(TelemetryGate.NoTraffic, false, Never, Never, Never) == "no_base_net");
        Check(r, "пользователь только что нажал «Отключить» → молчим", TelemetryGate.Decide(TelemetryGate.SessionDropped, true, Never, 5, Never) == "user_disconnect");
        Check(r, "«Отключить» было давно (≥30 с) → слать", TelemetryGate.Decide(TelemetryGate.SessionDropped, true, Never, 30, Never) == null);
        Check(r, "5 с после сна → молчим", TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, 5, Never, Never) == "after_sleep");
        Check(r, "ровно 10 с после сна → слать", TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, 10, Never, Never) == null);
        Check(r, "тот же отчёт 9 минут назад → молчим", TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, Never, Never, 540) == "duplicate");
        Check(r, "тот же отчёт 10 минут назад → слать", TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, Never, Never, 600) == null);
        Check(r, "неизвестная причина → молчим", TelemetryGate.Decide("AUTH_FAILED", true, Never, Never, Never) == "bad_reason");
        Check(r, "пустая причина → молчим", TelemetryGate.Decide(null, true, Never, Never, Never) == "bad_reason");
        Check(r, "несколько условий сразу: нет интернета важнее остальных", TelemetryGate.Decide(TelemetryGate.NoTraffic, false, 1, 1, 1) == "no_base_net");

        // ── дедупликация по причинам ──
        var d = new TelemetryGate.Dedup();
        var t0 = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        Check(r, "дедуп: раньше не слали → ∞", double.IsPositiveInfinity(d.SecondsSince(TelemetryGate.NoTraffic, t0)));
        d.Mark(TelemetryGate.NoTraffic, t0);
        Check(r, "дедуп: через 5 мин блок", TelemetryGate.Decide(TelemetryGate.NoTraffic, true, Never, Never, d.SecondsSince(TelemetryGate.NoTraffic, t0.AddMinutes(5))) == "duplicate");
        Check(r, "дедуп: другая причина не блокируется", TelemetryGate.Decide(TelemetryGate.TunnelTimeout, true, Never, Never, d.SecondsSince(TelemetryGate.TunnelTimeout, t0.AddMinutes(5))) == null);
        Check(r, "дедуп: через 11 мин можно снова", TelemetryGate.Decide(TelemetryGate.NoTraffic, true, Never, Never, d.SecondsSince(TelemetryGate.NoTraffic, t0.AddMinutes(11))) == null);

        // ── очистка лога ──
        var dirty = new[]
        {
            "connect vless://11111111-2222-3333-4444-555555555555@1.2.3.4:443?x=1",
            "uuid 11111111-2222-3333-4444-555555555555 ip 85.12.34.56",
            "X-App-Token: abcdef123456",
            "mail a.b@example.com",
            "code SC-ABCDE-FGHJK and SC-ABCD-EFGH-JKLM",
            "hex 0123456789abcdef0123456789abcdef",
            "tok " + new string('A', 40),
            "v6 2001:db8:0:0:0:0:0:1",
            @"file C:\Users\ElmarSecret\AppData\x.log",
            "sub https://sibirapp.duckdns.org/sub/abcd1234",
            new string('x', 1000)
        };
        var clean = string.Join("\n", TelemetryGate.CleanLog(dirty));
        foreach (var secret in new[] { "11111111-2222", "85.12.34.56", "1.2.3.4", "abcdef123456", "example.com", "ABCDE-FGHJK", "EFGH-JKLM", "0123456789abcdef", "AAAAAAAAAAAA", "2001:db8", "ElmarSecret", "abcd1234" })
            Check(r, "лог: «" + secret + "» вычищено", !clean.Contains(secret));
        Check(r, "лог: каждая строка ≤ 300", TelemetryGate.CleanLog(dirty).All(l => l.Length <= TelemetryGate.MaxLineLength));
        var many = Enumerable.Range(0, 100).Select(i => "l" + i).ToArray();
        var cut = TelemetryGate.CleanLog(many);
        Check(r, "лог: ≤ 25 строк, берутся последние", cut.Length == 25 && cut[0] == "l75" && cut[24] == "l99");
        Check(r, "лог: пустой и null", TelemetryGate.CleanLog(null).Length == 0 && TelemetryGate.CleanLog(Array.Empty<string>()).Length == 0);
        Check(r, "лог: обычные строки не портятся", TelemetryGate.CleanLog(new[] { "watchdog: туннель не пропускает трафик — восстанавливаю" })[0] == "watchdog: туннель не пропускает трафик — восстанавливаю");
    }
}
