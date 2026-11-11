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
        ApplyAccent(SettingsService.Current.EyeColor);
        SettingsService.Changed += () => ApplyAccent(SettingsService.Current.EyeColor);
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
            MessageBox.Show($"Непредвиденная ошибка:\n{args.Exception.Message}",
                "AURA", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    /// <summary>Обновляет акцентный цвет (цвет глаз / свечения) во всём приложении.</summary>
    public static void ApplyAccent(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var res = Current.Resources;
            // Кисти из XAML заморожены — заменяем их новыми (DynamicResource обновится сам)
            res["AccentColor"] = color;
            res["AccentBrush"] = new SolidColorBrush(color);
            res["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
            // тематичный оттенок внутреннего бокса — полупрозрачный акцент поверх фона
            res["BgPanelTintBrush"] = new SolidColorBrush(Color.FromArgb(0x16, color.R, color.G, color.B));
        }
        catch { /* некорректный hex — игнорируем */ }
    }

    /// <summary>Переключает палитру приложения: глубокий чёрный ⇄ воздушный светлый.
    /// Все потребители используют DynamicResource, поэтому обновление мгновенное.</summary>
    public static void ApplyPalette(bool light)
    {
        var res = Current.Resources;
        void Set(string key, string hex) =>
            res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        if (light)
        {
            Set("BgDeepBrush", "#F5F6F9");
            Set("BgPanelBrush", "#ECEEF3");
            Set("BgCardBrush", "#FFFFFF");
            Set("BgCardHoverBrush", "#F0F1F5");
            Set("BgInputBrush", "#FFFFFF");
            Set("BorderSoftBrush", "#1A000000");
            Set("TextPriBrush", "#1B1B21");
            Set("TextSecBrush", "#5A5A64");
            Set("TextDimBrush", "#8F8F99");
            Set("OverlayBrush", "#14000000");
            Set("OverlayFaintBrush", "#0A000000");
            Set("BgPopupBrush", "#FAFFFFFF");
            Set("BgWindowBrush", "#F2F5F6F9");
            Set("BgSidebarBrush", "#B0E9EBF0");
            Set("ScrollThumbBrush", "#1F000000");
            Set("EyeHighlightBrush", "#FFFFFF");
        }
        else
        {
            Set("BgDeepBrush", "#000000");
            Set("BgPanelBrush", "#070707");
            Set("BgCardBrush", "#0E0E0E");
            Set("BgCardHoverBrush", "#161616");
            Set("BgInputBrush", "#0A0A0A");
            Set("BorderSoftBrush", "#1FFFFFFF");
            Set("TextPriBrush", "#F5F5F7");
            Set("TextSecBrush", "#9A9AA5");
            Set("TextDimBrush", "#5C5C66");
            Set("OverlayBrush", "#2AFFFFFF");
            Set("OverlayFaintBrush", "#14FFFFFF");
            Set("BgPopupBrush", "#F214141C");
            Set("BgWindowBrush", "#F2000000");
            Set("BgSidebarBrush", "#B0070707");
            Set("ScrollThumbBrush", "#1FFFFFFF");
            Set("EyeHighlightBrush", "#000000");
        }
    }
}
