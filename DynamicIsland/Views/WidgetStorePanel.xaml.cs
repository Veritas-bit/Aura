using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Витрина будущих модулей-виджетов.</summary>
public partial class WidgetStorePanel : UserControl
{
    private record StoreItem(string Id, string IconKey, string Name, string Desc);

    private static readonly StoreItem[] Catalog =
    {
        new("weather",  "ic_cloud_sun",      "Погода", "Текущая погода и прогноз на островке"),
        new("currency", "ic_coins",          "Курсы валют", "USD / EUR / CNY с алертами"),
        new("pomodoro", "ic_timer",          "Pomodoro", "Фокус-таймер со статистикой"),
        new("snake",    "ic_gamepad_2",      "Мини-игры", "Змейка и 2048 прямо в панели"),
        new("rss",      "ic_newspaper",      "RSS-лента", "Заголовки новостей бегущей строкой"),
        new("spotify",  "ic_headphones",     "Spotify Connect", "Управление Spotify и обложки"),
        new("telegram", "ic_send",           "Telegram-мини", "Быстрые ответы на сообщения"),
        new("syspro",   "ic_chart_line",     "Мониторинг Pro", "Графики CPU/GPU за сессию"),
        new("crypto",   "ic_bitcoin",        "Крипто", "BTC / ETH и портфель"),
        new("habits",   "ic_calendar_check", "Привычки", "Трекер привычек и стрики"),
    };

    public WidgetStorePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        StoreWrap.Children.Clear();
        foreach (var item in Catalog)
            StoreWrap.Children.Add(BuildCard(item));
    }

    private UIElement BuildCard(StoreItem item)
    {
        bool builtIn = item.Id == "weather"; // погода встроена в главную панель
        bool wished = SettingsService.Current.WidgetWishes.Contains(item.Id);

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = builtIn ? "Встроено" : "Скоро", FontSize = 9,
                Foreground = builtIn
                    ? (Brush)FindResource("GreenBrush")
                    : (Brush)FindResource("TextSecBrush")
            }
        };

        var wishBtn = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Content = wished ? "✓ В списке ожидания" : "Хочу!",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = builtIn ? Visibility.Collapsed : Visibility.Visible
        };
        wishBtn.Click += (_, _) =>
        {
            var list = SettingsService.Current.WidgetWishes;
            if (list.Contains(item.Id)) list.Remove(item.Id); else list.Add(item.Id);
            SettingsService.Save();
            Build();
        };

        return new Border
        {
            Width = 160, Margin = new Thickness(4), Padding = new Thickness(12),
            Background = (Brush)FindResource("BgCardBrush"),
            CornerRadius = new CornerRadius(14),
            BorderBrush = (Brush)FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new System.Windows.Shapes.Path
                    {
                        Style = (Style)FindResource("VecIcon"),
                        Width = 26, Height = 26,
                        Data = (Geometry)FindResource(item.IconKey),
                        HorizontalAlignment = HorizontalAlignment.Left
                    },
                    new TextBlock { Text = item.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                    new TextBlock
                    {
                        Text = item.Desc, FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextSecBrush"), Margin = new Thickness(0, 2, 0, 6)
                    },
                    badge,
                    wishBtn
                }
            }
        };
    }
}
