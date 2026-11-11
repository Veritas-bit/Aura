using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Отдельное окно настроек с боковой навигацией (в духе macOS).</summary>
public partial class SettingsWindow : Window
{
    public const string AppVersion = "1.6.1";

    private static readonly string[] Palette =
        { "#7C4DFF", "#00E5FF", "#00E676", "#FF5252", "#FFD740", "#FF6DFF", "#FF9E40", "#FFFFFF" };

    private static readonly (string Key, string IconKey, string Title)[] Sections =
    {
        ("character", "ic_smile",               "Персонаж"),
        ("themes",    "ic_palette",             "Темы"),
        ("general",   "ic_sliders_horizontal",  "Общее"),
        ("stats",     "ic_cpu",                 "Статус-бар"),
        ("hotkeys",   "ic_keyboard",            "Клавиши"),
        ("ai",        "ic_bot",                 "ИИ"),
        ("store",     "ic_puzzle",              "Модули"),
        ("updates",   "ic_download",            "Обновления"),
    };

    private static readonly string[] HotkeyPresets =
        { "Ctrl+Shift+Space", "Ctrl+Alt+D", "Ctrl+Shift+D", "Ctrl+Alt+Space" };

    private readonly Dictionary<string, Button> _navButtons = new();
    private string _currentSection = "";
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Версия {AppVersion}";
        UpdatesVersionText.Text = $"Версия {AppVersion}";
        BuildPalette();
        BuildNav();
        BuildThemeCards();
        BuildHotkeys();

