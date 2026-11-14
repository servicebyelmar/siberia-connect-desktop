using System.Drawing.Drawing2D;

namespace SiberiaConnect;

/// <summary>
/// 1.4.0: арктическая заставка при запуске. Если в сборку вшит splash.png (ледокол) — он закрывает всё окно
/// (режим «cover»), иначе рисуется заготовка: тёмно-синий градиент, логотип и название.
/// Показывается, пока WebView2 поднимает интерфейс; прячется через MainForm.HideSplash.
/// </summary>
internal sealed class SplashPanel : Panel
{
    private static readonly Color Deep = Color.FromArgb(6, 15, 42);      // #06122b
    private static readonly Color Mid = Color.FromArgb(10, 42, 80);
    private static readonly Color Ice = Color.FromArgb(127, 211, 255);   // #7fd3ff
    private static readonly Color Mint = Color.FromArgb(61, 224, 194);   // #3de0c2

    private readonly Bitmap _art;
    private readonly Bitmap _logo;

    public SplashPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Deep;
        DoubleBuffered = true;
        ResizeRedraw = true;
        try
        {
            using var s = AppInfo.Resource("splash.png");
            if (s != null)
            {
                using var tmp = new Bitmap(s);
                _art = new Bitmap(tmp);   // копия: поток из ресурса закрывается
            }
        }
        catch (Exception ex) { Log.Write("splash: " + ex.Message); }
        if (_art == null) _logo = AppInfo.LoadBitmap();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var bg = new LinearGradientBrush(r, Deep, Mid, 90f)) g.FillRectangle(bg, r);

        if (_art != null)
        {
            var k = Math.Max((float)r.Width / _art.Width, (float)r.Height / _art.Height);
            float w = _art.Width * k, h = _art.Height * k;
            g.DrawImage(_art, (r.Width - w) / 2, (r.Height - h) / 2, w, h);
        }
        else
        {
            int side = Math.Min(r.Width / 2, 140);
            if (_logo != null)
                g.DrawImage(_logo, (r.Width - side) / 2, r.Height / 2 - side, side, side);
            using var f = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            DrawCentered(g, "Siberia Connect", f, Color.White, r.Height / 2 + 18, r.Width);
        }

        using var small = new Font("Segoe UI", 9F);
        DrawCentered(g, "Загрузка…", small, Ice, r.Height - 56, r.Width);
        using var line = new Pen(Mint, 2f);
        g.DrawLine(line, r.Width / 2 - 24, r.Height - 34, r.Width / 2 + 24, r.Height - 34);
    }

    private static void DrawCentered(Graphics g, string text, Font f, Color c, int y, int width)
    {
        var sz = g.MeasureString(text, f);
        using var b = new SolidBrush(c);
        g.DrawString(text, f, b, (width - sz.Width) / 2, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _art?.Dispose(); _logo?.Dispose(); }
        base.Dispose(disposing);
    }
}
