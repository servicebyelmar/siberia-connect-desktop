using System.Drawing.Drawing2D;

namespace SiberiaConnect;

internal sealed class SplashPanel : Panel
{
    private static readonly Color Deep = Color.FromArgb(6, 19, 32);     // #0B0D10
    private static readonly Color Mid = Color.FromArgb(13, 40, 59);     // #13191F
    private static readonly Color Ice = Color.FromArgb(174, 205, 221);  // #A9B5BD
    private static readonly Color Neon = Color.FromArgb(125, 225, 251);  // #D9E1E6

    private readonly Bitmap _logo;

    public SplashPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Deep;
        DoubleBuffered = true;
        ResizeRedraw = true;
        _logo = CreateWolfBitmap();
    }

    private static Bitmap CreateWolfBitmap()
    {
        const int size = 256;
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var head = new[]
        {
            new PointF(48, 48), new PointF(91, 74), new PointF(128, 62),
            new PointF(165, 74), new PointF(208, 48), new PointF(196, 112),
            new PointF(214, 157), new PointF(181, 194), new PointF(128, 222),
            new PointF(75, 194), new PointF(42, 157), new PointF(60, 112)
        };
        using var fill = new SolidBrush(Color.FromArgb(13, 40, 59));
        using var outline = new Pen(Color.FromArgb(125, 225, 251), 5) { LineJoin = LineJoin.Round };
        g.FillPolygon(fill, head);
        g.DrawPolygon(outline, head);

        using var brow = new Pen(Color.FromArgb(174, 205, 221), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(brow, new[]
        {
            new PointF(60, 95), new PointF(94, 111), new PointF(116, 100),
            new PointF(128, 116), new PointF(140, 100), new PointF(162, 111), new PointF(196, 95)
        });

        using var detail = new Pen(Color.FromArgb(125, 225, 251), 8) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(detail, new[]
        {
            new PointF(67, 132), new PointF(105, 143), new PointF(128, 126),
            new PointF(151, 143), new PointF(189, 132)
        });
        g.DrawLines(detail, new[] { new PointF(88, 167), new PointF(128, 189), new PointF(168, 167) });
        g.DrawLine(detail, 128, 130, 128, 169);

        using var nose = new SolidBrush(Color.FromArgb(125, 225, 251));
        g.FillPolygon(nose, new[] { new PointF(116, 168), new PointF(128, 177), new PointF(140, 168) });
        return bitmap;
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
        // Subtle ice-metal orbit around the wolf; no timers or heavy media resources.
        var halo = new Rectangle((r.Width - side) / 2 - 12, topY - 12, side + 24, side + 24);
        using (var pen = new Pen(Color.FromArgb(83, 95, 213, 248), 2F))
            g.DrawEllipse(pen, halo);
        halo.Inflate(10, 10);
        using (var pen = new Pen(Color.FromArgb(31, 95, 213, 248), 1F))
            g.DrawEllipse(pen, halo);
        if (_logo != null)
            g.DrawImage(_logo, (r.Width - side) / 2, topY, side, side);

        using var f = new Font("Segoe UI Semibold", 22F, FontStyle.Bold);
        DrawCentered(g, "SIBERIA", f, Color.White, topY + side + 12, r.Width);
        using var wordmarkSub = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        DrawCentered(g, "C  O  N  N  E  C  T", wordmarkSub, Neon, topY + side + 50, r.Width);

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
