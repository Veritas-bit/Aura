using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>
/// Иконка приложения в системном трее через Shell_NotifyIcon.
/// Меню: тема (подменю), статус-бар (вкл/выкл), выход.
/// Иконка — минималистичный знак AURA, рисуется WPF и кешируется в %APPDATA%\AURA.
/// </summary>
public sealed class TrayService : IDisposable
{
    public const int WM_TRAY = 0x8001; // WM_APP + 1

    private readonly IntPtr _hwnd;
    private bool _added;
    private IntPtr _icon = IntPtr.Zero;
    private string? _iconColor;

    /// <summary>Команды меню: "toggle", "stats", "theme:&lt;key&gt;", "exit".</summary>
    public event Action<string>? Command;

    public TrayService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        UpdateIcon(Parse(SettingsService.Current.EyeColor));
    }

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString("#7C4DFF"); }
    }

    // ---------- Иконка ----------

    /// <summary>Перерисовать иконку под цвет темы (кешируется по цвету).</summary>
    public void UpdateIcon(Color accent)
    {
        string key = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        if (_iconColor == key && _added) return;
        _iconColor = key;

        var newIcon = CreateIcon(accent);
        if (newIcon == IntPtr.Zero) return;

        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = newIcon;

        if (_added) Notify(NIM_MODIFY);
        else { Notify(NIM_ADD); _added = true; }
    }

    private void Notify(uint message)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
            szTip = "AURA"
        };
        Shell_NotifyIcon(message, ref data);
    }

    /// <summary>Рисует монограмму AURA, сохраняет .ico и загружает HICON.</summary>
    private static IntPtr CreateIcon(Color accent)
    {
        try
        {
            const int size = 64;
            var root = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x21)),
                Child = new Grid()
            };
            var content = (Grid)root.Child;
            content.Children.Add(new TextBlock
            {
                Text = "A",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 42,
                FontWeight = FontWeights.Light,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -3, 0, 0)
            });
            content.Children.Add(new Border
            {
                Width = 18,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Measure(new Size(size, size));
            root.Arrange(new Rect(0, 0, size, size));
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var png = new MemoryStream();
            encoder.Save(png);
            var pngBytes = png.ToArray();

            // ICO-контейнер с PNG внутри (Vista+)
            using var ico = new MemoryStream();
            using (var bw = new BinaryWriter(ico))
            {
                bw.Write((short)0);       // reserved
                bw.Write((short)1);       // type: icon
                bw.Write((short)1);       // count
                bw.Write((byte)size);     // width
                bw.Write((byte)size);     // height
                bw.Write((byte)0);        // colors
                bw.Write((byte)0);        // reserved
                bw.Write((short)1);       // planes
                bw.Write((short)32);      // bpp
                bw.Write(pngBytes.Length);
                bw.Write(6 + 16);         // offset
                bw.Write(pngBytes);
                bw.Flush();
            }

            var path = Path.Combine(SettingsService.DataDir, $"tray-{accent.R:X2}{accent.G:X2}{accent.B:X2}.ico");
            Directory.CreateDirectory(SettingsService.DataDir);
            File.WriteAllBytes(path, ico.ToArray());

            return LoadImage(IntPtr.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }
        catch { return IntPtr.Zero; }
    }

    // ---------- Сообщения и меню ----------

    public void HandleMessage(IntPtr lParam)
    {
        switch (lParam.ToInt32())
        {
            case WM_LBUTTONUP:
                Command?.Invoke("toggle");
                break;
            case WM_RBUTTONUP:
                ShowMenu();
                break;
        }
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            var themes = CreatePopupMenu();
            string current = SettingsService.Current.Theme;
            for (int i = 0; i < ThemeService.Themes.Length; i++)
            {
                var t = ThemeService.Themes[i];
                uint flags = MF_STRING | (t.Key == current ? MF_CHECKED : 0);
                AppendMenu(themes, flags, (UIntPtr)(IDM_THEME_BASE + i), t.Name);
            }
            AppendMenu(menu, MF_POPUP, (UIntPtr)themes.ToInt64(), "Тема");

            uint statsFlags = MF_STRING | (SettingsService.Current.ShowStats ? MF_CHECKED : 0);
            AppendMenu(menu, statsFlags, (UIntPtr)IDM_STATS, "Статус-бар");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)IDM_EXIT, "Выход");

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // чтобы меню закрывалось по клику мимо
            int cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);

            if (cmd >= IDM_THEME_BASE && cmd < IDM_THEME_BASE + ThemeService.Themes.Length)
                Command?.Invoke("theme:" + ThemeService.Themes[cmd - IDM_THEME_BASE].Key);
            else if (cmd == IDM_STATS) Command?.Invoke("stats");
            else if (cmd == IDM_EXIT) Command?.Invoke("exit");
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_added) { Notify(NIM_DELETE); _added = false; }
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    // ---------- P/Invoke ----------

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint MF_STRING = 0x0;
    private const uint MF_CHECKED = 0x8;
    private const uint MF_POPUP = 0x10;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint TPM_NONOTIFY = 0x80;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const int IDM_THEME_BASE = 100;
    private const int IDM_STATS = 200;
    private const int IDM_EXIT = 300;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);
}
