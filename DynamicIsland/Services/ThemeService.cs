using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Services;

/// <summary>Описание темы: три цвета (основной/вторичный/акцент) в HSL, как в исходной палитре.
/// Light=true — светлая тема на белом фоне.</summary>
public record ThemeInfo(string Key, string Name, string Hint,
    (double H, double S, double L)? Primary,
    (double H, double S, double L)? Secondary,
    (double H, double S, double L)? Accent,
    bool Light = false);

/// <summary>
/// Темы, адаптированные из репозитория MeetClouding (src/app/globals.css):
/// Утренняя / Дневная / Вечерняя / Ночная + Авто (по времени суток) и Системная,
/// которая подхватывает акцентный цвет Windows (панель задач, переключатели, бордеры окон).
/// Тема задаёт цвет компаньона — из него уже строятся акцентные кисти всего приложения.
/// </summary>
public static class ThemeService
{
    public static readonly ThemeInfo[] Themes =
    {
        new("classic", "Классика",  "Фирменный фиолетовый островка",
            (256, 100, 65), (300, 81, 59), (204, 100, 50)),
        new("system",  "Системная", "Акцентный цвет Windows",
            null, null, null),
        new("morning", "Утренняя",  "Светлая: фиолетовый, маджента и лазурь",
            (271, 91, 65), (300, 81, 59), (204, 100, 50), Light: true),
        new("day",     "Дневная",   "Светлая: тёплые персиковые тона",
            (350, 90, 70), (25, 95, 75), (20, 100, 70), Light: true),
        new("evening", "Вечерняя",  "Оранжевый закат",
            (25, 90, 60), (350, 70, 55), (35, 95, 65)),
        new("night",   "Ночная",    "Кислотные зелёный и жёлтый",
            (80, 90, 50), (210, 90, 60), (54, 96, 54)),
        new("auto",    "Авто",      "Меняется по времени суток",
            null, null, null),
    };

    private static DispatcherTimer? _autoTimer;
    private static Windows.UI.ViewManagement.UISettings? _uiSettings;
    private static bool _applying;

    /// <summary>Светлая ли сейчас тема (для фона островка и палитры приложения).</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Базовый цвет фона окна: глубокий чёрный или воздушный светлый.</summary>
    public static Color BaseColor => IsLight
        ? Color.FromRgb(0xF5, 0xF6, 0xF9)
        : Color.FromRgb(0x00, 0x00, 0x00);

    public static void Init()
    {
        SettingsService.Changed += Apply;

        // Авто-тема: проверка каждые 5 минут (как в MeetClouding)
        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _autoTimer.Tick += (_, _) => Apply();
        _autoTimer.Start();

        // Системная тема: отслеживаем смену акцента Windows
        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += (_, _) =>
                Application.Current?.Dispatcher.Invoke(Apply);
        }
        catch { /* WinRT недоступен — останется реестр */ }

        Apply();
    }

    /// <summary>Применяет текущую тему: пересчитывает цвет и пишет его в EyeColor,
    /// откуда уже обновляются акцентные кисти, глаза и тень.</summary>
    public static void Apply()
    {
        if (_applying) return;
        var s = SettingsService.Current;
        if (s.Theme == "custom")
        {
            ApplyLight(false); // «Своя» — всегда глубокий чёрный
            return;
        }

        bool light = s.Theme switch
        {
            "system" => false,
            "auto" => ByHourInfo()?.Light ?? false,
            _ => Themes.FirstOrDefault(t => t.Key == s.Theme)?.Light ?? false,
        };
        ApplyLight(light);

        Color? color = s.Theme switch
        {
            "system" => GetWindowsAccent(),
            "auto" => ByHour(),
            _ => ResolvePreset(s.Theme),
        };
        if (color is not { } c) return;

        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (!string.Equals(s.EyeColor, hex, StringComparison.OrdinalIgnoreCase))
        {
            _applying = true;
            s.EyeColor = hex;
            SettingsService.NotifyChanged();
            _applying = false;
        }
    }

    private static void ApplyLight(bool light)
    {
        if (IsLight == light) return;
        IsLight = light;
        Application.Current?.Dispatcher.Invoke(() => App.ApplyPalette(light));
    }

    private static Color? ResolvePreset(string key)
    {
        var t = Themes.FirstOrDefault(t => t.Key == key);
        return t?.Primary is { } p ? Hsl(p) : null;
    }

    /// <summary>Авто-тема: 5–9 Утренняя, 9–17 Дневная, 17–21 Вечерняя, 21–5 Ночная.</summary>
    private static ThemeInfo? ByHourInfo()
    {
        int h = DateTime.Now.Hour;
        string key = h is >= 5 and < 9 ? "morning"
                   : h is >= 9 and < 17 ? "day"
                   : h is >= 17 and < 21 ? "evening"
                   : "night";
        return Themes.FirstOrDefault(t => t.Key == key);
    }

    private static Color ByHour() =>
        ByHourInfo()?.Primary is { } p ? Hsl(p) : GetWindowsAccent();

    /// <summary>Акцентный цвет Windows (тот, что красит панель задач, переключатели и рамки).</summary>
    public static Color GetWindowsAccent()
    {
        try
        {
            _uiSettings ??= new Windows.UI.ViewManagement.UISettings();
            var c = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { }
        try
        {
            // Запасной путь: реестр DWM (значение ABGR)
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                    "AccentColor", null) is int abgr)
            {
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
            }
        }
        catch { }
        return (Color)ColorConverter.ConvertFromString("#7C4DFF");
    }

    /// <summary>HSL (H в градусах, S и L в процентах) → Color.</summary>
    public static Color Hsl((double H, double S, double L) hsl) => Hsl(hsl.H, hsl.S, hsl.L);

    public static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s /= 100; l /= 100;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
