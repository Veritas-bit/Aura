using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Вкладка погоды: текущая, почасовой прогноз и неделя.</summary>
public partial class WeatherPanel : UserControl
{
    public WeatherPanel()
    {
        InitializeComponent();
        WeatherService.Updated += () => Dispatcher.Invoke(Update);
        SettingsService.Changed += () => Dispatcher.Invoke(Update);
        Loaded += (_, _) => Update();
    }

    private void Update()
    {
        var info = WeatherService.Info;
        if (!info.Available)
        {
            WeatherTemp.Text = "—°";
            WeatherCity.Text = "";
            WeatherDesc.Text = string.IsNullOrEmpty(info.Error) ? "Загрузка прогноза…" : info.Error;
            WeatherHours.Children.Clear();
            WeatherDaily.Children.Clear();
            return;
        }

        var (icon, text) = WeatherService.Describe(info.Code);
        if (FindResource(icon) is Geometry g)
            WeatherIcon.Data = g;
        WeatherTemp.Text = $"{info.Temp:0}°";
        WeatherCity.Text = info.City;
        WeatherDesc.Text = $"{text}, ощущается как {info.FeelsLike:0}°, ветер {info.Wind:0} м/с";

        // Почасовой
        WeatherHours.Children.Clear();
        foreach (var (time, temp, code) in info.Hours)
        {
            var (hIcon, _) = WeatherService.Describe(code);
            var chip = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 46 };
            chip.Children.Add(new TextBlock
            {
                Text = time, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextDimBrush")
            });
            if (FindResource(hIcon) is Geometry hg)
                chip.Children.Add(new System.Windows.Shapes.Path
                {
                    Style = (Style)FindResource("VecIcon"), Width = 18, Height = 18, Data = hg,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4)
                });
            chip.Children.Add(new TextBlock
            {
                Text = $"{temp:0}°", FontSize = 12, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            WeatherHours.Children.Add(chip);
        }

        // Неделя
        WeatherDaily.Children.Clear();
        foreach (var (day, min, max, code) in info.Daily)
        {
            var (dIcon, _) = WeatherService.Describe(code);
            var card = new Border
            {
                Margin = new Thickness(2), Padding = new Thickness(6, 10, 6, 10),
                Background = (Brush)FindResource("BgCardBrush"),
                CornerRadius = new CornerRadius(14),
                BorderBrush = (Brush)FindResource("BorderSoftBrush"),
                BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = day, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecBrush")
            });
            if (FindResource(dIcon) is Geometry dg)
                stack.Children.Add(new System.Windows.Shapes.Path
                {
                    Style = (Style)FindResource("VecIcon"), Width = 20, Height = 20, Data = dg,
                    Fill = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8)
                });
            stack.Children.Add(new TextBlock
            {
                Text = $"{max:0}°", FontSize = 12, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{min:0}°", FontSize = 11,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            card.Child = stack;
            WeatherDaily.Children.Add(card);
        }
    }
}
