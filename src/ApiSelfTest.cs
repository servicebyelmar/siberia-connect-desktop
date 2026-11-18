using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace SiberiaConnect;

/// <summary>
/// CI: <c>SiberiaConnect.exe --selftest-api &lt;каталог результатов&gt; &lt;rank-ok.json&gt;</c>.
/// Проверяет разбор ответа GET nodes/rank (сохранённый реальный ответ) и поведение клиента при сбоях рейтинга
/// на локальном сервере 127.0.0.1. Боевой API не затрагивается. Работает только в режиме экземпляра
/// (SC_INSTANCE + SC_DATA_ROOT), поэтому ни общий профиль, ни система не меняются; адрес API подменяется SC_API_BASE
/// (допустим только loopback). Итог: api-ok.txt либо error.txt в каталоге результатов.
/// </summary>
internal static class ApiSelfTest
{
    public static void Run(string dir, string fixture)
    {
        Directory.CreateDirectory(dir);
        var report = new List<string>();
        try
        {
            Execute(fixture, report);
            File.WriteAllText(Path.Combine(dir, "api-ok.txt"), string.Join(Environment.NewLine, report));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "error.txt"), string.Join(Environment.NewLine, report) + Environment.NewLine + ex);
        }
    }

    private static void Execute(string fixturePath, List<string> report)
    {
        if (!AppInstance.Active) throw new Exception("нужен SC_INSTANCE (режим экземпляра)");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SC_DATA_ROOT"))) throw new Exception("нужен SC_DATA_ROOT");
        var okBody = File.ReadAllText(fixturePath, new UTF8Encoding(false));

        using var srv = new FakeApi();
        // до первого обращения к Api: адрес читается один раз
        Environment.SetEnvironmentVariable("SC_API_BASE", $"http://127.0.0.1:{srv.Port}/app/api/v1/");

        var core = new VpnCore();
        core.State.AuthToken = "test-token";
        core.State.Servers.Add(new ServerItem { Guid = "g1", Name = "🇫🇮 Ледокол · Быстрый", Uri = "vless://x@example.com:443" });
        var fetch = typeof(VpnCore).GetMethod("FetchRank", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FetchRank не найден");

        (object result, long ms) Call()
        {
            var sw = Stopwatch.StartNew();
            var r = fetch.Invoke(core, null);
            return (r, sw.ElapsedMilliseconds);
        }
        void Expect(bool cond, string what) { if (!cond) throw new Exception("FAIL: " + what); report.Add("ok: " + what); }

        // 1. сохранённый реальный ответ разбирается
        srv.Reply(200, okBody);
        var (r1, t1) = Call();
        Expect(r1 is IDictionary d1 && d1.Count == 2, "nodes/rank: 2 узла разобраны");
        var d = (IDictionary)r1;
        Expect(d.Contains("ice") && d.Contains("buran") && !d.Contains("taiga"), "ключи ice и buran есть, taiga нет");
        double N(object o, string p) => Convert.ToDouble(o.GetType().GetProperty(p)!.GetValue(o));
        bool A(object o) => (bool)o.GetType().GetProperty("Available")!.GetValue(o)!;
        Expect(N(d["ice"], "Rank") == 1 && A(d["ice"]) && Math.Abs(N(d["ice"], "Load") - 0.304) < 1e-9, "ice: rank 1, available, load 0.304");
        Expect(N(d["buran"], "Rank") == 2 && A(d["buran"]) && Math.Abs(N(d["buran"], "Load") - 0.271) < 1e-9, "buran: rank 2, available, load 0.271");
        Expect(srv.LastRequest.StartsWith("GET /app/api/v1/nodes/rank ", StringComparison.Ordinal), "запрос: GET /app/api/v1/nodes/rank");
        Expect(srv.LastRequest.Contains("X-App-Token: test-token", StringComparison.OrdinalIgnoreCase), "запрос несёт X-App-Token");

        // 2. сбои ответа сервера: null, быстро
        foreach (var (name, code, body) in new[]
        {
            ("HTTP 500", 500, "{\"ok\": false}"),
            ("HTTP 200, не JSON", 200, "<html>oops</html>"),
            ("HTTP 200, пустое тело", 200, ""),
            ("HTTP 200, ok=false", 200, "{\"ok\": false, \"nodes\": []}"),
            ("HTTP 200, ok=true, nodes пуст", 200, "{\"ok\": true, \"nodes\": []}"),
            ("HTTP 200, nodes не массив", 200, "{\"ok\": true, \"nodes\": 5}"),
            ("HTTP 401", 401, "{\"ok\": false}"),
        })
        {
            srv.Reply(code, body);
            var (r, t) = Call();
            Expect(r == null && t < 2500, $"{name}: null за {t} мс");
        }

        // 3. «молчащий» сервер: общий срок ~3 с (RankTimeoutMs), не больше
        srv.Mode = "blackhole";
        var (r3, t3) = Call();
        Expect(r3 == null && t3 >= 2500 && t3 <= 4500, $"сервер принял соединение и молчит: null за {t3} мс (ожидалось ~3000)");

        // 3б. заголовки пришли, а тело не дочитывается (замерло / оборвано): общий срок 3 с держится и на чтении тела.
        // До исправления такой вызов висел ~5 с (чтение тела не отменялось общим сроком).
        srv.Mode = "slowbody";
        var (r3b, t3b) = Call();
        Expect(r3b == null && t3b >= 2500 && t3b <= 3400, $"заголовки есть, тело замерло: null за {t3b} мс (общий срок 3000)");
        srv.Mode = "truncated";
        var (r3c, t3c) = Call();
        Expect(r3c == null && t3c <= 3400, $"тело оборвано закрытием соединения: null за {t3c} мс");
        srv.Mode = "chunkstall";
        var (r3d, t3d) = Call();
        Expect(r3d == null && t3d >= 2500 && t3d <= 3400, $"часть тела пришла и замерла: null за {t3d} мс (общий срок 3000)");

        // 4. токена нет — запрос вообще не уходит
        srv.Reply(200, okBody);
        var hits = srv.Hits;
        var saved = core.State.AuthToken;
        core.State.AuthToken = "";
        var (r4, t4) = Call();
        Expect(r4 == null && srv.Hits == hits, "без токена: null, запросов к серверу нет");
        core.State.AuthToken = saved;

        // 5. после сбоев вход и серверы на месте
        Expect(core.State.AuthToken == "test-token" && core.State.Servers.Count == 1, "токен и серверы не сброшены");

        // 6. порт закрыт (connection refused)
        var s = srv.Close();
        var (r6, t6) = Call();
        Expect(r6 == null && t6 < 4500, $"порт закрыт: null за {t6} мс");
        Expect(core.State.AuthToken == "test-token" && core.State.Servers.Count == 1, "после отказа соединения токен и серверы на месте");
        report.Add("done");
    }

    /// <summary>Минимальный HTTP-сервер на 127.0.0.1 (без HttpListener: не нужны права и резервирование адреса).</summary>
    private sealed class FakeApi : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _held = new();
        private volatile int _code = 200;
        private volatile string _body = "";
        private int _hits;
        public volatile string Mode = "reply";
        public volatile string LastRequest = "";
        public int Port { get; }
        public int Hits => Volatile.Read(ref _hits);

        public FakeApi()
        {
            _l.Start();
            Port = ((IPEndPoint)_l.LocalEndpoint).Port;
            new Thread(Loop) { IsBackground = true, Name = "fake-api" }.Start();
        }

        public void Reply(int code, string body) { _code = code; _body = body; Mode = "reply"; }

        /// <summary>Закрыть слушающий сокет: дальше соединения отвергаются.</summary>
        public bool Close() { try { _l.Stop(); return true; } catch { return false; } }

        private void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _l.AcceptTcpClient(); } catch { return; }
                Task.Run(() => Handle(c));
            }
        }

        private void Handle(TcpClient c)
        {
            try
            {
                var st = c.GetStream();
                st.ReadTimeout = 2000;
                var sb = new StringBuilder();
                var buf = new byte[2048];
                while (!sb.ToString().Contains("\r\n\r\n"))
                {
                    var n = st.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                }
                LastRequest = sb.ToString();
                Interlocked.Increment(ref _hits);
                var mode = Mode;
                if (mode == "blackhole") { lock (_held) _held.Add(c); return; }   // соединение держим, ответа нет
                if (mode is "slowbody" or "truncated" or "chunkstall")
                {
                    // заголовки с Content-Length 1000, но тело не приходит целиком
                    var prefix = mode == "slowbody" ? "" : "{\"ok\": tr";
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n" + prefix);
                    st.Write(head, 0, head.Length);
                    st.Flush();
                    if (mode == "truncated") { c.Close(); return; }   // обрыв соединения посреди тела
                    lock (_held) _held.Add(c);                        // иначе держим открытым: тело замерло
                    return;
                }
                var body = Encoding.UTF8.GetBytes(_body);
                var h = Encoding.ASCII.GetBytes($"HTTP/1.1 {_code} X\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                st.Write(h, 0, h.Length);
                st.Write(body, 0, body.Length);
                c.Close();
            }
            catch { try { c.Close(); } catch { } }
        }

        public void Dispose()
        {
            try { _l.Stop(); } catch { }
            lock (_held) foreach (var c in _held) try { c.Close(); } catch { }
        }
    }
}
