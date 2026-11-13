using System.Diagnostics;
using System.Security.Cryptography;
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
    private bool _glass;   // 1.1.0: Mica/Acrylic включён

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
        BackColor = WindowFx.Brand;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(760, 560);
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = WindowFx.Brand };
        Controls.Add(_web);
        // 1.0.2: фирменная заставка на доли секунды, пока поднимается интерфейс
        _splash = new Panel { Dock = DockStyle.Fill, BackColor = WindowFx.Brand };
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
        Core.TokenChanged += t => Ui(() => PushToken(t));

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
        // после обновления автозапуск мог быть снят старой версией — возвращаем
        if (Core.State.AutoStart && (Elevation.IsAdmin || !Core.State.TunMode)) AutoStart.Set(true, Core.State.TunMode);

        // link_v1: нажали «Открыть Siberia Connect» на странице входа, а приложение уже запущено
        try
        {
            Directory.CreateDirectory(Paths.Data);
            _dlWatch = new FileSystemWatcher(Paths.Data, Path.GetFileName(Paths.PendingLogin))
                { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = (_, _) => { try { BeginInvoke(new Action(OpenPendingLogin)); } catch { } };
            _dlWatch.Created += h;
            _dlWatch.Changed += h;
            _dlWatch.Renamed += (_, _) => { try { BeginInvoke(new Action(OpenPendingLogin)); } catch { } };
            _dlWatch.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Log.Write("deeplink watch: " + ex.Message); }
    }

    private FileSystemWatcher _dlWatch;

    private void OpenPendingLogin()
    {
        var c = DeepLink.Take();
        if (c == null) return;
        ShowFromTray();
        try { _web.CoreWebView2?.Navigate(Home + "&dl=" + c); } catch { }
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
        ApplyBackdrop();
    }

    // ───────────────────────── 1.1.0: стекло Windows 11 ─────────────────────────

    private void ApplyBackdrop()
    {
        if (!IsHandleCreated) return;
        _glass = WindowFx.Apply(Handle, Core?.State.Backdrop);
        // чёрный фон формы под расширенной рамкой DWM = прозрачный → видно Mica/Acrylic
        BackColor = _glass ? Color.Black : WindowFx.Brand;
        try { _web.DefaultBackgroundColor = _glass ? Color.Transparent : WindowFx.Brand; }
        catch (Exception ex) { Log.Write("web background: " + ex.Message); }
        Invalidate(true);
    }

    // ───────────────────────── 1.3.0: вход и связка с Telegram ─────────────────────────

    private void PushToken(string token)
    {
        PushSnapshot();
        if (string.IsNullOrEmpty(token)) return;
        var js = System.Text.Json.JsonSerializer.Serialize(token);
        Exec($"try{{if(localStorage.getItem('sc_tok')!=={js}){{localStorage.setItem('sc_tok',{js});location.reload();}}}}catch(e){{}}");
    }

    private void LinkTelegram()
    {
        Task.Run(() =>
        {
            var (code, url, err) = Core.LinkCode();
            Ui(() =>
            {
                if (err != null) { Notify(err, true); return; }
                if (!string.IsNullOrEmpty(url)) OpenExternal(url);
                _tray.ShowBalloonTip(8000, "Связать с Telegram",
                    $"Открыл бота. Если не открылся — отправь ему: /link {code} (код действует 10 минут)", ToolTipIcon.None);
            });
        });
    }

    /// <summary>1.2.0: включить «Авто» или вернуться к ручному выбору (остаёмся на текущем сервере).</summary>
    private void SetAutoMode(bool on)
    {
        if (on) { Core.Select(VpnCore.AutoGuid); return; }
        var keep = Core.Running && !string.IsNullOrEmpty(Core.ActiveGuid) ? Core.ActiveGuid : Core.State.Selected;
        if (!Core.State.Servers.Any(x => x.Guid == keep)) keep = Core.State.Servers.FirstOrDefault()?.Guid;
        if (keep != null) Core.Select(keep);
    }

    private void SetBackdrop(string mode)
    {
        Core.State.Backdrop = WindowFx.Normalize(mode);
        Core.State.Save();
        ApplyBackdrop();
        PushSnapshot();
        Log.Write("backdrop: " + Core.State.Backdrop + (_glass ? " (включён)" : " (не поддерживается — обычное окно)"));
    }

    // ───────────────────────── 1.1.0: зашифрованная копия настроек ─────────────────────────

    private void Notify(string text, bool error = false)
    {
        if (_ready && Visible) Emit(new JsonObject { ["type"] = "toast", ["text"] = text });
        else _tray.ShowBalloonTip(4000, "Siberia Connect", text, error ? ToolTipIcon.Warning : ToolTipIcon.None);
    }

    private void BackupSave()
    {
        ShowFromTray();
        if (!Core.HasSub)
        {
            MessageBox.Show(this, "Сохранять пока нечего — сначала войди в Siberia Connect.", "Резервная копия",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string path;
        using (var dlg = new SaveFileDialog
        {
            Title = "Сохранить копию настроек Siberia Connect",
            Filter = Backup.Filter,
            DefaultExt = Backup.Ext,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"SiberiaConnect-{DateTime.Now:yyyy-MM-dd}.{Backup.Ext}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        string pass;
        using (var pw = new PasswordDialog("Пароль для копии",
                   "Придумай пароль. Копия зашифрована: без пароля её не открыть. Мы пароль не храним и не сможем подсказать.", true))
        {
            if (pw.ShowDialog(this) != DialogResult.OK) return;
            pass = pw.Password;
        }
        var json = Backup.ToJson(Core.State);
        Task.Run(() =>
        {
            try
            {
                var bytes = Backup.Seal(json, pass);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, true);
                Log.Write("backup: копия сохранена");
                Ui(() => Notify("✅ Копия сохранена: " + Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                Log.Write("backup save: " + ex.Message);
                Ui(() => Notify("Не удалось сохранить копию: " + ex.Message, true));
            }
        });
    }

    private void BackupRestore()
    {
        ShowFromTray();
        byte[] data;
        using (var dlg = new OpenFileDialog
        {
            Title = "Восстановить настройки Siberia Connect",
            Filter = Backup.Filter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (new FileInfo(dlg.FileName).Length > 4_000_000) throw new InvalidDataException("Это не резервная копия Siberia Connect");
                data = File.ReadAllBytes(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Резервная копия", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        string pass;
        using (var pw = new PasswordDialog("Восстановление из копии", "Введи пароль, который задавал при сохранении копии.", false))
        {
            if (pw.ShowDialog(this) != DialogResult.OK) return;
            pass = pw.Password;
        }
        Task.Run(() =>
        {
            Backup.Data d;
            try { d = Backup.FromJson(Backup.Open(data, pass)); }
            catch (CryptographicException) { Ui(() => Notify("Неверный пароль или файл копии повреждён", true)); return; }
            catch (Exception ex) { Ui(() => Notify(ex.Message, true)); return; }
            var r = Core.RestoreBackup(d);
            var fresh = r["ok"]?.GetValue<bool>() == true;
            Ui(() =>
            {
                AutoStart.Set(Core.State.AutoStart, Core.State.TunMode);
                ApplyBackdrop();
                PushSnapshot();
                Notify(fresh ? "✅ Настройки восстановлены, список серверов обновлён"
                             : "✅ Настройки восстановлены. Список серверов обновится, когда появится связь");
            });
        });
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
        // 1.3.0: связка с Telegram (только для аккаунтов без Telegram)
        var miLink = new ToolStripMenuItem("🔗 Связать с Telegram…", null, (_, _) => LinkTelegram()) { Visible = false };
        menu.Items.Add(miLink);
        menu.Opening += (_, _) => miLink.Visible = Core.Account?["app_only"]?.GetValue<bool>() == true;
        // 1.2.0: автовыбор сервера
        var miAuto = new ToolStripMenuItem("⚡️ Северное Сияние — автовыбор сервера") { CheckOnClick = false };
        miAuto.Click += (_, _) =>
        {
            var on = !Core.State.AutoSelect;
            Task.Run(() => { SetAutoMode(on); Ui(PushSnapshot); });
        };
        menu.Items.Add(miAuto);
        menu.Opening += (_, _) =>
        {
            miAuto.Checked = Core.State.AutoSelect;
            miAuto.Enabled = Core.State.Servers.Count > 1 && !Core.Busy;
        };
        menu.Items.Add(new ToolStripSeparator());

        // 1.1.0: оформление окна (только Windows 11 22H2+)
        var fx = new ToolStripMenuItem("Оформление окна") { Visible = WindowFx.Supported };
        foreach (var (id, title) in new[]
                 {
                     (WindowFx.Mica, "Mica — матовое стекло"),
                     (WindowFx.Acrylic, "Acrylic — прозрачное стекло"),
                     (WindowFx.Off, "Без эффекта")
                 })
        {
            var item = new ToolStripMenuItem(title) { Tag = id };
            item.Click += (_, _) => SetBackdrop(id);
            fx.DropDownItems.Add(item);
        }
        fx.DropDownOpening += (_, _) =>
        {
            var cur = WindowFx.Normalize(Core.State.Backdrop);
            foreach (ToolStripItem i in fx.DropDownItems)
                if (i is ToolStripMenuItem m) m.Checked = (string)m.Tag == cur;
        };
        menu.Items.Add(fx);

        // 1.1.0: зашифрованная резервная копия настроек
        var bk = new ToolStripMenuItem("Резервная копия");
        bk.DropDownItems.Add("Сохранить копию настроек…", null, (_, _) => BackupSave());
        bk.DropDownItems.Add("Восстановить из копии…", null, (_, _) => BackupRestore());
        menu.Items.Add(bk);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            _miToggle.Text = Core.Running ? "Отключить VPN" : "Подключить VPN";
            _miToggle.Enabled = (Core.HasSub || !string.IsNullOrEmpty(Core.State.AuthToken)) && !Core.Busy;
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

        // 1.3.0: первый запуск без ключа — аккаунт создаётся сам, кабинет откроется уже с входом
        if (Core.NeedsAutoRegister)
        {
            try { await Task.Run(Core.AutoRegister); } catch (Exception ex) { Log.Write("auth: " + ex.Message); }
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
        var dl = DeepLink.Take();
        cw.Navigate(dl != null ? Home + "&dl=" + dl : Home);
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
                try
                {
                    // 1.3.2: журнал одним архивом на рабочем столе — его достаточно отправить в поддержку (ключей в нём нет)
                    var zip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SiberiaConnect-logs.zip");
                    if (File.Exists(zip)) File.Delete(zip);
                    using (var za = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                        foreach (var f in Directory.GetFiles(Paths.Logs))
                        {
                            using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var dst = za.CreateEntry(Path.GetFileName(f)).Open();
                            src.CopyTo(dst);
                        }
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });
                    Emit(new JsonObject { ["type"] = "toast", ["text"] = "Журнал сохранён на рабочий стол: SiberiaConnect-logs.zip — отправь его в поддержку" });
                }
                catch { try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Logs}\"") { UseShellExecute = true }); } catch { } }
                break;
            case "installUpdate":
            {
                var reconnect = Core.Running;
                Task.Run(async () =>
                {
                    var err = await Updater.Install(reconnect,
                        p => Ui(() => Emit(new JsonObject { ["type"] = "update", ["stage"] = "download", ["pct"] = p })));
                    Ui(() =>
                    {
                        if (err != null) { Emit(new JsonObject { ["type"] = "update", ["stage"] = "error", ["text"] = err }); return; }
                        Emit(new JsonObject { ["type"] = "update", ["stage"] = "install" });
                        ExitApp();   // установщик заменит файлы и сам запустит новую версию
                    });
                });
                break;
            }
            case "authToken":       // 1.3.0: кабинет вошёл/вышел
                Core.AdoptToken(A(0));
                break;
            case "linkTelegram":    // 1.3.0
                BeginInvoke(new Action(LinkTelegram));
                break;
            case "autoMode":        // 1.2.0
            {
                var on = A(0) == "true";
                Task.Run(() => { SetAutoMode(on); Ui(PushSnapshot); });
                break;
            }
            case "backdrop":        // 1.1.0
                SetBackdrop(A(0));
                break;
            case "backupSave":      // 1.1.0
                BeginInvoke(new Action(BackupSave));
                break;
            case "backupRestore":   // 1.1.0
                BeginInvoke(new Action(BackupRestore));
                break;
            case "reload":
                try { _web.CoreWebView2.Navigate(Home); } catch { }
                break;
        }
    }

    private void OnCoreEvent(JsonObject ev)
    {
        if (ev["type"]?.GetValue<string>() == "account")   // 1.3.0: подписка кончилась — сразу к тарифам
        {
            if (ev["state"]?.GetValue<string>() == "expired" && Visible) Exec("try{go('plans')}catch(e){}");
            Emit(ev);
            return;
        }
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
        var auto = Core.State.AutoSelect && Core.State.Servers.Count > 1;
        // 1.2.0: «Северное Сияние (Авто)» — первым пунктом списка; кабинет показывает его как обычный сервер
        if (Core.State.Servers.Count > 1)
        {
            var active = Core.Running ? Core.State.Servers.FirstOrDefault(x => x.Guid == Core.ActiveGuid) : null;
            servers.Add(new JsonObject
            {
                ["guid"] = VpnCore.AutoGuid,
                ["name"] = auto && active != null ? $"⚡️ Северное Сияние → {active.Name}" : VpnCore.AutoName,
                ["selected"] = auto,
                ["auto"] = true
            });
        }
        foreach (var s in Core.State.Servers)
            servers.Add(new JsonObject
            {
                ["guid"] = s.Guid, ["name"] = s.Name,
                ["selected"] = !auto && s.Guid == Core.State.Selected,
                ["active"] = Core.Running && s.Guid == Core.ActiveGuid,
                ["ping"] = Core.PingOf(s.Guid)
            });
        return new JsonObject
        {
            ["running"] = Core.Running,
            ["hasSub"] = Core.HasSub,
            ["subUrl"] = Core.State.SubUrl ?? "",
            ["servers"] = servers,
            ["autoStart"] = Core.State.AutoStart,
            ["tun"] = Core.State.TunMode,
            ["tunActive"] = Core.TunActive,
            ["auto"] = Core.State.AutoSelect,
            ["appToken"] = Core.State.AuthToken ?? "",
            ["account"] = Core.Account?["state"]?.GetValue<string>() ?? "",
            ["activeGuid"] = Core.Running ? Core.ActiveGuid : "",
            ["admin"] = Elevation.IsAdmin,
            ["glass"] = _glass,
            ["backdrop"] = WindowFx.Normalize(Core.State.Backdrop),
            ["backdropSupported"] = WindowFx.Supported,
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
