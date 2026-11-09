using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace SiberiaConnect;

public sealed class MainForm : Form
{
    public const string Host = "sibirapp.duckdns.org";
    public static readonly string Home = $"https://{Host}/app/?native=1&os=windows";
    public static MainForm Instance { get; private set; }

    public VpnCore Core { get; }

    private readonly WebView2 _web;
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _stats = new() { Interval = 1000 };
    private readonly System.Threading.Timer _refresh;
    private readonly Icon _iconOn = AppInfo.LoadIcon(true);
    private readonly Icon _iconOff = AppInfo.LoadIcon(false);
    private ToolStripMenuItem _miToggle;
    private bool _allowShow;
    private bool _exiting;
    private bool _ready;
    private bool _webStarted;
    private bool _trayHint;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly Panel _splash;

    public MainForm(bool autostart, bool connectNow = false)
    {
        Instance = this;
        _allowShow = !autostart;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Siberia Connect";
        Icon = _iconOn;
        BackColor = Color.FromArgb(6, 15, 42);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(760, 560);
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(6, 15, 42) };
        Controls.Add(_web);
        // 1.0.2: фирменная заставка на доли секунды, пока поднимается интерфейс
        _splash = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(6, 15, 42) };
        var logo = new PictureBox { Size = new Size(120, 120), SizeMode = PictureBoxSizeMode.Zoom, Image = AppInfo.LoadBitmap(), BackColor = Color.Transparent };
        var title = new Label { Text = "Siberia Connect", AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold) };
        _splash.Controls.Add(logo);
        _splash.Controls.Add(title);
        _splash.Resize += (_, _) =>
        {
            logo.Location = new Point((_splash.ClientSize.Width - logo.Width) / 2, _splash.ClientSize.Height / 2 - logo.Height + 10);
            title.Location = new Point((_splash.ClientSize.Width - title.Width) / 2, logo.Bottom + 18);
        };
        Controls.Add(_splash);
        _splash.BringToFront();
        ResumeLayout(false);

        Core = new VpnCore();
        Core.Changed += ev => Ui(() => OnCoreEvent(ev));

        SetupTray();
        _stats.Tick += (_, _) => PushStats();
        _refresh = new System.Threading.Timer(_ =>
        {
            try
            {
                var before = Core.State.Servers.Count;
                Core.RefreshIfDue();
                if (Core.State.Servers.Count != before) Ui(PushSnapshot);
            }
            catch (Exception ex) { Log.Write("refresh: " + ex.Message); }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(20));

        SystemEvents.SessionEnding += (_, _) => Core.Shutdown();

        if (Core.HasSub && (connectNow || (autostart && Core.State.AutoStart)))
            Task.Run(Core.Connect);
        if (Elevation.IsAdmin && Core.State.AutoStart) AutoStart.Set(true, Core.State.TunMode);
    }

    // ───────────────────────── Окно ─────────────────────────

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, 4);          // тёмный заголовок
            int caption = 0x003D1A08;                              // #081A3D
            DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            int text = 0x00FFFFFF;
            DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }
        catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToScreen();
        StartWeb();
    }

    private void FitToScreen()
    {
        try
        {
            var wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width - 40), h = Math.Min(Height, wa.Height - 40);
            if (w != Width || h != Height)
            {
                Size = new Size(Math.Max(w, 640), Math.Max(h, 480));
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            }
        }
        catch { }
    }

    public void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        StartWeb();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exiting)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHint)
            {
                _trayHint = true;
                _tray.ShowBalloonTip(4000, "Siberia Connect",
                    Core.Running ? "VPN продолжает работать. Приложение в трее — значок со снежинкой у часов."
                                 : "Приложение свернулось в трей — значок со снежинкой у часов.",
                    ToolTipIcon.None);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Core.Shutdown();
        _tray.Visible = false;
        base.OnFormClosed(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        Core.Shutdown();
        _tray.Visible = false;
        Log.Write("Exit");
        Application.Exit();
    }

    private void Ui(Action a)
    {
        try
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a); else a();
        }
        catch { }
    }

    // ───────────────────────── Трей ─────────────────────────

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть Siberia Connect", null, (_, _) => ShowFromTray());
        _miToggle = new ToolStripMenuItem("Подключить VPN", null, (_, _) => Task.Run(() =>
        {
            if (Core.Running) Core.Disconnect(); else Core.Connect();
        }));
        menu.Items.Add(_miToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            _miToggle.Text = Core.Running ? "Отключить VPN" : "Подключить VPN";
            _miToggle.Enabled = Core.HasSub && !Core.Busy;
        };
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        UpdateTray();
        _tray.Visible = true;
    }

    private void UpdateTray()
    {
        _tray.Icon = Core.Running ? _iconOn : _iconOff;
        _tray.Text = Core.Running ? "Siberia Connect — защищено" : "Siberia Connect — VPN выключен";
    }

    // ───────────────────────── WebView2 ─────────────────────────

    private async void StartWeb()
    {
        if (_webStarted) return;
        _webStarted = true;
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebData);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            HideSplash();
            Log.Write("WebView2 runtime not found");
            if (MessageBox.Show(this,
                    "Для работы Siberia Connect нужен компонент Microsoft Edge WebView2.\n\nОткрыть страницу загрузки?",
                    "Siberia Connect", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            return;
        }
        catch (Exception ex)
        {
            HideSplash();
            Log.Write("WebView2 init: " + ex);
            MessageBox.Show(this, "Не удалось запустить интерфейс: " + ex.Message, "Siberia Connect",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var cw = _web.CoreWebView2;
        var s = cw.Settings;
        s.UserAgent = s.UserAgent + $" SiberiaConnectApp/{AppInfo.Version} SiberiaConnectWin/{AppInfo.Version}";
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--devtools");
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;

        await cw.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript());
        cw.WebMessageReceived += OnWebMessage;
        cw.NavigationStarting += OnNavigationStarting;
        cw.NavigationCompleted += OnNavigationCompleted;
        cw.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
        cw.ProcessFailed += (_, e) =>
        {
            Log.Write("WebView process failed: " + e.ProcessFailedKind);
            try { cw.Reload(); } catch { }
        };
        _ready = true;
        cw.Navigate(Home);
    }

    private string BridgeScript()
    {
        var init = Snapshot();
        init["version"] = AppInfo.Version;
        init["deviceId"] = Core.DeviceId;
        return AppInfo.ResourceText("Bridge.js").Replace("__INIT__", init.ToJsonString());
    }

    private static bool IsOurs(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase);

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri ?? "";
        if (IsOurs(uri) || uri.StartsWith("data:") || uri.StartsWith("about:")) return;
        e.Cancel = true;
        OpenExternal(uri);
    }

    private void HideSplash()
    {
        if (_splash != null && _splash.Visible) { _splash.Visible = false; _web.Focus(); }
    }

    private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        HideSplash();
        if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            Log.Write("page load failed: " + e.WebErrorStatus);
            try
            {
                _web.CoreWebView2.NavigateToString(AppInfo.ResourceText("Offline.html").Replace("__HOME__", Home));
            }
            catch { }
            return;
        }
        PushSnapshot();
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOurs(e.Source)) return;
        string raw;
        try { raw = e.TryGetWebMessageAsString(); } catch { return; }
        JsonNode msg;
        try { msg = JsonNode.Parse(raw); } catch { return; }
        var cmd = msg?["cmd"]?.GetValue<string>() ?? "";
        var args = msg?["args"] as JsonArray;
        string A(int i)
        {
            if (args == null || args.Count <= i || args[i] == null) return "";
            return args[i] is JsonValue v && v.TryGetValue<string>(out var str) ? str : args[i].ToJsonString();
        }

        switch (cmd)
        {
            case "setSub":
            {
                var url = A(0);
                Task.Run(() =>
                {
                    var r = Core.UpdateSub(url);
                    Ui(() => { PushSnapshot(); Emit(r); });
                });
                break;
            }
            case "connect":
                if (Core.State.TunMode && !Elevation.IsAdmin)
                {
                    if (Elevation.Relaunch("--connect")) { ExitApp(); return; }
                    Emit(new JsonObject { ["type"] = "state", ["state"] = "error",
                        ["error"] = "Для режима «Весь компьютер» нужно разрешение Windows. Нажми «Да» в окне или выключи режим в Профиле" });
                    break;
                }
                Task.Run(Core.Connect);
                break;
            case "tunMode":
            {
                var on = A(0) == "true";
                var wasRunning = Core.Running;
                Core.State.TunMode = on;
                Core.State.Save();
                if (on && !Elevation.IsAdmin)
                {
                    if (Elevation.Relaunch(wasRunning ? "--connect" : "")) { ExitApp(); return; }
                    Core.State.TunMode = false;
                    Core.State.Save();
                    Emit(new JsonObject { ["type"] = "toast", ["text"] = "Без разрешения Windows режим «Весь компьютер» не включить" });
                    PushSnapshot();
                    break;
                }
                if (Elevation.IsAdmin) AutoStart.Set(Core.State.AutoStart, on);
                PushSnapshot();
                Emit(new JsonObject { ["type"] = "toast", ["text"] = on ? "✅ Весь компьютер идёт через VPN" : "Режим «Весь компьютер» выключен" });
                if (wasRunning) Task.Run(Core.Reconnect);
                break;
            }
            case "disconnect":
                Task.Run(Core.Disconnect);
                break;
            case "select":
            {
                var g = A(0);
                Task.Run(() => { Core.Select(g); Ui(PushSnapshot); });
                break;
            }
            case "open":
                OpenExternal(A(0));
                break;
            case "autoStart":
            {
                var on = A(0) == "true";
                AutoStart.Set(on, Core.State.TunMode);
                Core.State.AutoStart = on;
                Core.State.Save();
                PushSnapshot();
                break;
            }
            case "advanced":
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Logs}\"") { UseShellExecute = true }); } catch { }
                Emit(new JsonObject { ["type"] = "toast", ["text"] = "Открыл папку с журналом — отправь файлы в поддержку, если попросят" });
                break;
            case "reload":
                try { _web.CoreWebView2.Navigate(Home); } catch { }
                break;
        }
    }

    private void OnCoreEvent(JsonObject ev)
    {
        UpdateTray();
        _stats.Enabled = Core.Running;
        PushSnapshot();
        Emit(ev);
        var st = ev["state"]?.GetValue<string>();
        if (st == "error" && !Visible)
            _tray.ShowBalloonTip(4000, "Siberia Connect", ev["error"]?.GetValue<string>() ?? "Ошибка подключения", ToolTipIcon.Warning);
    }

    private JsonObject Snapshot()
    {
        var servers = new JsonArray();
        foreach (var s in Core.State.Servers)
            servers.Add(new JsonObject { ["guid"] = s.Guid, ["name"] = s.Name, ["selected"] = s.Guid == Core.State.Selected });
        return new JsonObject
        {
            ["running"] = Core.Running,
            ["hasSub"] = Core.HasSub,
            ["subUrl"] = Core.State.SubUrl ?? "",
            ["servers"] = servers,
            ["autoStart"] = Core.State.AutoStart,
            ["tun"] = Core.State.TunMode,
            ["tunActive"] = Core.TunActive,
            ["admin"] = Elevation.IsAdmin,
            ["stats"] = StatsJson()
        };
    }

    private JsonObject StatsJson()
    {
        var (rx, tx) = VpnCore.Traffic();
        return new JsonObject
        {
            ["rx"] = rx, ["tx"] = tx, ["since"] = Core.Since,
            ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private void PushSnapshot() => Exec("window.__scSnap&&window.__scSnap(" + Snapshot().ToJsonString() + ")");

    private void PushStats()
    {
        if (!Visible) return;
        Exec("window.__scSnap&&window.__scSnap(" + new JsonObject { ["stats"] = StatsJson() }.ToJsonString() + ")");
    }

    private void Emit(JsonObject ev) => Exec("window.scNative&&window.scNative(" + ev.ToJsonString() + ")");

    private void Exec(string js)
    {
        if (!_ready || _web.CoreWebView2 == null) return;
        try { _ = _web.CoreWebView2.ExecuteScriptAsync(js); } catch { }
    }

    private static void OpenExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp && u.Scheme != "tg") return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open: " + ex.Message); }
    }
}
