using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>
/// 1.1.0: зашифрованная резервная копия настроек (*.scbackup) — для переустановки Windows или другого ПК.
/// Формат: "SCBK" | версия (1 байт) | число итераций PBKDF2 (int32 LE) | соль 16 | nonce 12 | тег 16 | шифртекст.
/// Ключ: PBKDF2-SHA256 из пароля пользователя, шифр AES-256-GCM, заголовок защищён как associated data.
/// Пароль нигде не хранится — без него копию не открыть.
/// </summary>
internal static class Backup
{
    public const string Ext = "scbackup";
    public const string Filter = "Резервная копия Siberia Connect (*.scbackup)|*.scbackup";
    public const int MinPassword = 6;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SCBK");
    private const byte Ver = 1;
    private const int Iter = 310_000;
    private const int HeaderLen = 9;
    private const int SaltLen = 16, NonceLen = 12, TagLen = 16;

    public sealed class Data
    {
        public string SubUrl = "", Selected = "", Backdrop = WindowFx.Mica;
        public List<ServerItem> Servers = new();
        public bool AutoStart, TunMode, AutoSelect = true;
    }

    public static byte[] Seal(string json, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var header = new byte[HeaderLen];
        Magic.CopyTo(header, 0);
        header[4] = Ver;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5, 4), Iter);

        var plain = Encoding.UTF8.GetBytes(json);
        var ct = new byte[plain.Length];
        var tag = new byte[TagLen];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iter, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagLen);
            aes.Encrypt(nonce, plain, ct, tag, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var outBuf = new byte[HeaderLen + SaltLen + NonceLen + TagLen + ct.Length];
        header.CopyTo(outBuf, 0);
        salt.CopyTo(outBuf, HeaderLen);
        nonce.CopyTo(outBuf, HeaderLen + SaltLen);
        tag.CopyTo(outBuf, HeaderLen + SaltLen + NonceLen);
        ct.CopyTo(outBuf, HeaderLen + SaltLen + NonceLen + TagLen);
        return outBuf;
    }

    /// <summary>Расшифровка. Неверный пароль или испорченный файл → CryptographicException.</summary>
    public static string Open(byte[] data, string password)
    {
        const int min = HeaderLen + SaltLen + NonceLen + TagLen;
        if (data == null || data.Length < min || !((ReadOnlySpan<byte>)data.AsSpan(0, 4)).SequenceEqual((ReadOnlySpan<byte>)Magic))
            throw new InvalidDataException("Это не резервная копия Siberia Connect");
        if (data[4] != Ver)
            throw new InvalidDataException("Копия сделана более новой версией — обнови приложение");
        var iter = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(5, 4));
        if (iter < 100_000 || iter > 5_000_000)
            throw new InvalidDataException("Файл копии повреждён");

        var header = data.AsSpan(0, HeaderLen).ToArray();
        var salt = data.AsSpan(HeaderLen, SaltLen).ToArray();
        var nonce = data.AsSpan(HeaderLen + SaltLen, NonceLen).ToArray();
        var tag = data.AsSpan(HeaderLen + SaltLen + NonceLen, TagLen).ToArray();
        var ct = data.AsSpan(min).ToArray();
        var plain = new byte[ct.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagLen);
            aes.Decrypt(nonce, ct, tag, plain, header);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static string ToJson(AppState st)
    {
        var servers = new JsonArray();
        foreach (var s in st.Servers)
            servers.Add(new JsonObject { ["guid"] = s.Guid, ["name"] = s.Name, ["uri"] = s.Uri });
        return new JsonObject
        {
            ["app"] = "SiberiaConnect-Windows",
            ["v"] = 1,
            ["appVersion"] = AppInfo.Version,
            ["created"] = DateTime.UtcNow.ToString("o"),
            ["subUrl"] = st.SubUrl ?? "",
            ["selected"] = st.Selected ?? "",
            ["servers"] = servers,
            ["autoStart"] = st.AutoStart,
            ["tunMode"] = st.TunMode,
            ["autoSelect"] = st.AutoSelect,
            ["backdrop"] = WindowFx.Normalize(st.Backdrop)
        }.ToJsonString();
    }

    public static Data FromJson(string json)
    {
        var n = JsonNode.Parse(json) as JsonObject;
        string S(JsonNode x, string k) => x?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        bool B(string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        if (n == null || S(n, "app") != "SiberiaConnect-Windows")
            throw new InvalidDataException("Это не резервная копия Siberia Connect");

        var d = new Data
        {
            SubUrl = S(n, "subUrl").Trim(),
            Selected = S(n, "selected"),
            AutoStart = B("autoStart"),
            TunMode = B("tunMode"),
            AutoSelect = n?["autoSelect"] == null || B("autoSelect"),
            Backdrop = WindowFx.Normalize(S(n, "backdrop"))
        };
        // замок как у подписки: в приложение попадают только ключи Siberia Connect
        if (!VpnCore.IsAllowedSubUrl(d.SubUrl))
            throw new InvalidDataException("В копии нет ключа Siberia Connect");
        if (n["servers"] is JsonArray arr)
        {
            foreach (var x in arr)
            {
                var uri = S(x, "uri");
                if (!uri.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) continue;
                d.Servers.Add(new ServerItem { Guid = S(x, "guid"), Name = S(x, "name"), Uri = uri });
            }
        }
        return d;
    }
}

/// <summary>Окно ввода пароля для резервной копии — в фирменных цветах.</summary>
internal sealed class PasswordDialog : Form
{
    private readonly TextBox _p1;
    private readonly TextBox _p2;
    private readonly bool _confirm;

    public string Password => _p1.Text;

    public PasswordDialog(string title, string hint, bool confirm)
    {
        _confirm = confirm;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        Icon = AppInfo.LoadIcon(true);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = WindowFx.Brand;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(440, confirm ? 262 : 196);

        Controls.Add(new Label
        {
            Text = hint, Location = new Point(20, 14), Size = new Size(400, 58),
            ForeColor = Color.FromArgb(159, 184, 214)
        });
        Controls.Add(new Label { Text = "Пароль", Location = new Point(20, 76), AutoSize = true });
        _p1 = Box(98);
        Controls.Add(_p1);
        var y = 146;
        if (confirm)
        {
            Controls.Add(new Label { Text = "Пароль ещё раз", Location = new Point(20, 140), AutoSize = true });
            _p2 = Box(162);
            Controls.Add(_p2);
            y = 212;
        }

        var ok = Btn("Готово", new Point(224, y), true);
        var cancel = Btn("Отмена", new Point(330, y), false);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) =>
        {
            if (_p1.Text.Length == 0) { Warn("Введи пароль"); return; }
            if (_confirm && _p1.Text.Length < Backup.MinPassword) { Warn($"Пароль — минимум {Backup.MinPassword} символов"); return; }
            if (_confirm && _p1.Text != _p2.Text) { Warn("Пароли не совпали"); return; }
            DialogResult = DialogResult.OK;
        };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowFx.Apply(Handle, WindowFx.Off);   // тёмный заголовок
    }

    private void Warn(string text) =>
        MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static TextBox Box(int y) => new()
    {
        Location = new Point(20, y), Size = new Size(400, 28), UseSystemPasswordChar = true,
        BackColor = Color.FromArgb(16, 36, 78), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
    };

    private static Button Btn(string text, Point at, bool primary)
    {
        var b = new Button
        {
            Text = text, Location = at, Size = new Size(96, 34), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(61, 224, 194) : Color.FromArgb(16, 36, 78),
            ForeColor = primary ? Color.FromArgb(6, 33, 56) : Color.White,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
