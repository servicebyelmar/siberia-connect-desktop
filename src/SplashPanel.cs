using System.Drawing.Drawing2D;

namespace SiberiaConnect;

internal sealed class SplashPanel : Panel
{
    private static readonly Color Bg = Color.FromArgb(11, 16, 23);
    private static readonly Color Ice = Color.FromArgb(148, 163, 184);
    private static readonly Color Sky = Color.FromArgb(56, 189, 248);
    private static readonly Color Mint = Color.FromArgb(61, 224, 194);
    private readonly Bitmap _bg;
    private readonly Bitmap _art;

    public SplashPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Bg;
        DoubleBuffered = true;
        ResizeRedraw = true;
        _bg = LoadBmp("aurora.jpg");
        _art = LoadBmp("splash.png") ?? AppInfo.LoadBitmap();
    }

    private static Bitmap LoadBmp(string name)
    {
        try { using var s = AppInfo.Resource(name); return s != null ? new Bitmap(s) : null; }
        catch { return null; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Bg);
        if (_bg != null)
        {
            int bh = (int)(r.Height * 0.62f);
            float k = Math.Max((float)r.Width / _bg.Width, (float)bh / _bg.Height);
            float w = _bg.Width * k, h = _bg.Height * k;
            g.DrawImage(_bg, (r.Width - w) / 2f, 0, w, h);
            using var fade = new LinearGradientBrush(new Rectangle(0, 0, r.Width, bh + 2), Color.FromArgb(40, Bg), Bg, 90f);
            g.FillRectangle(fade, 0, 0, r.Width, bh + 2);
        }
        int cx = r.Width / 2, cy = (int)(r.Height * 0.37f);
        using (var p1 = new Pen(Color.FromArgb(55, Sky), 1.2f)) g.DrawEllipse(p1, cx - 102, cy - 102, 204, 204);
        using (var p2 = new Pen(Color.FromArgb(30, Sky), 1f)) g.DrawEllipse(p2, cx - 84, cy - 84, 168, 168);
        using (var dot = new SolidBrush(Sky)) { g.FillEllipse(dot, cx + 70, cy - 74, 4, 4); g.FillEllipse(dot, cx - 74, cy + 70, 4, 4); }
        if (_art != null) g.DrawImage(_art, cx - 72, cy - 72, 144, 144);
        using var f1 = new Font("Segoe UI", 22F, FontStyle.Bold);
        DrawCentered(g, "SIBERIA", f1, Color.White, cy + 124, r.Width);
        using var f2 = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        DrawCentered(g, "C O N N E C T", f2, Sky, cy + 162, r.Width);
        using var lp = new Pen(Color.FromArgb(70, Sky), 1f);
        g.DrawLine(lp, cx - 88, cy + 170, cx - 54, cy + 170);
        g.DrawLine(lp, cx + 54, cy + 170, cx + 88, cy + 170);
        using var sm = new Font("Segoe UI", 9F);
        DrawCentered(g, "Загрузка…", sm, Ice, r.Height - 54, r.Width);
        using var ml = new Pen(Mint, 2f);
        g.DrawLine(ml, cx - 22, r.Height - 32, cx + 22, r.Height - 32);
    }

    private static void DrawCentered(Graphics g, string text, Font f, Color c, int y, int width)
    {
        var sz = g.MeasureString(text, f);
        using var b = new SolidBrush(c);
        g.DrawString(text, f, b, (width - sz.Width) / 2f, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _bg?.Dispose(); _art?.Dispose(); }
        base.Dispose(disposing);
    }
}
