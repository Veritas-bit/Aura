using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private readonly SystemMonitorService _monitor = new();
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _hoverExpandTimer;
    private readonly DispatcherTimer _hoverCollapseTimer;
    private readonly DispatcherTimer _hoverPoll;
    private readonly DispatcherTimer _gameModeTimer;
    private readonly Dictionary<string, UserControl> _panels = new();
    private readonly Dictionary<string, (StackPanel Root, Border Fill, TextBlock Value, string Label)> _statItems = new();
    private bool _expanded;
    private bool _animating;
    private bool _navGuard;
    private bool _gameMode;
    private Point _lastCursor;
    private DateTime _lastActivity = DateTime.Now;
    private TrayService? _tray;

    // Геометрия свёрнутого вида
    private const double CollapsedH = 50;          // высота островка/выреза
    private const double GameH = 30;               // толщина выреза в игровом режиме
    private const double BodyGap = 14;             // единый отступ: от края тела и между элементами
    private const double CollapsedBaseW = 172;     // оценка до первой раскладки: часы + компаньон + батарея
    private const double StatItemW = 46;           // ширина одного элемента статус-бара с отступом
    private const double NotchShoulder = 14;       // ширина «плечика» выреза
    private const double IslandMarginH = 8;        // боковые поля фона островка
    private const double IslandMarginV = 20;       // суммарные вертикальные поля (8+12)
    private const double ExpandedContentW = 764;

    private static readonly string[] TabOrder =
        { "home", "player", "weather", "apps", "ai", "control" };

    public MainWindow()
    {
        InitializeComponent();

        BuildStatsStrip();

        // Исходная форма тела и позиция окна до первого кадра
        Top = TopForMode();
        RecenterHorizontal();
        ApplyShape();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += (_, _) => TickClock();
        _clockTimer.Start();

        // Наведение мыши: развернуть; убирание — свернуть (с задержками).
        // Отслеживание — опросом (WPF-события на layered-окне нестабильны).
        _hoverExpandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverExpandTimer.Tick += (_, _) => { _hoverExpandTimer.Stop(); Expand(); };
        _hoverCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _hoverCollapseTimer.Tick += (_, _) =>
        {
            _hoverCollapseTimer.Stop();
            GetCursorPos(out var pt);
            if (!_bodyRect.Contains(pt.X, pt.Y) && IslandMenu.IsOpen != true) Collapse();
        };
        _hoverPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _hoverPoll.Tick += (_, _) => HoverPoll();
        _hoverPoll.Start();

        // Игровой режим: слежение за полноэкранными приложениями
        _gameModeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameModeTimer.Tick += (_, _) => CheckGameMode();
        _gameModeTimer.Start();

        _monitor.Updated += OnStats;
        SettingsService.Changed += ApplySettings;

        // после первой раскладки — пересчитать тело по замеренной ширине контента
        Loaded += (_, _) => ApplyShape();

        SizeChanged += (_, _) =>
        {
            if (!_animating) RecenterHorizontal();
        };
        ExpandedView.SizeChanged += (_, _) => { if (!_animating) MoveNavPill(animate: false); };

        // Жест смахивания по развёрнутой панели — листание вкладок
        PreviewMouseLeftButtonDown += Window_PreviewDown;
        PreviewMouseLeftButtonUp += Window_PreviewUp;

        // Сторож геометрии: на этой машине внешняя утилита периодически пытается
        // пересчитать размер окна (видны посторонние SetWindowPos/WM_SIZE). Пока идёт
        // наша анимация — размеры задаёт она; в остальное время любое отклонение
        // от целевой геометрии немедленно откатывается, до того как layout его увидит.
        var heightDescriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(HeightProperty, typeof(Window));
        heightDescriptor.AddValueChanged(this, (_, _) => GuardGeometry());
        var widthDescriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(WidthProperty, typeof(Window));
        widthDescriptor.AddValueChanged(this, (_, _) => GuardGeometry());

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            MakeToolWindow();
            // хук окна: хоткей + защита геометрии от внешних SetWindowPos + трей
            if (!_hookAdded)
            {
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
                _hookAdded = true;
            }
            RegisterGlobalHotkey();

            // Иконка в трее: тема, статус-бар, выход
            _tray = new TrayService(_hwnd);
            _tray.Command += OnTrayCommand;
            _tray.UpdateIcon(CurrentAccent());

            ApplySettings();
            RecenterHorizontal();
        };

        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _monitor.Dispose();
        };
    }

    // ==================== Геометрия ====================

    private void RecenterHorizontal()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
    }

    /// <summary>Откат посторонних изменений размера к целевой геометрии (см. сторож выше).</summary>
    private void GuardGeometry()
    {
        if (_animating || _guarding) return;
        double w = WindowW(), h = WindowH();
        if (Math.Abs(Width - w) <= 0.5 && Math.Abs(Height - h) <= 0.5) return;
        _guarding = true;
        try
        {
            Width = w;
            Height = h;
            Top = TopForMode();
            RecenterHorizontal();
        }
        finally { _guarding = false; }
    }
    private bool _guarding;

    private bool IsNotch => _gameMode || SettingsService.Current.CollapsedStyle == "Notch";

    private double CollapsedHeight => _gameMode ? GameH : CollapsedH;

    /// <summary>Ширина содержимого: замер реального контента.
    /// До первой раскладки — оценка: базовые 172 плюс элементы статус-бара.</summary>
    private double CollapsedContentW()
    {
        if (IsLoaded && CollapsedStack.DesiredSize.Width > 4)
            return CollapsedStack.DesiredSize.Width;

        var s = SettingsService.Current;
        if (!s.ShowStats) return CollapsedBaseW;
        int n = (s.StatCpu ? 1 : 0) + (s.StatRam ? 1 : 0)
              + (s.StatGpu && _monitor.Stats.GpuAvailable ? 1 : 0);
        if (_gameMode) n += 4; // температура CPU/GPU, ping, потери
        return CollapsedBaseW + n * StatItemW;
    }

    private double ExpandedContentH()
    {
        var wa = SystemParameters.WorkArea;
        return Math.Min(620, wa.Height - 80);
    }

    // Окно ВСЕГДА фиксированного размера — анимируется фигура тела внутри него,
    // а не само окно. Благодаря этому пружина идёт в render-потоке без дёрганья.
    private double WindowW() => ExpandedContentW + 2 * NotchShoulder + 16; // 808
    private double WindowH() => ExpandedContentH() + IslandMarginV;        // 640 при 1080p

    private double TopForMode() =>
        SystemParameters.WorkArea.Top + (IsNotch ? 0 : 2);

    // ==================== Прогресс и форма тела ====================

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).ApplyShape()));

    /// <summary>0 = свёрнутый островок, 1 = развёрнутая панель.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Пересчёт фигуры тела и видимости контента на каждом кадре прогресса.</summary>
    private void ApplyShape()
    {
        double p = Progress;
        double c = Math.Clamp(p, 0, 1);
        _animating = Math.Abs(p - (_expanded ? 1.0 : 0.0)) > 0.002;

        bool notch = IsNotch;
        double margin = notch ? NotchShoulder : IslandMarginH;
        double contentW = CollapsedContentW();
        // свёрнутое тело обнимает контент ровно на BodyGap — отступы от края
        // равны отступам между элементами; margin здесь не участвует
        double collapsedW = contentW + 2 * BodyGap;
        double collapsedH = CollapsedHeight;
        double expandedW = ExpandedContentW + 2 * margin;
        double expandedH = ExpandedContentH();

        double w = Math.Max(1, Lerp(collapsedW, expandedW, p));
        double h = Math.Max(1, Lerp(collapsedH, expandedH, p));
        double x = (Width - w) / 2;
        double y = notch ? 0 : 8;

        if (notch)
        {
            double r = Lerp(_gameMode ? 11 : 15, 22, c);
            Body.Data = NotchGeometry(w, h, r);
        }
        else
        {
            double r = Lerp(collapsedH / 2, 24, c);
            Body.Data = RoundedRect(w, h, Math.Min(r, h / 2));
        }
        Body.Margin = new Thickness(x, y, 0, 0);
        _bodyRect = new Rect(Left + x, Top + y, w, h);

        // Содержимое исчезает с первого кадра анимации:
        // при раскрытии свёрнутый вид гаснет мгновенно, развёрнутый проявляется
        // после 55 % пути; при сворачивании — наоборот.
        bool opening = _expanded;
        CollapsedView.Margin = new Thickness(x + BodyGap, y, 0, 0);
        CollapsedView.Width = Math.Max(1, contentW);
        CollapsedView.Height = collapsedH;
        CollapsedView.Opacity = opening ? 0 : Math.Clamp((0.45 - c) / 0.45, 0, 1);
        CollapsedView.IsHitTestVisible = !opening && c < 0.1;

        ExpandedView.Margin = new Thickness(x + margin + 4, y + 2, 0, 0);
        ExpandedView.Width = ExpandedContentW - 8;
        ExpandedView.Height = expandedH - 2;
        ExpandedView.Opacity = opening ? Math.Clamp((c - 0.55) / 0.45, 0, 1) : 0;
        ExpandedView.IsHitTestVisible = opening && c > 0.9;
    }

    private static Geometry RoundedRect(double w, double h, double r) =>
        new RectangleGeometry(new Rect(0, 0, w, h), r, r);

    /// <summary>Фигура выреза: «плечики» — выпуклые дуги у верхнего края экрана.</summary>
    private static Geometry NotchGeometry(double w, double h, double r)
    {
        double s = NotchShoulder;
        if (w < 2 * s + 2 * r || h < s + r)
            return RoundedRect(w, h, Math.Min(r, h / 2));

        var fig = new PathFigure { StartPoint = new Point(0, 0), IsFilled = true, IsClosed = true };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(s, 0), new Point(s, s), true));       // левое плечико
        fig.Segments.Add(new LineSegment(new Point(s, h - r), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(s, h), new Point(s + r, h), true));    // низ слева
        fig.Segments.Add(new LineSegment(new Point(w - s - r, h), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(w - s, h), new Point(w - s, h - r), true)); // низ справа
        fig.Segments.Add(new LineSegment(new Point(w - s, s), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(w - s, 0), new Point(w, 0), true));    // правое плечико
        return new PathGeometry(new[] { fig });
    }

    // ==================== Применение настроек ====================

    private Color CurrentAccent()
    {
        try { return (Color)ColorConverter.ConvertFromString(SettingsService.Current.EyeColor); }
        catch { return (Color)ColorConverter.ConvertFromString("#7C4DFF"); }
    }

    private void ApplySettings()
    {
        var s = SettingsService.Current;
        var accent = CurrentAccent();

        EyesCollapsed.SetLineTheme(ThemeService.IsLight);
        EyesCollapsed.SetEyeColor(accent);
        EyesCollapsed.Scale = _gameMode ? 0.6 : 0.95;
        _tray?.UpdateIcon(accent);

        // Тень снизу и по бокам в цвет компаньона (1–2 px смещение)
        if (Body.Effect is DropShadowEffect shadow)
        {
            shadow.Color = accent;
            shadow.Opacity = s.GlowEnabled ? 0.55 : 0;
        }

        // Фон в цвет темы (глубокий чёрный или воздушный светлый) с настраиваемой непрозрачностью
        byte a = (byte)Math.Clamp(s.UiOpacity * 255, 0, 255);
        var bc = ThemeService.BaseColor;
        Body.Fill = new SolidColorBrush(Color.FromArgb(a, bc.R, bc.G, bc.B));

        ClockSeconds.Visibility = s.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        BatteryPanel.Visibility = s.ShowBattery && _monitor.Stats.HasBattery ? Visibility.Visible : Visibility.Collapsed;

        bool game = _gameMode && s.ShowStats;
        SetStatVisible("cpu", s.ShowStats && s.StatCpu);
        SetStatVisible("ram", s.ShowStats && s.StatRam);
        SetStatVisible("gpu", s.ShowStats && s.StatGpu && _monitor.Stats.GpuAvailable);
        SetStatVisible("cput", game);
        SetStatVisible("gput", game);
        SetStatVisible("ping", game);
        SetStatVisible("loss", game);

        Topmost = s.Topmost;

        // хоткей мог измениться в настройках
        if (_hwnd != IntPtr.Zero) RegisterGlobalHotkey();

        // Окно фиксированное; пересчитываем тело (могли смениться режим, тема, ширины)
        Width = WindowW();
        Height = WindowH();
        Top = TopForMode();
        RecenterHorizontal();
        ApplyShape();
    }

    private void SetStatVisible(string key, bool visible)
    {
        if (_statItems.TryGetValue(key, out var item))
            item.Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== Статус-бар (батарея + метрики единым блоком) ====================

    private void BuildStatsStrip()
    {
        _statItems.Clear();
        AddStatItem("cpu", "CPU");
        AddStatItem("ram", "RAM");
        AddStatItem("gpu", "GPU");
        // игровой режим: температуры и сеть
        AddStatItem("cput", "CPU");
        AddStatItem("gput", "GPU");
        AddStatItem("ping", "Ping");
        AddStatItem("loss", "Loss");
    }

    private void AddStatItem(string key, string label)
    {
        var fill = new Border
        {
            Height = 3, CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0
        };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var barWrap = new Border
        {
            Width = 34, Height = 3, CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = fill
        };

        var value = new TextBlock
        {
            FontSize = 9, Text = $"{label} —",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecBrush")
        };

        var root = new StackPanel { Margin = new Thickness(6, 0, 0, 0), Width = 40 };
        root.Children.Add(value);
        root.Children.Add(barWrap);

        StatsPanel.Children.Add(root);
        _statItems[key] = (root, fill, value, label);
    }

    private void OnStats(SystemStats st)
    {
        UpdateStat("cpu", st.Cpu);
        UpdateStat("ram", st.Ram);
        UpdateStat("gpu", st.Gpu);

        // Игровые метрики: температуры и сеть
        UpdateStatText("cput", st.CpuTemp >= 0 ? $"CPU {st.CpuTemp:0}°" : "CPU —", st.CpuTemp);
        UpdateStatText("gput", st.GpuTemp >= 0 ? $"GPU {st.GpuTemp:0}°" : "GPU —", st.GpuTemp);
        UpdateStatText("ping", st.Ping >= 0 ? $"{st.Ping:0} мс" : "Ping —", st.Ping / 1.5);
        UpdateStatText("loss", $"{st.PingLoss:0}% loss", st.PingLoss);

        var s = SettingsService.Current;
        BatteryPanel.Visibility = s.ShowBattery && st.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (st.HasBattery)
        {
            string key = st.BatteryCharging ? "ic_battery_charging"
                : st.BatteryPercent < 0 ? "ic_battery_full"
                : st.BatteryPercent <= 20 ? "ic_battery_low"
                : st.BatteryPercent <= 60 ? "ic_battery_medium"
                : "ic_battery_full";
            if (Application.Current.FindResource(key) is Geometry g)
                BatteryIcon.Data = g;
            BatteryIcon.Fill = st.BatteryCharging
                ? (Brush)Application.Current.FindResource("GreenBrush")
                : (Brush)FindResource("TextSecBrush");
            BatteryText.Text = st.BatteryPercent >= 0 ? $"{st.BatteryPercent}%" : "—";
        }

        bool gpuVisible = s.ShowStats && s.StatGpu && st.GpuAvailable;
        if (_statItems.TryGetValue("gpu", out var gpuItem) &&
            (gpuItem.Root.Visibility == Visibility.Visible) != gpuVisible)
            gpuItem.Root.Visibility = gpuVisible ? Visibility.Visible : Visibility.Collapsed;

        // ширина тела могла измениться (появление/пропадание GPU, включение статистики)
        if (!_expanded && !_animating) ApplyShape();
    }

    private void UpdateStat(string key, double pct)
    {
        if (!_statItems.TryGetValue(key, out var item)) return;
        item.Fill.Width = 34.0 * Math.Clamp(pct, 0, 100) / 100.0;
        item.Value.Text = $"{item.Label} {(int)Math.Round(pct)}%";
    }

    private void UpdateStatText(string key, string text, double barPct)
    {
        if (!_statItems.TryGetValue(key, out var item)) return;
        item.Value.Text = text;
        item.Fill.Width = 34.0 * Math.Clamp(barPct, 0, 100) / 100.0;
    }

    // ==================== Часы + активность ====================

    private void TickClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        ClockSeconds.Text = now.ToString(":ss");

        // детект бездействия → глаза засыпают; движение → пробуждение с «поиском» курсора
        GetCursorPos(out var pt);
        var cursor = new Point(pt.X, pt.Y);
        if (Math.Abs(cursor.X - _lastCursor.X) > 2 || Math.Abs(cursor.Y - _lastCursor.Y) > 2)
        {
            _lastCursor = cursor;
            if ((DateTime.Now - _lastActivity).TotalSeconds > 45)
            {
                EyesCollapsed.WakeUp();
            }
            _lastActivity = DateTime.Now;
        }
        else if ((DateTime.Now - _lastActivity).TotalSeconds > 45
                 && EyesCollapsed.CurrentEmotion != EyeEmotion.Sleepy)
        {
            EyesCollapsed.SetEmotion(EyeEmotion.Sleepy);
        }
    }

    // ==================== Игровой режим ====================

    /// <summary>В играх (полноэкранное окно поверх) — тонкий вырез с температурой и сетью.</summary>
    private void CheckGameMode()
    {
        bool game = SettingsService.Current.GameMode && IsFullscreenAppActive();
        if (game == _gameMode) return;
        _gameMode = game;
        if (_expanded && !_animating) Collapse();
        ApplySettings();
    }

    private bool IsFullscreenAppActive()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _hwnd) return false;

        // своё же окно (настройки и т.п.) — не игра
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return false;

        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
            return false;

        if (!GetWindowRect(hwnd, out var r)) return false;
        if (r.Right - r.Left < 800 || r.Bottom - r.Top < 500) return false;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top
               && r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
    }

    // ==================== Разворачивание / сворачивание ====================

    public void Expand(string? panel = null)
    {
        if (_expanded || _gameMode) return;
        _expanded = true;
        _hoverCollapseTimer.Stop();

        ShowPanel(panel ?? "home");
        AnimateProgress(1, opening: true);
    }

    public void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _hoverExpandTimer.Stop();

        AnimateProgress(0, opening: false);
    }

    /// <summary>
    /// Одна анимация прогресса по гладкой кривой Безье (как cubic-bezier в CSS).
    /// Прерывание на середине разворачивает/сворачивает от текущего значения —
    /// без скачков и блокировок.
    /// </summary>
    private void AnimateProgress(double target, bool opening)
    {
        var animation = new DoubleAnimation
        {
            From = Progress,
            To = target,
            Duration = TimeSpan.FromMilliseconds(opening ? 380 : 240),
            // раскрытие — плавный разгон и мягкая посадка (iOS-подобная кривая);
            // закрытие — равномерное ускорение вниз без перелёта
            EasingFunction = opening
                ? new CubicBezierEase { X1 = 0.32, Y1 = 0.72, X2 = 0, Y2 = 1 }
                : new CubicBezierEase { X1 = 0.55, Y1 = 0, X2 = 0.55, Y2 = 0.2 }
        };
        BeginAnimation(ProgressProperty, animation);
    }

    // ==================== Панели ====================

    private void ShowPanel(string key)
    {
        if (!_panels.TryGetValue(key, out var panel))
        {
            panel = key switch
            {
                "home" => new HomePanel(),
                "player" => new PlayerPanel(),
                "weather" => new WeatherPanel(),
                "apps" => new LauncherPanel(),
                "ai" => new AiChatPanel(),
                "control" => new ControlPanel(),
                _ => new HomePanel()
            };
            _panels[key] = panel;
        }
        PanelHost.Content = panel;

        _navGuard = true;
        foreach (var t in new[] { NavHome, NavPlayer, NavWeather, NavApps, NavAi, NavControl })
            t.IsChecked = (string)t.Tag == key;
        _navGuard = false;

        MoveNavPill(animate: true);
    }

    /// <summary>Аэро-плитка навбара: плавно переезжает и растягивается к активной вкладке.</summary>
    private void MoveNavPill(bool animate)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            var active = new[] { NavHome, NavPlayer, NavWeather, NavApps, NavAi, NavControl }
                .FirstOrDefault(t => t.IsChecked == true);
            if (active == null || active.ActualWidth <= 0)
            {
                NavPill.Width = 0;
                return;
            }
            var pos = active.TransformToAncestor(NavBar).Transform(new Point(0, 0));
            double w = active.ActualWidth;

            NavPill.BeginAnimation(WidthProperty, null);
            NavPillMove.BeginAnimation(TranslateTransform.XProperty, null);
            if (animate && NavPill.Width > 0)
            {
                NavPill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                NavPillMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(pos.X, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                NavPill.Width = w;
                NavPillMove.X = pos.X;
            }
        }));
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_navGuard || sender is not ToggleButton tb) return;
        ShowPanel((string)tb.Tag);
    }

    private void Nav_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_navGuard && sender is ToggleButton tb && (string)tb.Tag == CurrentPanelKey())
            tb.IsChecked = true;
    }

    private string CurrentPanelKey()
    {
        foreach (var kv in _panels)
            if (ReferenceEquals(kv.Value, PanelHost.Content)) return kv.Key;
        return "home";
    }

    private void SwitchTab(int delta)
    {
        int i = Array.IndexOf(TabOrder, CurrentPanelKey());
        int next = Math.Clamp(i + delta, 0, TabOrder.Length - 1);
        if (next != i) ShowPanel(TabOrder[next]);
    }

    // ==================== Мышь: наведение ====================

    // WPF MouseEnter/Leave на прозрачном layered-окне «флапает» при движении
    // курсора внутри островка, поэтому наведение отслеживается опросом позиции
    // курсора и сравнением с живым прямоугольником тела (см. _hoverPoll в конструкторе).

    private Rect _bodyRect;   // прямоугольник тела в координатах экрана (обновляет ApplyShape)

    private void HoverPoll()
    {
        GetCursorPos(out var pt);
        bool inside = _bodyRect.Contains(pt.X, pt.Y);
        if (inside)
        {
            _hoverCollapseTimer.Stop();
            // прерывание сворачивания на середине — Expand пойдёт от текущего прогресса
            if (!_expanded && !_gameMode && !_hoverExpandTimer.IsEnabled)
                _hoverExpandTimer.Start();
        }
        else
        {
            _hoverExpandTimer.Stop();
            if (_expanded && IslandMenu.IsOpen != true && !_hoverCollapseTimer.IsEnabled)
                _hoverCollapseTimer.Start();
        }
    }

    // ==================== Жесты ====================

    private Point _pressStart;
    private bool _pressing;        // тянем свёрнутый островок вниз
    private bool _swipeTracking;   // жест смахивания по развёрнутой панели
    private bool _swipeOnButton;

    /// <summary>Потянуть островок вниз — «резиновое» растяжение, отпустить — раскрыть.</summary>
    private void Island_Press(object sender, MouseButtonEventArgs e)
    {
        if (_expanded || _animating || _gameMode) return;
        _pressing = true;
        _pressStart = e.GetPosition(this);
        CollapsedView.CaptureMouse();
    }

    private void Island_Drag(object sender, MouseEventArgs e)
    {
        if (!_pressing) return;
        double dy = e.GetPosition(this).Y - _pressStart.Y;
        // резиновое сопротивление: чем дальше, тем неохотнее
        ContentHostMove.Y = dy > 0 ? Rubber(dy) : 0;
    }

    private void Island_Release(object sender, MouseButtonEventArgs e)
    {
        if (!_pressing) return;
        _pressing = false;
        CollapsedView.ReleaseMouseCapture();

        var pos = e.GetPosition(this);
        double dy = pos.Y - _pressStart.Y;
        double dx = pos.X - _pressStart.X;
        bool isClick = Math.Abs(dx) < 8 && Math.Abs(dy) < 8;

        // пружиним содержимое обратно
        ContentHostMove.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            });

        if (!_gameMode && (isClick || dy > 40)) Expand();
    }

    /// <summary>«Резина»: асимптотическое растяжение до ~12 px.</summary>
    private static double Rubber(double d) => 12.0 * (1.0 - Math.Exp(-d / 55.0));

    private void Window_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded || _animating) return;
        _swipeTracking = true;
        _pressStart = e.GetPosition(this);
        _swipeOnButton = e.OriginalSource is DependencyObject src && FindParent<ButtonBase>(src) != null;
    }

    private void Window_PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (!_swipeTracking) return;
        _swipeTracking = false;
        if (!_expanded || _swipeOnButton) return;
        double dx = e.GetPosition(this).X - _pressStart.X;
        if (dx <= -70) SwitchTab(+1);
        else if (dx >= 70) SwitchTab(-1);
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

    // ==================== Трей ====================

    private void OnTrayCommand(string command)
    {
        if (command == "toggle")
        {
            if (_expanded) Collapse(); else if (!_gameMode) Expand();
        }
        else if (command == "stats")
        {
            SettingsService.Current.ShowStats = !SettingsService.Current.ShowStats;
            SettingsService.NotifyChanged();
        }
        else if (command.StartsWith("theme:"))
        {
            SettingsService.Current.Theme = command[6..];
            ThemeService.Apply();
            SettingsService.NotifyChanged();
        }
        else if (command == "exit")
        {
            Close();
        }
    }

    // ==================== События островка ====================

    // Окно настроек (отдельное, в стиле macOS-конфигуратора)
    private SettingsWindow? _settingsWindow;

    public void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Menu_Expand(object sender, RoutedEventArgs e) { if (!_gameMode) Expand(); }
    private void Menu_Settings(object sender, RoutedEventArgs e) => OpenSettings();
    private void Menu_Exit(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _expanded) Collapse();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (SettingsService.Current.CollapseOnFocusLoss && _expanded && !_animating)
            Collapse();
    }

    // ==================== Интеграция с Windows ====================

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
    }

    private IntPtr _hwnd;
    private string? _registeredHotkey;
    private bool _hookAdded;

    private void RegisterGlobalHotkey()
    {
        var s = SettingsService.Current.Hotkey;
        if (s == _registeredHotkey) return;

        if (_registeredHotkey != null)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registeredHotkey = null;
        }
        if (TryParseHotkey(s, out var mods, out var vk) &&
            RegisterHotKey(_hwnd, HOTKEY_ID, mods | MOD_NOREPEAT, vk))
        {
            _registeredHotkey = s;
        }
    }

    /// <summary>Разбирает строку вида "Ctrl+Shift+Space" в модификаторы и виртуальную клавишу.</summary>
    private static bool TryParseHotkey(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "win": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], true, out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            if (_expanded) Collapse(); else if (!_gameMode) Expand();
            handled = true;
        }
        if (msg == TrayService.WM_TRAY && _tray != null)
        {
            _tray.HandleMessage(lParam);
        }
        // Защита геометрии: пока не идёт наша анимация, любой SetWindowPos,
        // не совпадающий с целевым размером/позицией окна, отменяется.
        if (msg == WM_WINDOWPOSCHANGING && !_animating)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            var src = PresentationSource.FromVisual(this) as HwndSource;
            var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int wantCx = (int)Math.Round(WindowW() * m.M11);
            int wantCy = (int)Math.Round(WindowH() * m.M22);
            int wantX = (int)Math.Round(Left * m.M11);
            int wantY = (int)Math.Round(Top * m.M22);

            bool badSize = (pos.flags & SWP_NOSIZE) == 0
                           && (Math.Abs(pos.cx - wantCx) > 2 || Math.Abs(pos.cy - wantCy) > 2);
            bool badMove = !double.IsNaN(Left) && !double.IsNaN(Top)
                           && (pos.flags & SWP_NOMOVE) == 0
                           && (Math.Abs(pos.x - wantX) > 2 || Math.Abs(pos.y - wantY) > 2);
            if (badSize || badMove)
            {
                pos.flags |= SWP_NOSIZE | SWP_NOMOVE;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    private const int HOTKEY_ID = 9001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy, flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
