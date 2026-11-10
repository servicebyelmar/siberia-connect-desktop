using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>
/// Обновление приложения изнутри.
/// Сервер отдаёт dl/win.json (версия, SHA-256 установщика, подпись). Подпись ставит сборка GitHub ключом,
/// который хранится только в секретах GitHub. Здесь зашит открытый ключ: установщик запускается,
/// только если подпись верна и SHA-256 скачанного файла совпал. Взломанный сервер не сможет подсунуть чужой файл.
/// </summary>
internal static class Updater
{
    // Открытый ключ обновлений (ECDSA P-256). Закрытый — только в GitHub → Settings → Secrets → UPDATE_KEY.
    private const string PublicKey = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEJtbsgSW0nbzyKlp8jluMlmB2AOr5
98jUoOCWQdHLJCSwsZiuQH/5xlXm4uBFIX9eyW3v2k/FFmzLdkBNZbQNNA==
-----END PUBLIC KEY-----";

    private static readonly string Base = $"https://{MainForm.Host}/app/dl/";
    private static int _busy;

    private static HttpClient Make(bool useProxy) => new(new HttpClientHandler
    {
        UseProxy = useProxy,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = Pinning.Check
    }) { Timeout = TimeSpan.FromMinutes(15) };

    private static readonly HttpClient Direct = Make(false);
    private static readonly HttpClient ViaSystem = Make(true);

    public sealed class Manifest
    {
        public string Version = "", Sha256 = "", Sig = "";
        public long Size;
    }

    public static int Cmp(string a, string b)
    {
        int[] P(string s) => (s ?? "").Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var x = P(a); var y = P(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var d = (i < x.Length ? x[i] : 0) - (i < y.Length ? y[i] : 0);
            if (d != 0) return d > 0 ? 1 : -1;
        }
        return 0;
    }

    private static async Task<HttpResponseMessage> Get(string url, CancellationToken ct)
    {
        try
        {
            var r = await Direct.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (r.IsSuccessStatusCode) return r;
            r.Dispose();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log.Write("update direct: " + ex.Message); }
        return await ViaSystem.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public static async Task<Manifest> Fetch()
    {
        using var r = await Get(Base + "win.json?" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), CancellationToken.None);
        r.EnsureSuccessStatusCode();
        var n = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        string S(string k) { try { return n?[k]?.GetValue<string>() ?? ""; } catch { return ""; } }
        long L(string k) { try { return n?[k]?.GetValue<long>() ?? 0; } catch { return 0; } }
        return new Manifest { Version = S("version"), Sha256 = S("sha256").ToLowerInvariant(), Sig = S("sig"), Size = L("size") };
    }

    public static bool Verify(string version, string sha256, string sigB64)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(PublicKey);
            var msg = Encoding.ASCII.GetBytes($"SiberiaConnect-Windows|{version}|{sha256.ToLowerInvariant()}");
            return ec.VerifyData(msg, Convert.FromBase64String(sigB64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) { Log.Write("update verify: " + ex.Message); return false; }
    }

    /// <summary>Скачать, проверить и запустить установщик. null — установщик запущен (приложение должно закрыться), иначе текст ошибки.</summary>
    public static async Task<string> Install(bool reconnect, Action<int> progress)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return "Обновление уже скачивается";
        try
        {
            var m = await Fetch();
            if (string.IsNullOrEmpty(m.Version) || string.IsNullOrEmpty(m.Sig) || m.Sha256.Length != 64)
                return "Обновление пока не готово — попробуй позже";
            if (Cmp(m.Version, AppInfo.Version) <= 0) return "У тебя уже последняя версия";
            if (!Verify(m.Version, m.Sha256, m.Sig))
            {
                Log.Write($"update: подпись {m.Version} не прошла проверку");
                return "Подпись обновления не прошла проверку — обновление отменено";
            }

            var file = Path.Combine(Path.GetTempPath(), "SiberiaConnect-Update.exe");
            try { File.Delete(file); } catch { }
            using (var r = await Get(Base + "SiberiaConnect-Setup.exe?" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), CancellationToken.None))
            {
                r.EnsureSuccessStatusCode();
                var total = r.Content.Headers.ContentLength ?? m.Size;
                await using var src = await r.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                long done = 0; var last = -1;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0)
                    {
                        var p = (int)Math.Min(100, done * 100 / total);
                        if (p != last) { last = p; progress?.Invoke(p); }
                    }
                }
            }

            string sha;
            await using (var fs = File.OpenRead(file)) sha = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (sha != m.Sha256)
            {
                try { File.Delete(file); } catch { }
                Log.Write($"update: SHA-256 не совпал ({sha} ≠ {m.Sha256})");
                return "Файл обновления не совпал с подписью — обновление отменено";
            }

            Log.Write($"update: {AppInfo.Version} → {m.Version}, подпись и SHA-256 верны, запускаю установщик");
            var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-" + (reconnect ? " /scargs=--connect" : "");
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("update: " + ex);
            return "Не удалось скачать обновление — проверь интернет и попробуй ещё раз";
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
