using System.Drawing.Drawing2D;

namespace SiberiaConnect;

internal sealed class SplashPanel : Panel
{
    private static readonly Color Deep = Color.FromArgb(5, 13, 26);      // #050D1A
    private static readonly Color Mid = Color.FromArgb(10, 28, 60);      // #0A1C3C
    private static readonly Color Ice = Color.FromArgb(143, 168, 200);   // #8FA8C8
    private static readonly Color Neon = Color.FromArgb(0, 210, 255);    // #00D2FF

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
                _logo = new Bitmap(tmp);
            }
        }
        catch (Exception ex) { Log.Write("splash: " + ex.Message); }
        if (_logo == null) _logo = AppInfo.LoadBitmap();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var bg = new LinearGradientBrush(r, Deep, Mid, 90f)) g.FillRectangle(bg, r);

        int side = Math.Min((int)(r.Width * 0.62), 240);
        int topY = r.Height / 2 - side / 2 - 36;
        if (_logo != null)
            g.DrawImage(_logo, (r.Width - side) / 2, topY, side, side);

        using var f = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
        DrawCentered(g, "SIBERIA CONNECT", f, Color.White, topY + side + 18, r.Width);

        using var small = new Font("Segoe UI", 9.5F);
        DrawCentered(g, "Загрузка…", small, Ice, r.Height - 56, r.Width);
        using var line = new Pen(Neon, 2.5f);
        g.DrawLine(line, r.Width / 2 - 28, r.Height - 34, r.Width / 2 + 28, r.Height - 34);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, int y, int width)
    {
        var sz = g.MeasureString(text, font);
        using var br = new SolidBrush(color);
        g.DrawString(text, font, br, (width - sz.Width) / 2, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}
