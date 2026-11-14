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
/// 1.4.0: фоновая проверка (Check), предварительная загрузка (Download) и запуск (Start) разделены,
/// чтобы приложение могло скачать и проверить обновление заранее и поставить его в удобный момент.
/// </summary>
internal static class Updater
{
    // Открытый ключ обновлений (ECDSA P-256). Закрытый — только в GitHub → Settings → Secrets → UPDATE_KEY.
    private const string PublicKey = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEJtbsgSW0nbzyKlp8jluMlmB2AOr5
98jUoOCWQdHLJCSwsZiuQH/5xlXm4uBFIX9eyW3v2k/FFmzLdkBNZbQNNA==
-----END PUBLIC KEY-----";

    /// <summary>Каталог обновлений. Для проверки на тестовом сервере — переменная SC_UPDATE_BASE (подпись и SHA-256 всё равно проверяются).</summary>
    private static string Base =>
        Environment.GetEnvironmentVariable("SC_UPDATE_BASE") is { Length: > 0 } b
            ? b.TrimEnd('/') + "/"
            : $"https://{MainForm.Host}/app/dl/";

    private static int _busy;
    private static string _readyFile = "";

    /// <summary>Версия, чей установщик уже скачан и проверен (пусто — ничего не готово).</summary>
    public static string ReadyVersion { get; private set; } = "";

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

    /// <summary>Манифест полный (версия, SHA-256, подпись)?</summary>
    private static bool Complete(Manifest m) =>
        !string.IsNullOrEmpty(m.Version) && !string.IsNullOrEmpty(m.Sig) && m.Sha256.Length == 64;

    /// <summary>
    /// Фоновая проверка: на сервере версия выше текущей и подпись верна? Возвращает манифест, иначе null
    /// (обновлений нет, сервер недоступен или подпись не прошла — в этих случаях пользователю ничего не показываем).
    /// </summary>
    public static async Task<Manifest> Check()
    {
        try
        {
            var m = await Fetch();
            if (!Complete(m) || Cmp(m.Version, AppInfo.Version) <= 0) return null;
            if (!Verify(m.Version, m.Sha256, m.Sig))
            {
                Log.Write($"update: подпись {m.Version} не прошла проверку — обновление не предлагаем");
                return null;
            }
            return m;
        }
        catch (Exception ex)
        {
            Log.Write("update check: " + ex.Message);
            return null;
        }
    }

    /// <summary>Скачать установщик и сверить SHA-256 с подписанным манифестом. null — файл готов, иначе текст ошибки.</summary>
    public static async Task<string> Download(Manifest m, Action<int> progress)
    {
        if (ReadyVersion == m.Version && File.Exists(_readyFile)) return null;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return "Обновление уже скачивается";
        try
        {
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

            if (HashOf(file) != m.Sha256)
            {
                try { File.Delete(file); } catch { }
                Log.Write("update: SHA-256 не совпал с подписанным манифестом");
                return "Файл обновления не совпал с подписью — обновление отменено";
            }
            _readyFile = file;
            ReadyVersion = m.Version;
            Log.Write($"update: {m.Version} скачано, SHA-256 верен");
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("update: " + ex);
            return "Не удалось скачать обновление — проверь интернет и попробуй ещё раз";
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static string HashOf(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// Запустить скачанный установщик (тихо: без окон, с закрытием старой копии и запуском новой, ключ и настройки остаются).
    /// Перед запуском файл проверяется ещё раз — между скачиванием и установкой его могли подменить.
    /// null — установщик запущен (приложение должно закрыться), иначе текст ошибки.
    /// </summary>
    public static string Start(Manifest m, bool reconnect)
    {
        try
        {
            if (ReadyVersion != m.Version || !File.Exists(_readyFile)) return "Файл обновления не найден — скачай заново";
            if (HashOf(_readyFile) != m.Sha256)
            {
                try { File.Delete(_readyFile); } catch { }
                ReadyVersion = "";
                Log.Write("update: файл изменился после скачивания — отмена");
                return "Файл обновления не совпал с подписью — обновление отменено";
            }
            Log.Write($"update: {AppInfo.Version} → {m.Version}, подпись и SHA-256 верны, запускаю установщик");
            var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-" + (reconnect ? " /scargs=--connect" : "");
            Process.Start(new ProcessStartInfo(_readyFile, args) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("update start: " + ex.Message);
            return "Не удалось запустить установщик — попробуй ещё раз";
        }
    }

    /// <summary>Скачать (если ещё не скачано), проверить и запустить установщик. null — установщик запущен, иначе текст ошибки.</summary>
    public static async Task<string> Install(bool reconnect, Action<int> progress)
    {
        try
        {
            var m = await Fetch();
            if (!Complete(m)) return "Обновление пока не готово — попробуй позже";
            if (Cmp(m.Version, AppInfo.Version) <= 0) return "У тебя уже последняя версия";
            if (!Verify(m.Version, m.Sha256, m.Sig))
            {
                Log.Write($"update: подпись {m.Version} не прошла проверку");
                return "Подпись обновления не прошла проверку — обновление отменено";
            }
            var err = await Download(m, progress);
            if (err != null) return err;
            return Start(m, reconnect);
        }
        catch (Exception ex)
        {
            Log.Write("update: " + ex);
            return "Не удалось скачать обновление — проверь интернет и попробуй ещё раз";
        }
    }
}
