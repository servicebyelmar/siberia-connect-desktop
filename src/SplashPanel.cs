using System.Drawing.Drawing2D;
namespace SiberiaConnect;
internal sealed class SplashPanel : Panel
{
    private readonly Bitmap _bg;
    public SplashPanel()
    {
        Dock = DockStyle.Fill; BackColor = Color.FromArgb(6, 15, 42);
        DoubleBuffered = true; ResizeRedraw = true;
        using var a = AppInfo.Resource("wolf/wolf-splash.png");
        using var im = Image.FromStream(a);
        _bg = new Bitmap(im);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var s = Math.Max((float)r.Width / _bg.Width, (float)r.Height / _bg.Height);
        var w = _bg.Width * s; var h = _bg.Height * s;
        g.DrawImage(_bg, (r.Width - w) / 2f, (r.Height - h) / 2f, w, h);
    }
    protected override void Dispose(bool d) { if (d) _bg?.Dispose(); base.Dispose(d); }
}
