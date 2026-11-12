using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Одна копия приложения
        _mutex = new Mutex(true, "AuraCompanion.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("AURA уже запущена.", "AURA",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        SettingsService.Load();
        ApplyAccentColors(SettingsService.Current.EyeColor,
            SettingsService.Current.SecondaryAccentColor,
            SettingsService.Current.TertiaryAccentColor);
        SettingsService.Changed += () => ApplyAccentColors(SettingsService.Current.EyeColor,
            SettingsService.Current.SecondaryAccentColor,
            SettingsService.Current.TertiaryAccentColor);
        ThemeService.Init();
        WeatherService.Init();

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(SettingsService.DataDir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(SettingsService.DataDir, "crash.log"),
                    args.Exception.ToString());
            }
            catch { }
            var root = args.Exception.GetBaseException();
            var crashLog = System.IO.Path.Combine(SettingsService.DataDir, "crash.log");
            MessageBox.Show(
                $"Непредвиденная ошибка:\n{root.Message}\n\nПолная информация: {crashLog}",
                "AURA", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        base.OnStartup(e);
    }

    /// <summary>Обновляет акцентный цвет приложения.</summary>
    public static void ApplyAccent(string hex)
    {
        var settings = SettingsService.Current;
        ApplyAccentColors(hex, settings.SecondaryAccentColor, settings.TertiaryAccentColor);
    }

    public static void ApplyAccentColors(string primaryHex, string secondaryHex, string tertiaryHex)
    {
        try
        {
            ApplyAccentColors(
                (Color)ColorConverter.ConvertFromString(primaryHex),
                (Color)ColorConverter.ConvertFromString(secondaryHex),
                (Color)ColorConverter.ConvertFromString(tertiaryHex));
        }
        catch (FormatException) { }
    }

    public static void ApplyAccentColors(Color primary, Color secondary, Color tertiary)
    {
        var resources = Current.Resources;
        resources["AccentColor"] = primary;
        resources["AccentSecondaryColor"] = secondary;
        resources["AccentTertiaryColor"] = tertiary;
        resources["AccentBrush"] = new SolidColorBrush(primary);
        resources["AccentOnBrush"] = new SolidColorBrush(GetContrastingTextColor(primary));
        resources["AccentSecondaryBrush"] = new SolidColorBrush(secondary);
        resources["AccentTertiaryBrush"] = new SolidColorBrush(tertiary);
        resources["GreenBrush"] = new SolidColorBrush(tertiary);
        resources["AccentSoftBrush"] = new SolidColorBrush(
            Color.FromArgb(0x30, primary.R, primary.G, primary.B));
    }

    private static Color GetContrastingTextColor(Color background)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        double luminance = 0.2126 * Linearize(background.R) +
                           0.7152 * Linearize(background.G) +
                           0.0722 * Linearize(background.B);
        double blackContrast = (luminance + 0.05) / 0.05;
        double whiteContrast = 1.05 / (luminance + 0.05);
        return blackContrast >= whiteContrast ? Colors.Black : Colors.White;
    }

    /// <summary>Переключает весь интерфейс между контрастной чёрной и белой палитрами.</summary>
    public static void ApplyPalette(bool light)
    {
        var res = Current.Resources;
        void Set(string key, string hex) =>
            res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        if (light)
        {
            Set("BgDeepBrush", "#FFFFFF");
            Set("BgPanelBrush", "#FFFFFF");
            Set("BgCardBrush", "#FFF7F7F7");
            Set("BgCardHoverBrush", "#FFEAEAEA");
            Set("BgInputBrush", "#FFFFFF");
            Set("BorderSoftBrush", "#FF222222");
            Set("TextPriBrush", "#FF000000");
            Set("TextSecBrush", "#FF383838");
            Set("TextDimBrush", "#FF5C5C5C");
            Set("OverlayBrush", "#FFE2E2E2");
            Set("OverlayFaintBrush", "#FFF1F1F1");
            Set("BgPopupBrush", "#FFFFFFFF");
            Set("BgWindowBrush", "#FFFFFFFF");
            Set("BgSidebarBrush", "#FFF5F5F5");
            Set("ScrollThumbBrush", "#FF707070");
            Set("EyeHighlightBrush", "#000000");
            Set("ControlThumbBrush", "#FF000000");
        }
        else
        {
            Set("BgDeepBrush", "#FF000000");
            Set("BgPanelBrush", "#FF000000");
            Set("BgCardBrush", "#FF101010");
            Set("BgCardHoverBrush", "#FF1A1A1A");
            Set("BgInputBrush", "#FF080808");
            Set("BorderSoftBrush", "#FF383838");
            Set("TextPriBrush", "#FFFFFFFF");
            Set("TextSecBrush", "#FFD0D0D0");
            Set("TextDimBrush", "#FF9A9A9A");
            Set("OverlayBrush", "#FF282828");
            Set("OverlayFaintBrush", "#FF181818");
            Set("BgPopupBrush", "#FF080808");
            Set("BgWindowBrush", "#FF000000");
            Set("BgSidebarBrush", "#FF080808");
            Set("ScrollThumbBrush", "#FF666666");
            Set("EyeHighlightBrush", "#000000");
            Set("ControlThumbBrush", "#FFFFFFFF");
        }
    }
}
