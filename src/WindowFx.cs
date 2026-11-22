using System.Runtime.InteropServices;

namespace SiberiaConnect;

/// <summary>
/// 1.1.0: «стекло» Windows 11 (22H2+) — Mica или Acrylic под окном и заголовком.
/// На Windows 10 и старых сборках 11 — прежний тёмно-синий заголовок без эффекта.
/// Интерфейс (WebView2) получает прозрачный фон, страница — класс html.sc-glass (см. Bridge.js).
/// </summary>
internal static class WindowFx
{
    public const string Mica = "mica";
    public const string Acrylic = "acrylic";
    public const string Off = "off";

    public static readonly Color Brand = Color.FromArgb(6, 15, 42);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2, DWMSBT_TRANSIENTWINDOW = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE есть с Windows 11 22H2 (сборка 22621).</summary>
    public static bool Supported => Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621;

    public static string Normalize(string mode) => mode is Acrylic or Off ? mode : Mica;

    /// <summary>Включает эффект. true — стекло работает (фон окна нужно сделать прозрачным).</summary>
    public static bool Apply(IntPtr hwnd, string mode)
    {
        mode = Normalize(mode);
        var glass = Supported && mode != Off;
        try
        {
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);   // тёмный заголовок
            int text = 0x00FFFFFF;
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);

            if (glass)
            {
                int captionDefault = DWMWA_COLOR_DEFAULT;                             // свой цвет заголовка перекрыл бы стекло
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionDefault, 4);
                int type = mode == Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) != 0) glass = false;
                else
                {
                    var all = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };   // стекло на всё окно
                    DwmExtendFrameIntoClientArea(hwnd, ref all);
                }
            }

            if (!glass)
            {
                if (Supported)
                {
                    int none = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, 4);
                    var zero = new Margins();
                    DwmExtendFrameIntoClientArea(hwnd, ref zero);
                }
                int caption = 0x003D1A08;                                             // #081A3D, как в 1.0.x
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
            }
        }
        catch (Exception ex)
        {
            Log.Write("backdrop: " + ex.Message);
            glass = false;
        }
        return glass;
    }
}
