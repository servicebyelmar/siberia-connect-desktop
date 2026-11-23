using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SiberiaConnect;

/// <summary>Короткая заставка на время загрузки WebView2.</summary>
internal sealed class SplashPanel : Panel
{
    private static readonly Color Surface = Color.FromArgb(10, 16, 25);
    private static readonly Color BrandText = Color.FromArgb(237, 246, 255);
    private static readonly Color MutedText = Color.FromArgb(155, 175, 194);
    private static readonly Color Rail = Color.FromArgb(34, 48, 65);
    private static readonly Color Accent = Color.FromArgb(142, 240, 212);

    private readonly Bitmap _wolf;
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 40 };
    private readonly Stopwatch _clock = new();

    public SplashPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Surface;
        DoubleBuffered = true;
        ResizeRedraw = true;
        _wolf = LoadWolf();
        _animation.Tick += (_, _) => Invalidate();
    }

    private static Bitmap LoadWolf()
    {
        try
        {
            using var stream = AppInfo.Resource("siberia-splash-wolf.jpg");
            if (stream != null)
            {
                using var original = new Bitmap(stream);
                return new Bitmap(original);
            }
        }
        catch (Exception ex) { Log.Write("splash logo: " + ex.Message); }

        return AppInfo.LoadBitmap();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SyncAnimation();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        SyncAnimation();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _animation?.Stop();
        _clock?.Stop();
        base.OnHandleDestroyed(e);
    }

    private void SyncAnimation()
    {
        if (_animation == null || IsDisposed || Disposing) return;
        if (IsHandleCreated && Visible && SystemInformation.IsMenuAnimationEnabled)
        {
            _clock.Start();
            _animation.Start();
        }
        else
        {
            _animation.Stop();
            _clock.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = ClientRectangle;
        if (bounds.Width < 8 || bounds.Height < 8) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var scale = DeviceDpi / 96f;
        float Px(float logical) => logical * scale;

        using var brandFont = new Font("Segoe UI", 21F, FontStyle.Bold);
        using var statusFont = new Font("Segoe UI", 9F);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        using var titleBrush = new SolidBrush(BrandText);
        using var statusBrush = new SolidBrush(MutedText);
        var midX = bounds.Width / 2f;
        var emblemSize = Math.Min(Px(154), Math.Min(bounds.Width * .56f, bounds.Height * .35f));
        var brandHeight = g.MeasureString("Siberia Connect", brandFont).Height;
        var emblemTop = bounds.Height * .43f - (emblemSize + Px(22) + brandHeight) / 2f;
        var emblem = new RectangleF(midX - emblemSize / 2f, emblemTop, emblemSize, emblemSize);
        DrawEmblem(g, emblem, scale);
        g.DrawString("Siberia Connect", brandFont, titleBrush,
            new PointF(midX, emblem.Bottom + Px(22)), center);

        var trackY = bounds.Height - Px(43);
        g.DrawString("Запуск клиента…", statusFont, statusBrush,
            new PointF(midX, trackY - Px(10) - statusFont.GetHeight(g)), center);
        var trackWidth = Math.Min(Px(88), bounds.Width / 3f);
        using var track = new Pen(Rail, Px(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var highlight = new Pen(Accent, Px(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var left = midX - trackWidth / 2f;
        var segment = trackWidth * .29f;
        var progress = _animation.Enabled ? (1f - (float)Math.Cos(_clock.Elapsed.TotalSeconds * Math.PI)) / 2f : .5f;
        var x = left + (trackWidth - segment) * progress;
        g.DrawLine(track, left, trackY, left + trackWidth, trackY);
        g.DrawLine(highlight, x, trackY, x + segment, trackY);
    }

    private void DrawEmblem(Graphics g, RectangleF bounds, float scale)
    {
        var inner = RectangleF.Inflate(bounds, -8f * scale, -8f * scale);
        if (inner.Width > 0 && _wolf != null)
        {
            using var clip = new GraphicsPath();
            clip.AddEllipse(inner);
            var state = g.Save();
            g.SetClip(clip, CombineMode.Intersect);
            var factor = Math.Min(inner.Width / _wolf.Width, inner.Height / _wolf.Height);
            var width = _wolf.Width * factor;
            var height = _wolf.Height * factor;
            g.DrawImage(_wolf, inner.X + (inner.Width - width) / 2f, inner.Y + (inner.Height - height) / 2f, width, height);
            g.Restore(state);
        }

        using var ring = new Pen(Rail, scale);
        using var highlight = new Pen(Accent, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(ring, bounds);
        g.DrawArc(highlight, bounds, 234f, 39f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Stop();
        base.Dispose(disposing);
        if (disposing)
        {
            _animation.Dispose();
            _wolf?.Dispose();
        }
    }
}