        Loaded += (_, _) =>
        {
            LoadValues();
            SelectSection("character");
        };
        SettingsService.Changed += () => { if (IsLoaded) LoadValues(); };
    }

    // ---------- Сайдбар ----------

    private void BuildNav()
    {
        foreach (var (key, iconKey, title) in Sections)
        {
            var btn = new Button
            {
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 1, 0, 1),
                Tag = key,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Shapes.Path
                        {
                            Style = (Style)FindResource("VecIcon"),
                            Width = 15, Height = 15,
                            Data = (Geometry)FindResource(iconKey),
                            Margin = new Thickness(0, 0, 10, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock { Text = title, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            btn.Template = NavTemplate();
            btn.Click += (_, _) => SelectSection(key);
            NavPanel.Children.Add(btn);
            _navButtons[key] = btn;
        }
    }

    private static ControlTemplate NavTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.PaddingProperty,
            new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty,
            new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void SelectSection(string key)
    {
        _currentSection = key;
        PageCharacter.Visibility = key == "character" ? Visibility.Visible : Visibility.Collapsed;
        PageThemes.Visibility = key == "themes" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = key == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageStats.Visibility = key == "stats" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = key == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PageAi.Visibility = key == "ai" ? Visibility.Visible : Visibility.Collapsed;
        PageStore.Visibility = key == "store" ? Visibility.Visible : Visibility.Collapsed;
        PageUpdates.Visibility = key == "updates" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (k, btn) in _navButtons)
        {
            bool sel = k == key;
            btn.Background = sel
                ? (Brush)Application.Current.FindResource("AccentSoftBrush")
                : Brushes.Transparent;
        }
        var section = Sections.First(s => s.Key == key);
        PageTitle.Text = section.Title;
    }

    // ---------- Загрузка значений ----------

    private void LoadValues()
    {
        _loaded = false;
        var s = SettingsService.Current;

        HexBox.Text = s.EyeColor;
        SwGlow.IsChecked = s.GlowEnabled;

        StyleIsland.IsChecked = s.CollapsedStyle != "Notch";
        StyleNotch.IsChecked = s.CollapsedStyle == "Notch";

        SwClock.IsChecked = s.ShowClock;
        SwSeconds.IsChecked = s.ShowSeconds;
        SwWeather.IsChecked = s.ShowWeather;
        SwBattery.IsChecked = s.ShowBattery;
        SwStats.IsChecked = s.ShowStats;
        SwCpu.IsChecked = s.StatCpu;
        SwRam.IsChecked = s.StatRam;
        SwGpu.IsChecked = s.StatGpu;
        SwGameMode.IsChecked = s.GameMode;

        SwTopmost.IsChecked = s.Topmost;
        SwCollapseLoss.IsChecked = s.CollapseOnFocusLoss;
        SwAutostart.IsChecked = s.RunAtStartup;
        OpacitySlider.Value = s.UiOpacity * 100;
        OpacityText.Text = $"{s.UiOpacity * 100:0}%";

        SelectHotkey(HotkeyPresets.Contains(s.Hotkey) ? s.Hotkey : HotkeyPresets[0]);

        ApiBaseUrlBox.Text = s.AiBaseUrl;
        ApiKeyBox.Password = s.AiApiKey;
        ApiModelBox.Text = s.AiModel;

        SwUpdates.IsChecked = s.CheckUpdates;

        UpdateThemeSelection();
        _loaded = true;
    }

    // ---------- Цвет глаз ----------

    private void BuildPalette()
    {
        foreach (var hex in Palette)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(3), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = hex
            };
            b.Template = RoundButtonTemplate();
            b.Click += (_, _) => SetEyeColor(hex);
            PaletteWrap.Children.Add(b);
        }
    }

    private static ControlTemplate RoundButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void Hex_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SetEyeColor(HexBox.Text);
    }

    private void Hex_Apply(object s, RoutedEventArgs e) => SetEyeColor(HexBox.Text);

    private void SetEyeColor(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            SettingsService.Current.EyeColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            SettingsService.Current.Theme = "custom"; // ручной цвет отменяет тему
            SettingsService.NotifyChanged();
        }
        catch { HexBox.Text = SettingsService.Current.EyeColor; }
    }

    // ---------- Темы ----------

    private readonly Dictionary<string, Border> _themeCards = new();

    private void BuildThemeCards()
    {
        foreach (var theme in ThemeService.Themes)
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var color in PreviewColors(theme))
            {
                dots.Children.Add(new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(0, 0, 4, 0)
                });
            }

            var content = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            content.Children.Add(dots);
            content.Children.Add(new TextBlock
            {
                Text = theme.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 1)
            });
            content.Children.Add(new TextBlock
            {
                Text = theme.Hint, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecBrush")
            });

            var card = new Border
            {
                Width = 186, CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("BgCardBrush"),
                BorderBrush = (Brush)FindResource("BorderSoftBrush"),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Child = content,
                Tag = theme.Key
            };
            card.MouseLeftButtonDown += (_, _) =>
            {
                SettingsService.Current.Theme = theme.Key;
                ThemeService.Apply();
                SettingsService.NotifyChanged();
            };
            ThemeCardsHost.Children.Add(card);
            _themeCards[theme.Key] = card;
        }
    }

    /// <summary>Цвета-точки для превью карточки темы.</summary>
    private static IEnumerable<Color> PreviewColors(ThemeInfo theme) => theme.Key switch
    {
        "system" => new[] { ThemeService.GetWindowsAccent() },
        "auto" => new[]
        {
            ThemeService.Hsl(271, 91, 65), // утро
            ThemeService.Hsl(350, 90, 70), // день
            ThemeService.Hsl(25, 90, 60),  // вечер
            ThemeService.Hsl(80, 90, 50),  // ночь
        },
        _ => new[] { theme.Primary, theme.Secondary, theme.Accent }
            .Where(c => c.HasValue).Select(c => ThemeService.Hsl(c!.Value)),
    };

    private void UpdateThemeSelection()
    {
        string current = SettingsService.Current.Theme;
        foreach (var (key, card) in _themeCards)
        {
            bool sel = key == current;
            card.BorderBrush = sel
                ? (Brush)Application.Current.FindResource("AccentBrush")
                : (Brush)FindResource("BorderSoftBrush");
            card.Background = sel
                ? (Brush)Application.Current.FindResource("AccentSoftBrush")
                : (Brush)FindResource("BgCardBrush");
        }
    }

    // ---------- Общее ----------

    private void Style_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SettingsService.Current.CollapsedStyle = StyleNotch.IsChecked == true ? "Notch" : "Island";
        SettingsService.NotifyChanged();
    }

    private void Display_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var st = SettingsService.Current;
        st.ShowClock = SwClock.IsChecked == true;
        st.ShowSeconds = SwSeconds.IsChecked == true;
        st.ShowWeather = SwWeather.IsChecked == true;
        st.ShowBattery = SwBattery.IsChecked == true;
        st.ShowStats = SwStats.IsChecked == true;
        st.StatCpu = SwCpu.IsChecked == true;
        st.StatRam = SwRam.IsChecked == true;
        st.StatGpu = SwGpu.IsChecked == true;
        SettingsService.NotifyChanged();
    }

    private void Behavior_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var st = SettingsService.Current;
        st.GlowEnabled = SwGlow.IsChecked == true;
        st.Topmost = SwTopmost.IsChecked == true;
        st.CollapseOnFocusLoss = SwCollapseLoss.IsChecked == true;
        st.GameMode = SwGameMode.IsChecked == true;
        SettingsService.NotifyChanged();
    }

    private void Autostart_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SettingsService.Current.RunAtStartup = SwAutostart.IsChecked == true;
        SystemControlService.SetAutoStart(SettingsService.Current.RunAtStartup);
        SettingsService.NotifyChanged();
    }

    private void Opacity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText == null) return; // событие может прийти во время InitializeComponent
        OpacityText.Text = $"{e.NewValue:0}%";
        if (!_loaded) return;
        SettingsService.Current.UiOpacity = e.NewValue / 100.0;
        SettingsService.NotifyChanged();
    }

    private void Reset_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Сбросить все настройки к значениям по умолчанию?", "AURA",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SettingsService.Reset();
    }

    // ---------- Клавиши ----------

    private readonly Dictionary<string, RadioButton> _hotkeyRadios = new();

    private void BuildHotkeys()
    {
        foreach (var combo in HotkeyPresets)
        {
            var radio = new RadioButton
            {
                GroupName = "Hotkey",
                Content = combo,
                Foreground = (Brush)FindResource("TextPriBrush"),
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4),
                Tag = combo
            };
            radio.Checked += (_, _) =>
            {
                if (!_loaded) return;
                SettingsService.Current.Hotkey = combo;
                SettingsService.NotifyChanged();
            };
            HotkeyList.Children.Add(radio);
            _hotkeyRadios[combo] = radio;
        }
    }

    private void SelectHotkey(string combo)
    {
        foreach (var (key, radio) in _hotkeyRadios)
            radio.IsChecked = key == combo;
    }

    // ---------- ИИ ----------

    private void Ai_Save(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var st = SettingsService.Current;
        st.AiBaseUrl = ApiBaseUrlBox.Text.Trim().TrimEnd('/');
        st.AiApiKey = ApiKeyBox.Password;
        st.AiModel = ApiModelBox.Text.Trim();
        SettingsService.NotifyChanged();
    }

    // ---------- Обновления ----------

    private void Updates_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SettingsService.Current.CheckUpdates = SwUpdates.IsChecked == true;
        SettingsService.NotifyChanged();
    }

    private void CheckNow_Click(object s, RoutedEventArgs e) =>
        MessageBox.Show(this, $"У вас последняя версия — {AppVersion}.", "AURA",
            MessageBoxButton.OK, MessageBoxImage.Information);

    // ---------- Окно ----------

    private void Window_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // не дёргать окно, когда нажали на кнопку (например, крестик)
        if (e.OriginalSource is DependencyObject src && FindParent<Button>(src) != null) return;
        DragMove();
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d) ?? (d is FrameworkElement fe ? fe.Parent : null);
        }
        return null;
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Exit_Click(object s, RoutedEventArgs e) => Application.Current.Shutdown();
}
