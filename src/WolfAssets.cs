using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace SiberiaConnect;

/// <summary>
/// Файлы анимации логотипа (ролик, кадры, вой), вшитые в exe, для страницы кабинета.
/// Страница берёт их по адресу https://sc-assets.local/&lt;имя&gt; (SCNative.assetBase() в Bridge.js), сеть не используется.
/// Видео запрашивается кусками (Range), поэтому поддержан ответ 206.
/// </summary>
internal static class WolfAssets
{
    private const string Host = "sc-assets.local";
    private static readonly Regex Name = new(@"^wolf_[a-z]+\.(mp4|mp3|jpg)$", RegexOptions.Compiled);
    private static readonly Regex RangeRx = new(@"^bytes=(\d*)-(\d*)$", RegexOptions.Compiled);

    public static void Attach(CoreWebView2 cw)
    {
        cw.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += (_, e) =>
        {
            try { e.Response = Respond(cw.Environment, e.Request); }
            catch (Exception ex) { Log.Write("wolf asset: " + ex.Message); }
        };
    }

    private static CoreWebView2WebResourceResponse Respond(CoreWebView2Environment env, CoreWebView2WebResourceRequest req)
    {
        var name = Path.GetFileName(new Uri(req.Uri).AbsolutePath);
        if (!Name.IsMatch(name)) return env.CreateWebResourceResponse(null, 404, "Not Found", "");

        using var src = AppInfo.Resource("wolf/" + name);
        if (src == null) return env.CreateWebResourceResponse(null, 404, "Not Found", "");   // файла нет (например, wolf_howl.mp3 ещё не добавлен)

        long total = src.Length;
        var mime = name.EndsWith(".mp4") ? "video/mp4" : name.EndsWith(".mp3") ? "audio/mpeg" : "image/jpeg";
        var common = $"Content-Type: {mime}\nAccept-Ranges: bytes\nAccess-Control-Allow-Origin: *";

        var m = req.Headers.Contains("Range") ? RangeRx.Match(req.Headers.GetHeader("Range").Trim()) : null;
        long start = 0, end = total - 1;
        int status = 200;
        string reason = "OK", extra = "";
        if (m != null && m.Success)
        {
            var a = m.Groups[1].Value; var b = m.Groups[2].Value;
            start = a.Length == 0 ? Math.Max(0, total - (b.Length == 0 ? 0 : long.Parse(b))) : long.Parse(a);
            end = a.Length == 0 || b.Length == 0 ? total - 1 : Math.Min(long.Parse(b), total - 1);
            if (start > end || start >= total)
                return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", common + $"\nContent-Range: bytes */{total}");
            status = 206; reason = "Partial Content"; extra = $"\nContent-Range: bytes {start}-{end}/{total}";
        }

        var buf = new byte[end - start + 1];
        src.Seek(start, SeekOrigin.Begin);
        src.ReadExactly(buf, 0, buf.Length);
        return env.CreateWebResourceResponse(new MemoryStream(buf, false), status, reason, common + extra + $"\nContent-Length: {buf.Length}");
    }
}
