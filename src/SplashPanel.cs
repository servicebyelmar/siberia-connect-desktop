using System.Drawing.Drawing2D;

namespace SiberiaConnect;

internal sealed class SplashPanel : Panel
{
    private static readonly Color Deep = Color.FromArgb(11, 13, 16);     // #0B0D10
    private static readonly Color Mid = Color.FromArgb(19, 25, 31);     // #13191F
    private static readonly Color Ice = Color.FromArgb(169, 181, 189);  // #A9B5BD
    private static readonly Color Neon = Color.FromArgb(98, 219, 247);

    private readonly Bitmap _logo;

    public SplashPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Deep;
        DoubleBuffered = true;
        ResizeRedraw = true;
        using var asset = AppInfo.Resource("wolf/wolf-splash.png");
        using var image = Image.FromStream(asset);
        _logo = new Bitmap(image);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var bg = new LinearGradientBrush(r, Deep, Mid, 90f)) g.FillRectangle(bg, r);

        var side = Math.Min(Math.Min(r.Width * 0.62f, r.Height * 0.48f), 240f * DeviceDpi / 96f);
        var topY = r.Height / 2f - side / 2f - 36f;
        var orbit = new RectangleF((r.Width - side) / 2f, topY, side, side);
        using var orbitFill = new SolidBrush(Color.FromArgb(8, 21, 35));
        g.FillEllipse(orbitFill, orbit);

        // The portrait fits in the circle even with scaling set to 200%.
        var ratio = Math.Min(side * .68f / _logo.Width, side * .68f / _logo.Height);
        var width = _logo.Width * ratio;
        var height = _logo.Height * ratio;
        g.DrawImage(_logo, orbit.X + (side - width) / 2f, orbit.Y + (side - height) / 2f, width, height);
        using var ring = new Pen(Neon, 2f);
        g.DrawEllipse(ring, orbit);

        using var f = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
        DrawCentered(g, "SIBERIA CONNECT", f, Color.White, topY + side + 18, r.Width);

        using var small = new Font("Segoe UI", 9.5F);
        DrawCentered(g, "Загрузка…", small, Ice, r.Height - 56, r.Width);
        using var line = new Pen(Neon, 2.5f);
        g.DrawLine(line, r.Width / 2 - 28, r.Height - 34, r.Width / 2 + 28, r.Height - 34);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, float y, int width)
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
