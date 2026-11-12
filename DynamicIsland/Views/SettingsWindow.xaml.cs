using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Отдельное окно настроек с боковой навигацией (в духе macOS).</summary>
public partial class SettingsWindow : Window
{
    public const string AppVersion = "0.0.1";

    private static readonly (string Key, string IconKey, string Title)[] Sections =
    {
        ("themes",    "ic_palette",             "Внешний вид"),
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
        BuildNav();
        BuildThemeCards();
        BuildHotkeys();

        Loaded += (_, _) =>
        {
            LoadValues();
            SelectSection("themes");
        };
        SettingsService.Changed += () =>
        {
            if (!IsLoaded) return;
            LoadValues();
        };
    }

    // ---------- Сайдбар ----------

    private void BuildNav()
    {
        foreach (var (key, iconKey, title) in Sections)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Style = (Style)FindResource("VecIcon"),
                Width = 16,
                Height = 16,
                Data = (Geometry)FindResource(iconKey),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextSecBrush");
            var label = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
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
                    Children = { icon, label }
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
        border.Name = "NavBackground";
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
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters = { new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("BgCardHoverBrush"), "NavBackground") }
        });
        return template;
    }

    private void SelectSection(string key)
    {
        _currentSection = key;
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
            if (btn.Content is StackPanel row)
            {
                var icon = row.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
                var label = row.Children.OfType<TextBlock>().FirstOrDefault();
                var accent = k switch
                {
                    "general" or "hotkeys" or "updates" => "AccentSecondaryBrush",
                    "stats" or "ai" => "AccentTertiaryBrush",
                    _ => "AccentBrush"
                };
                icon?.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                    sel ? accent : "TextSecBrush");
                label?.SetResourceReference(TextBlock.ForegroundProperty,
                    sel ? accent : "TextSecBrush");
                if (label is not null)
                    label.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Medium;
            }
        }
        var section = Sections.First(s => s.Key == key);
        PageTitle.Text = section.Title;
        PageTitle.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
    }

    // ---------- Загрузка значений ----------

    private void LoadValues()
    {
        _loaded = false;
        var s = SettingsService.Current;

        StyleIsland.IsChecked = s.CollapsedStyle != "Notch";
        StyleNotch.IsChecked = s.CollapsedStyle == "Notch";

        SwClock.IsChecked = s.ShowClock;
        SwSeconds.IsChecked = s.ShowSeconds;
        SwWeather.IsChecked = s.ShowWeather;
        SwWeatherInIsland.IsChecked = s.ShowWeatherInIsland;
        s.CollapsedTrackDisplayMode = AppSettings.NormalizeTrackDisplayMode(s.CollapsedTrackDisplayMode);
        TrackDisplayModeBox.SelectedIndex = s.CollapsedTrackDisplayMode - 1;
        SwReferenceTrackCard.IsChecked = s.ReferenceTrackCard;
        SwBattery.IsChecked = s.ShowBattery;
        SwStats.IsChecked = s.ShowStats;
        SwCpu.IsChecked = s.StatCpu;
        SwRam.IsChecked = s.StatRam;
        SwGpu.IsChecked = s.StatGpu;
        SwGameMode.IsChecked = s.GameMode;

        SwTopmost.IsChecked = s.Topmost;
        SwCollapseLoss.IsChecked = s.CollapseOnFocusLoss;
        SwAutostart.IsChecked = s.RunAtStartup;
        BlackBackground.IsChecked = !s.LightBackground;
        WhiteBackground.IsChecked = s.LightBackground;
        PrimaryAccentBox.Text = s.EyeColor;
        SecondaryAccentBox.Text = s.SecondaryAccentColor;
        TertiaryAccentBox.Text = s.TertiaryAccentColor;

        SelectHotkey(HotkeyPresets.Contains(s.Hotkey) ? s.Hotkey : HotkeyPresets[0]);

        ApiBaseUrlBox.Text = s.AiBaseUrl;
        ApiKeyBox.Password = s.AiApiKey;
        ApiModelBox.Text = s.AiModel;

        SwUpdates.IsChecked = s.CheckUpdates;

        UpdateThemeSelection();
        _loaded = true;
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
    private static IEnumerable<Color> PreviewColors(ThemeInfo theme)
    {
        var palette = ThemeService.GetThemePalette(theme.Key);
        return palette is { } colors
            ? new[] { colors.Primary, colors.Secondary, colors.Tertiary }
            : Array.Empty<Color>();
    }

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
        st.ShowWeatherInIsland = SwWeatherInIsland.IsChecked == true;
        st.ShowBattery = SwBattery.IsChecked == true;
        st.ReferenceTrackCard = SwReferenceTrackCard.IsChecked == true;
        st.ShowStats = SwStats.IsChecked == true;
        st.StatCpu = SwCpu.IsChecked == true;
        st.StatRam = SwRam.IsChecked == true;
        st.StatGpu = SwGpu.IsChecked == true;
        SettingsService.NotifyChanged();
    }

    private void TrackDisplayMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || TrackDisplayModeBox.SelectedIndex < 0) return;
        SettingsService.Current.CollapsedTrackDisplayMode = Math.Clamp(TrackDisplayModeBox.SelectedIndex + 1, 1, 3);
        SettingsService.NotifyChanged();
    }

    private void BackgroundMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var settings = SettingsService.Current;
        settings.Theme = "custom";
        settings.LightBackground = WhiteBackground.IsChecked == true;
        SettingsService.NotifyChanged();
    }

    private void AccentColor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not TextBox box) return;
        if (!TryParseAccent(box.Text, out var color))
        {
            MessageBox.Show(this, "Введите цвет в формате #RRGGBB.", "Некорректный цвет",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadValues();
            return;
        }

        var settings = SettingsService.Current;
        settings.Theme = "custom";
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (ReferenceEquals(box, PrimaryAccentBox))
            settings.EyeColor = hex;
        else if (ReferenceEquals(box, SecondaryAccentBox))
            settings.SecondaryAccentColor = hex;
        else
            settings.TertiaryAccentColor = hex;
        SettingsService.NotifyChanged();
    }

    private static bool TryParseAccent(string value, out Color color)
    {
        color = Colors.Transparent;
        value = value.Trim();
        if (value.Length != 7 || value[0] != '#') return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Behavior_Changed(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var st = SettingsService.Current;
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

    private void Reset_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Сбросить все настройки к значениям по умолчанию?", "AURA",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            SystemControlService.SetAutoStart(false);
            SettingsService.Reset();
        }
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
