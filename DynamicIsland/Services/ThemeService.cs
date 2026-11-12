using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Services;

/// <summary>Описание темы: три акцентных цвета в HSL и режим чёрного/белого фона.</summary>
public record ThemeInfo(string Key, string Name, string Hint,
    (double H, double S, double L)? Primary,
    (double H, double S, double L)? Secondary,
    (double H, double S, double L)? Accent,
    bool Light = false);

/// <summary>
/// Темы оформления с тремя контрастными акцентами и однотонным чёрным/белым фоном.
/// </summary>
public static class ThemeService
{
    public static readonly ThemeInfo[] Themes =
    {
        new("classic", "Классика",  "Фирменный фиолетовый акцент",
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

    /// <summary>Светлая ли сейчас тема.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Базовый цвет фона островка.</summary>
    public static Color BaseColor => IsLight
        ? Colors.White
        : Colors.Black;

    public static (Color Primary, Color Secondary, Color Tertiary)? GetThemePalette(string key)
    {
        bool light = key switch
        {
            "auto" => ByHourInfo()?.Light ?? false,
            _ => Themes.FirstOrDefault(theme => theme.Key == key)?.Light ?? false
        };
        return ResolvePreset(key, light);
    }

    public static void Init()
    {
        SettingsService.Changed += Apply;

        // Авто-тема обновляется по времени суток.
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

    /// <summary>Применяет выбранную тему и обновляет её акцентный цвет.</summary>
    public static void Apply()
    {
        if (_applying) return;
        var s = SettingsService.Current;
        if (s.Theme == "custom")
        {
            ApplyLight(s.LightBackground);
            var customPrimary = EnsureAccentContrast(ParseColor(s.EyeColor, "#A78BFA"), s.LightBackground);
            var customSecondary = EnsureAccentContrast(ParseColor(s.SecondaryAccentColor, "#D946EF"), s.LightBackground);
            var customTertiary = EnsureAccentContrast(ParseColor(s.TertiaryAccentColor, "#22D3EE"), s.LightBackground);
            var customPrimaryHex = ToHex(customPrimary);
            var customSecondaryHex = ToHex(customSecondary);
            var customTertiaryHex = ToHex(customTertiary);
            if (!string.Equals(s.EyeColor, customPrimaryHex, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(s.SecondaryAccentColor, customSecondaryHex, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(s.TertiaryAccentColor, customTertiaryHex, StringComparison.OrdinalIgnoreCase))
            {
                _applying = true;
                try
                {
                    s.EyeColor = customPrimaryHex;
                    s.SecondaryAccentColor = customSecondaryHex;
                    s.TertiaryAccentColor = customTertiaryHex;
                    SettingsService.NotifyChanged();
                }
                finally { _applying = false; }
            }
            ApplyAccents(customPrimary, customSecondary, customTertiary);
            return;
        }

        bool light = s.Theme switch
        {
            "system" => false,
            "auto" => ByHourInfo()?.Light ?? false,
            _ => Themes.FirstOrDefault(t => t.Key == s.Theme)?.Light ?? false,
        };
        var colors = ResolvePreset(s.Theme, light);
        if (colors is null) return;
        var (primary, secondary, tertiary) = colors.Value;
        string primaryHex = ToHex(primary);
        string secondaryHex = ToHex(secondary);
        string tertiaryHex = ToHex(tertiary);
        ApplyLight(light);
        bool changed = s.LightBackground != light ||
                       !string.Equals(s.EyeColor, primaryHex, StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(s.SecondaryAccentColor, secondaryHex, StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(s.TertiaryAccentColor, tertiaryHex, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            _applying = true;
            try
            {
                s.LightBackground = light;
                s.EyeColor = primaryHex;
                s.SecondaryAccentColor = secondaryHex;
                s.TertiaryAccentColor = tertiaryHex;
                SettingsService.NotifyChanged();
            }
            finally { _applying = false; }
        }
        ApplyAccents(primary, secondary, tertiary);
    }

    private static void ApplyLight(bool light)
    {
        if (IsLight == light) return;
        IsLight = light;
        Application.Current?.Dispatcher.Invoke(() => App.ApplyPalette(light));
    }

    private static (Color Primary, Color Secondary, Color Tertiary)? ResolvePreset(string key, bool light)
    {
        if (key == "auto" && ByHourInfo() is { } timedTheme &&
            timedTheme.Primary is { } timedPrimary &&
            timedTheme.Secondary is { } timedSecondary &&
            timedTheme.Accent is { } timedTertiary)
            return EnsurePaletteContrast(Hsl(timedPrimary), Hsl(timedSecondary), Hsl(timedTertiary), light);

        if (key == "system")
        {
            var primary = GetWindowsAccent();
            var (h, saturation, lightness) = ToHsl(primary);
            return EnsurePaletteContrast(primary,
                Hsl(h + 42, Math.Max(62, saturation), Math.Clamp(lightness, 42, 68)),
                Hsl(h + 190, Math.Max(62, saturation), Math.Clamp(lightness, 42, 68)), light);
        }

        var t = Themes.FirstOrDefault(t => t.Key == key);
        return t?.Primary is { } p && t.Secondary is { } secondary && t.Accent is { } accent
            ? EnsurePaletteContrast(Hsl(p), Hsl(secondary), Hsl(accent), light)
            : null;
    }

    private static (Color Primary, Color Secondary, Color Tertiary) EnsurePaletteContrast(
        Color primary, Color secondary, Color tertiary, bool light) =>
        (EnsureAccentContrast(primary, light),
         EnsureAccentContrast(secondary, light),
         EnsureAccentContrast(tertiary, light));

    private static Color EnsureAccentContrast(Color color, bool lightBackground)
    {
        double backgroundLuminance = lightBackground ? 1 : 0;
        if (ContrastRatio(RelativeLuminance(color), backgroundLuminance) >= 4.5)
            return color;

        var (h, saturation, lightness) = ToHsl(color);
        for (int step = 1; step <= 100; step++)
        {
            double adjustedLightness = lightBackground
                ? Math.Max(0, lightness - step)
                : Math.Min(100, lightness + step);
            var candidate = Hsl(h, saturation, adjustedLightness);
            if (ContrastRatio(RelativeLuminance(candidate), backgroundLuminance) >= 4.5)
                return candidate;
        }

        return lightBackground ? Colors.Black : Colors.White;
    }

    private static double ContrastRatio(double first, double second)
    {
        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R) +
               0.7152 * Linearize(color.G) +
               0.0722 * Linearize(color.B);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color ParseColor(string value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    private static void ApplyAccents(Color primary, Color secondary, Color tertiary) =>
        Application.Current?.Dispatcher.Invoke(() =>
            App.ApplyAccentColors(primary, secondary, tertiary));

    private static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        double l = (max + min) / 2;
        double s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
        return (((h + 360) % 360), s * 100, l * 100);
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
