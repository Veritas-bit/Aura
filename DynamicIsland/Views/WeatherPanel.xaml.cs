using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Net;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Вкладка погоды: текущая, почасовой прогноз и неделя.</summary>
public partial class WeatherPanel : UserControl
{
    private int? _sceneKind;
    private bool _animationInitializing;
    private bool _animationReady;
    private string _requestedAnimation = "";

    public WeatherPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WeatherService.Updated += OnWeatherUpdated;
            SettingsService.Changed += OnSettingsChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            WeatherService.Updated -= OnWeatherUpdated;
            SettingsService.Changed -= OnSettingsChanged;
            RemoteWeatherAnimation.Visibility = Visibility.Collapsed;
            WeatherScene.Children.Clear();
            _sceneKind = null;
        };
    }

    private void OnWeatherUpdated() => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.DataBind, new Action(Update));

    private void OnSettingsChanged() => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.DataBind, new Action(Update));

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
            WeatherScene.Children.Clear();
            RemoteWeatherAnimation.Visibility = Visibility.Collapsed;
            _requestedAnimation = "";
            _sceneKind = null;
            HumidityValue.Text = WindValue.Text = PressureValue.Text = UvValue.Text = "—";
            WeatherRange.Text = "Макс. —°  ·  Мин. —°";
            return;
        }

        if (IsLoaded)
        {
            UpdateScene(info.Code);
            if (!_animationReady)
                _ = InitializeWeatherAnimationAsync();
            else
                LoadRemoteWeatherAnimation(info.Code, info.IsDay);
        }
        var (icon, text) = WeatherService.Describe(info.Code);
        if (FindResource(icon) is Geometry g)
            WeatherIcon.Data = g;
        WeatherTemp.Text = $"{info.Temp:0}°";
        WeatherCity.Text = string.IsNullOrWhiteSpace(info.City) ? "Погода рядом с вами" : info.City;
        WeatherDesc.Text = $"{text} · ощущается как {info.FeelsLike:0}°";
        WeatherRange.Text = info.Daily.Count > 0
            ? $"Макс. {info.Daily[0].Max:0}°  ·  Мин. {info.Daily[0].Min:0}°  ·  Осадки {info.Precipitation:0.#} мм"
            : "Прогноз на сегодня обновляется";
        HumidityValue.Text = $"{info.Humidity}%  ·  облачность {info.CloudCover}%";
        WindValue.Text = $"{info.Wind:0.#} м/с  {WindDirectionText(info.WindDirection)}";
        PressureValue.Text = $"{info.Pressure:0} гПа";
        UvValue.Text = $"{info.UvIndex:0.#}  ·  {UvDescription(info.UvIndex)}";
        WeatherCity.ToolTip = "Анимированные погодные иконки: Meteocons (Bas Milius), MIT";

        // Почасовой
        WeatherHours.Children.Clear();
        foreach (var (time, temp, code, precipitationProbability) in info.Hours)
        {
            var (hIcon, _) = WeatherService.Describe(code);
            var chip = new StackPanel { Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            chip.Children.Add(new TextBlock
            {
                Text = time, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextDimBrush")
            });
            if (FindResource(hIcon) is Geometry hg)
                chip.Children.Add(new System.Windows.Shapes.Path
                {
                    Style = (Style)FindResource("VecIcon"), Width = 19, Height = 19, Data = hg,
                    Fill = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5)
                });
            chip.Children.Add(new TextBlock
            {
                Text = $"{temp:0}°", FontSize = 12, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            chip.Children.Add(new TextBlock
            {
                Text = $"{precipitationProbability}%",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            WeatherHours.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("BgDeepBrush"),
                Margin = new Thickness(2),
                Padding = new Thickness(4, 6, 4, 5),
                Child = chip
            });
        }

        // Неделя
        WeatherDaily.Children.Clear();
        foreach (var (day, min, max, code, sunrise, sunset, precipitation, wind) in info.Daily)
        {
            var (dIcon, _) = WeatherService.Describe(code);
            var card = new Border
            {
                Margin = new Thickness(2), Padding = new Thickness(5, 8, 5, 7),
                Background = (Brush)FindResource("BgDeepBrush"),
                CornerRadius = new CornerRadius(12)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = day, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecBrush")
            });
            if (FindResource(dIcon) is Geometry dg)
                stack.Children.Add(new System.Windows.Shapes.Path
                {
                    Style = (Style)FindResource("VecIcon"), Width = 22, Height = 22, Data = dg,
                    Fill = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5)
                });
            stack.Children.Add(new TextBlock
            {
                Text = $"{max:0}°  /  {min:0}°", FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Осадки {precipitation:0.#} мм",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Ветер {wind:0.#} м/с",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"↑ {sunrise}   ↓ {sunset}",
                FontSize = 8.5,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = stack;
            WeatherDaily.Children.Add(card);
        }
    }

    private async Task InitializeWeatherAnimationAsync()
    {
        if (_animationReady || _animationInitializing || !IsLoaded) return;
        _animationInitializing = true;
        try
        {
            RemoteWeatherAnimation.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            await RemoteWeatherAnimation.EnsureCoreWebView2Async();
            if (!IsLoaded) return;
            RemoteWeatherAnimation.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            RemoteWeatherAnimation.CoreWebView2.Settings.AreDevToolsEnabled = false;
            RemoteWeatherAnimation.CoreWebView2.Settings.IsStatusBarEnabled = false;
            RemoteWeatherAnimation.CoreWebView2.Settings.IsZoomControlEnabled = false;
            RemoteWeatherAnimation.CoreWebView2.WebMessageReceived += WeatherAnimation_WebMessageReceived;
            _animationReady = true;
            Update();
        }
        catch (Exception ex)
        {
            RemoteWeatherAnimation.Visibility = Visibility.Collapsed;
            RemoteWeatherAnimation.ToolTip = "Серверная анимация погоды недоступна; используется локальная сцена.";
            Debug.WriteLine($"Meteocons WebView2 initialization failed: {ex}");
        }
        finally
        {
            _animationInitializing = false;
        }
    }

    private void LoadRemoteWeatherAnimation(int code, bool isDay)
    {
        string icon = GetAnimatedIcon(code, isDay);
        if (icon == _requestedAnimation) return;
        _requestedAnimation = icon;

        string source = $"https://bmcdn.nl/assets/weather-icons/v2.0/line/{icon}.svg";
        string encodedSource = WebUtility.HtmlEncode(source);
        string html =
            "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; " +
            "img-src https://bmcdn.nl; style-src 'unsafe-inline'; script-src 'unsafe-inline'\">" +
            "<style>html,body{width:100%;height:100%;margin:0;background:transparent;overflow:hidden}" +
            "body{display:flex;align-items:center;justify-content:center}" +
            "img{width:100%;height:100%;object-fit:contain}</style></head>" +
            $"<body><img src=\"{encodedSource}\" alt=\"Погода\" " +
            "onload=\"window.chrome.webview.postMessage('loaded')\" " +
            "onerror=\"window.chrome.webview.postMessage('error')\"></body></html>";

        RemoteWeatherAnimation.Visibility = Visibility.Visible;
        RemoteWeatherAnimation.NavigateToString(html);
    }

    private void WeatherAnimation_WebMessageReceived(
        object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsLoaded) return;
        string message = e.TryGetWebMessageAsString();
        if (message == "loaded")
        {
            WeatherScene.Visibility = Visibility.Collapsed;
            RemoteWeatherAnimation.Visibility = Visibility.Visible;
        }
        else if (message == "error")
        {
            RemoteWeatherAnimation.Visibility = Visibility.Collapsed;
            WeatherScene.Visibility = Visibility.Visible;
            RemoteWeatherAnimation.ToolTip = "Серверная анимация недоступна; используется локальная сцена.";
        }
    }

    private static string GetAnimatedIcon(int code, bool isDay) => code switch
    {
        0 => isDay ? "clear-day" : "clear-night",
        1 => isDay ? "partly-cloudy-day" : "partly-cloudy-night",
        2 => "overcast",
        3 => "cloudy",
        45 or 48 => "fog",
        51 or 53 or 55 => "drizzle",
        56 or 57 or 66 or 67 => "sleet",
        >= 61 and <= 65 or >= 80 and <= 82 => "rain",
        >= 71 and <= 77 or 85 or 86 => "snow",
        >= 95 => "thunderstorms-rain",
        _ => "cloudy"
    };

    private void UpdateScene(int code)
    {
        int kind = code switch
        {
            0 => 0,
            45 or 48 => 5,
            >= 51 and <= 67 or >= 80 and <= 82 => 2,
            >= 71 and <= 77 or 85 or 86 => 3,
            >= 95 => 4,
            _ => 1
        };
        if (_sceneKind == kind) return;
        _sceneKind = kind;
        WeatherScene.Children.Clear();
        WeatherScene.Visibility = Visibility.Visible;

        if (kind == 0)
        {
            var glow = new Ellipse
            {
                Width = 84, Height = 84,
                Fill = (Brush)FindResource("AccentBrush"),
                Opacity = 0.08
            };
            Canvas.SetLeft(glow, 174);
            Canvas.SetTop(glow, 39);
            WeatherScene.Children.Add(glow);

            var sun = new Ellipse
            {
                Width = 34, Height = 34,
                Fill = (Brush)FindResource("AccentBrush"),
                Opacity = 0.25
            };
            Canvas.SetLeft(sun, 199);
            Canvas.SetTop(sun, 64);
            sun.BeginAnimation(OpacityProperty, new DoubleAnimation(0.18, 0.34,
                TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            WeatherScene.Children.Add(sun);
            return;
        }

        AddCloud(103, kind == 5 ? 35 : 40);

        if (kind == 2 || kind == 4)
        {
            for (int i = 0; i < 10; i++)
            {
                var drop = new Line
                {
                    X1 = 0, Y1 = 0, X2 = -4, Y2 = 11,
                    Stroke = (Brush)FindResource("AccentBrush"),
                    StrokeThickness = 1.4,
                    Opacity = 0.42
                };
                Canvas.SetLeft(drop, 105 + i * 15);
                Canvas.SetTop(drop, 89 + (i % 3) * 7);
                var fall = new TranslateTransform();
                drop.RenderTransform = fall;
                fall.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 27,
                    TimeSpan.FromSeconds(0.85 + (i % 4) * 0.13))
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 90),
                    RepeatBehavior = RepeatBehavior.Forever
                });
                WeatherScene.Children.Add(drop);
            }
        }
        else if (kind == 3)
        {
            for (int i = 0; i < 11; i++)
            {
                var flake = new Ellipse
                {
                    Width = 3 + i % 3,
                    Height = 3 + i % 3,
                    Fill = (Brush)FindResource("AccentBrush"),
                    Opacity = 0.55
                };
                Canvas.SetLeft(flake, 99 + i * 15);
                Canvas.SetTop(flake, 88 + (i % 4) * 7);
                var drift = new TranslateTransform();
                flake.RenderTransform = drift;
                drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 30,
                    TimeSpan.FromSeconds(2.4 + (i % 4) * 0.45))
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 120),
                    RepeatBehavior = RepeatBehavior.Forever
                });
                drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-4, 4,
                    TimeSpan.FromSeconds(1.3 + (i % 3) * 0.2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
                WeatherScene.Children.Add(flake);
            }
        }
        else if (kind == 5)
        {
            for (int i = 0; i < 3; i++)
            {
                var fog = new Line
                {
                    X1 = 0, Y1 = 0, X2 = 58, Y2 = 0,
                    Stroke = (Brush)FindResource("TextSecBrush"),
                    StrokeThickness = 1.5,
                    Opacity = 0.2
                };
                Canvas.SetLeft(fog, 116);
                Canvas.SetTop(fog, 99 + i * 14);
                fog.RenderTransform = new TranslateTransform();
                fog.BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, 0.32,
                    TimeSpan.FromSeconds(2.3 + i * 0.4))
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 240),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
                WeatherScene.Children.Add(fog);
            }
        }

        if (kind == 4)
        {
            var flash = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 195,78 L 209,78 L 201,92 L 211,92 L 190,113 L 197,96 L 188,96 Z"),
                Fill = (Brush)FindResource("AccentBrush"),
                Opacity = 0.12
            };
            flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, 0.62,
                TimeSpan.FromMilliseconds(520))
            {
                BeginTime = TimeSpan.FromSeconds(1.2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
            WeatherScene.Children.Add(flash);
        }
    }

    private void AddCloud(double left, double top)
    {
        var cloud = new Canvas { Width = 92, Height = 50, Opacity = 0.18 };
        cloud.Children.Add(new Ellipse { Width = 48, Height = 27, Fill = (Brush)FindResource("TextPriBrush"), Margin = new Thickness(5, 20, 0, 0) });
        cloud.Children.Add(new Ellipse { Width = 38, Height = 36, Fill = (Brush)FindResource("TextPriBrush"), Margin = new Thickness(25, 10, 0, 0) });
        cloud.Children.Add(new Ellipse { Width = 42, Height = 25, Fill = (Brush)FindResource("TextPriBrush"), Margin = new Thickness(47, 21, 0, 0) });
        var drift = new TranslateTransform();
        cloud.RenderTransform = drift;
        drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-5, 5,
            TimeSpan.FromSeconds(5.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        Canvas.SetLeft(cloud, left);
        Canvas.SetTop(cloud, top);
        WeatherScene.Children.Add(cloud);
    }

    private static string WindDirectionText(int degrees)
    {
        string[] directions = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
        return directions[(int)Math.Round(degrees / 45.0) % directions.Length];
    }

    private static string UvDescription(double value) => value switch
    {
        < 3 => "низкий",
        < 6 => "умеренный",
        < 8 => "высокий",
        < 11 => "очень высокий",
        _ => "экстремальный"
    };
}
