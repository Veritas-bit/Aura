using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Models;
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
    private readonly DispatcherTimer _equalizerTimer;
    private readonly DispatcherTimer _marqueeTimer;
    private readonly DispatcherTimer _transientTimer;
    private DateTimeOffset _transientDeadline;
    private string _brightnessDiagnosis = "";
    private readonly DispatcherTimer _volumePollTimer;
    private readonly DispatcherTimer _brightnessPollTimer;
    private readonly DispatcherTimer _compactMorphTimer;
    private bool _compactMorphing;
    private double _compactTargetWidth;
    private double _compactTargetHeight;
    private bool _brightnessPolling;
    private double _lastBrightness = -1;
    private double _displayVolumePercent;
    private double _displayBrightnessPercent;
    private readonly MediaSessionService _media = new();
    private readonly BrightnessPowerEventService _brightnessPower = new();
    private DateTimeOffset _brightnessPowerRegisteredAt;
    private readonly AudioVolumeMonitorService _volumeMonitor = new();
    private readonly NotificationListenerService _notificationListener = new();
    private readonly AudioSpectrumService _audioSpectrum = new();
    private readonly List<Rectangle> _equalizerBars = new();
    private readonly Dictionary<string, UserControl> _panels = new();
    private readonly Dictionary<string, (StackPanel Root, Border Fill, TextBlock Value, string Label)> _statItems = new();
    private readonly TranslateTransform _panelTransition = new();
    private int _panelTransitionVersion;
    private bool _expanded;
    private bool _animating;
    private bool _navGuard;
    private bool _gameMode;
    private MediaSessionInfo _currentMedia = MediaSessionInfo.Empty;
    private float[] _audioLevels = new float[48];
    private double _marqueeOffset;
    private string _displayedTrackId = "";
    private byte[]? _displayedArtwork;
    private TrayService? _tray;
    private enum TransientCardKind { None, Track, Volume, Brightness, Notification }
    private TransientCardKind _transientKind;
    private bool _volumeEventsAttached;

    // Геометрия свёрнутого вида
    // Battery colors are fixed by charge, never tinted by the application theme.
    private static readonly Brush BatteryLowColor = new SolidColorBrush(Color.FromRgb(0xF0, 0x4B, 0x55));
    private static readonly Brush BatteryMidColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0x38));
    private static readonly Brush BatteryHighColor = new SolidColorBrush(Color.FromRgb(0x47, 0xD1, 0x7D));

    private const double CollapsedH = 46;
    private const double GameH = 30;
    private const double BodyGap = 14;
    private const double CollapsedBaseW = 172;
    private const double StatItemW = 64;           // ширина одного элемента статус-бара с отступом
    private const double NotchShoulder = 14;       // ширина «плечика» выреза
    private const double IslandMarginH = 8;        // боковые поля фона островка
    private const double IslandMarginV = 20;       // суммарные вертикальные поля (8+12)
    private const double ExpandedContentWBase = 764;

    private static readonly string[] TabOrder =
        { "home", "player", "weather", "apps", "ai", "control" };

    public MainWindow()
    {
        InitializeComponent();
        PanelHost.RenderTransform = _panelTransition;

        TrackTextViewport.SizeChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateTrackMarquee));
        VolumeBarTrack.SizeChanged += (_, _) => UpdateVolumeBar();
        BrightnessBarTrack.SizeChanged += (_, _) => UpdateBrightnessBar();
        TrackCardProgressTrack.SizeChanged += (_, _) => UpdateTrackCardTimeline();
        Width = WindowW();
        Height = WindowH();
        BuildStatsStrip();
        BuildTrackEqualizer();
        _media.Changed += OnMediaChanged;
        _audioSpectrum.LevelsUpdated += OnAudioLevelsUpdated;
        _audioSpectrum.ErrorOccurred += OnAudioSpectrumError;
        _ = _media.StartAsync();
        _equalizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _equalizerTimer.Tick += (_, _) => AnimateTrackEqualizer();
        _equalizerTimer.Start();
        _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _marqueeTimer.Tick += (_, _) => AdvanceTrackMarquee();
        // Poll the deadline rather than rearming a one-shot per media timeline callback.
        _transientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _transientTimer.Tick += (_, _) =>
        {
            if (_transientKind != TransientCardKind.None &&
                DateTimeOffset.UtcNow >= _transientDeadline)
                HideTransientCard();
        };
        _compactMorphTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _compactMorphTimer.Tick += (_, _) => FinishCompactMorph();
        _volumeMonitor.VolumeChanged += OnSystemVolumeChanged;
        _volumeMonitor.OutputDeviceChanged += OnOutputDeviceChanged;
        _volumeEventsAttached = true;
        // Audio endpoint change detection only; volume changes use CoreAudio callbacks.
        _volumePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _volumePollTimer.Tick += (_, _) => _volumeMonitor.RefreshDefaultDevice();
        _volumePollTimer.Start();
        // WMI brightness is probed off the UI thread; unsupported external monitors
        // never trigger a brightness transient.
        _brightnessPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _brightnessPollTimer.Tick += async (_, _) => await PollBrightnessAsync();
        _brightnessPollTimer.Start();
        _notificationListener.NotificationReceived += OnDesktopNotificationReceived;

        // Исходная форма тела и позиция окна до первого кадра
        Top = TopForMode();
        RecenterHorizontal();
        ApplyShape();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += (_, _) =>
        {
            TickClock();
            UpdateTrackCardTimeline();
        };
        _clockTimer.Start();

        // Наведение мыши: развернуть; убирание — свернуть (с задержками).
        // Отслеживание — опросом (WPF-события на layered-окне нестабильны).
        _hoverExpandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverExpandTimer.Tick += (_, _) =>
        {
            _hoverExpandTimer.Stop();
            if (_transientKind == TransientCardKind.Track)
                HideTransientCard();
            Expand(ShowTrack ? "player" : "home");
        };
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
        WeatherService.Updated += OnWeatherUpdated;
        SettingsService.Changed += ApplySettings;

        // после первой раскладки — пересчитать тело по замеренной ширине контента
        Loaded += async (_, _) =>
        {
            UpdateCollapsedWidth(animate: false);
            UpdateTrackDisplay();
            ApplyShape();
            await InitializeNotificationsAsync();
        };

        SizeChanged += (_, _) =>
        {
            if (!_animating) RecenterHorizontal();
        };
        ExpandedView.SizeChanged += (_, _) => { if (!_animating) MoveNavPill(animate: false); };

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
            _brightnessPower.BrightnessChanged += OnWindowsBrightnessChanged;
            _brightnessPowerRegisteredAt = DateTimeOffset.UtcNow;
            _brightnessPower.Register(_hwnd);

            // Иконка в трее: тема, статус-бар, выход
            _tray = new TrayService(_hwnd);
            _tray.Command += OnTrayCommand;
            _tray.UpdateIcon(CurrentAccent());

            ApplySettings();
            RecenterHorizontal();
        };

        Closed += (_, _) =>
        {
            CompanionEyes.Dispose();
            _tray?.Dispose();
            _monitor.Dispose();
            _equalizerTimer.Stop();
            _marqueeTimer.Stop();
            _transientTimer.Stop();
            _compactMorphTimer.Stop();
            _volumePollTimer.Stop();
            _brightnessPollTimer.Stop();
            _brightnessPower.BrightnessChanged -= OnWindowsBrightnessChanged;
            _brightnessPower.Dispose();
            if (_volumeEventsAttached)
            {
                _volumeMonitor.VolumeChanged -= OnSystemVolumeChanged;
                _volumeMonitor.OutputDeviceChanged -= OnOutputDeviceChanged;
            }
            _volumeMonitor.Dispose();
            _notificationListener.NotificationReceived -= OnDesktopNotificationReceived;
            _notificationListener.Dispose();
            WeatherService.Updated -= OnWeatherUpdated;
            _audioSpectrum.LevelsUpdated -= OnAudioLevelsUpdated;
            _audioSpectrum.ErrorOccurred -= OnAudioSpectrumError;
            _audioSpectrum.Dispose();
            _media.Changed -= OnMediaChanged;
            _media.Dispose();
        };
    }

    // ==================== Геометрия ====================

    private void RecenterHorizontal()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + wa.Width * 0.5 - Width * 0.5;
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

    private bool ShowTrack => !_gameMode && _currentMedia.IsPlaying && _currentMedia.HasTrack;
    private bool ShowPinnedTrackCard => ShowTrack && SettingsService.Current.ExpandedTrackIsland;
    private int TrackDisplayMode =>
        AppSettings.NormalizeTrackDisplayMode(SettingsService.Current.CollapsedTrackDisplayMode);
    // All media sessions use the single 325x24 Figma mini-player. Legacy cover/equalizer
    // settings no longer affect the six compact visual states.
    private bool ShowTrackCover => false;
    private bool ShowTrackEqualizer => false;
    private double UiScale
    {
        get
        {
            return Math.Clamp(Math.Min(SystemParameters.PrimaryScreenWidth / 1920.0,
                SystemParameters.PrimaryScreenHeight / 1080.0), 0.65, 1.3);
        }
    }
    // Figma dimensions are WPF device-independent pixels, not content + padding.
    private (double Width, double Height) TargetCollapsedSize()
    {
        if (_gameMode) return (CollapsedBaseW + 3 * StatItemW, GameH * UiScale);
        return _transientKind switch
        {
            TransientCardKind.Volume or TransientCardKind.Brightness => (325, 24),
            TransientCardKind.Track => (325, 80),
            TransientCardKind.Notification => (325, 80),
            _ when ShowPinnedTrackCard => (325, 80),
            _ when ShowTrack => (325, 24),
            _ when SettingsService.Current.ShowClock && SettingsService.Current.ShowSeconds
                => (251, 46),
            _ => (235, 46)
        };
    }

    private double CollapsedHeight => CompactHeight > 0 ? CompactHeight : TargetCollapsedSize().Height;
    private double CollapsedContentInset => 0;
    private double ExpandedContentW => Math.Min(ExpandedContentWBase * UiScale, SystemParameters.WorkArea.Width * 0.84);

    /// <summary>Ширина содержимого: замер реального контента.
    /// До первой раскладки — оценка: базовые 172 плюс элементы статус-бара.</summary>
    private double CollapsedContentW() =>
        CollapsedWidth > 0 ? CollapsedWidth : TargetCollapsedSize().Width;

    private double ExpandedContentH()
    {
        var wa = SystemParameters.WorkArea;
        return Math.Min(620 * UiScale, wa.Height * 0.84);
    }

    // Окно ВСЕГДА фиксированного размера — анимируется фигура тела внутри него,
    // а не само окно. Благодаря этому пружина идёт в render-потоке без дёрганья.
    private double WindowW() => ExpandedContentW + (2 * NotchShoulder + 16) * UiScale;
    private double WindowH() => ExpandedContentH() + IslandMarginV * UiScale;

    private double TopForMode() =>
        SystemParameters.WorkArea.Top + (IsNotch ? 0 : 2 * UiScale);

    // ==================== Прогресс и форма тела ====================

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).ApplyShape()));

    private static readonly DependencyProperty CollapsedWidthProperty = DependencyProperty.Register(
        nameof(CollapsedWidth), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).ApplyShape()));

    private double CollapsedWidth
    {
        get => (double)GetValue(CollapsedWidthProperty);
        set => SetValue(CollapsedWidthProperty, value);
    }

    private static readonly DependencyProperty CompactHeightProperty = DependencyProperty.Register(
        nameof(CompactHeight), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).ApplyShape()));

    private double CompactHeight
    {
        get => (double)GetValue(CompactHeightProperty);
        set => SetValue(CompactHeightProperty, value);
    }

    /// <summary>0 = свёрнутый островок, 1 = развёрнутая панель.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double SmoothStep(double value) => value * value * (3 - 2 * value);

    /// <summary>Пересчёт фигуры тела и видимости контента на каждом кадре прогресса.</summary>
    private void ApplyShape()
    {
        double p = Progress;
        double c = Math.Clamp(p, 0, 1);
        _animating = Math.Abs(p - (_expanded ? 1.0 : 0.0)) > 0.002;

        bool notch = IsNotch;
        double margin = (notch ? NotchShoulder : IslandMarginH) * UiScale;
        double contentW = CollapsedContentW();
        // свёрнутое тело обнимает контент ровно на BodyGap — отступы от края
        // равны отступам между элементами; margin здесь не участвует
        double collapsedInset = CollapsedContentInset;
        double collapsedW = Math.Max(1, contentW + 2 * collapsedInset);
        double collapsedH = CollapsedHeight;
        double expandedW = ExpandedContentW + 2 * margin;
        double expandedH = ExpandedContentH();

        double widthProgress = SmoothStep(Math.Clamp((c - 0.14) / 0.86, 0, 1));
        double heightProgress = SmoothStep(Math.Clamp((c - 0.16) / 0.84, 0, 1));
        double w = Math.Max(1, Lerp(collapsedW, expandedW, widthProgress));
        double h = Math.Max(1, Lerp(collapsedH, expandedH, heightProgress));
        double x = (Width - w) / 2;
        double y = notch ? 0 : 8 * UiScale;

        if (notch)
        {
            double r = Lerp(_gameMode ? 11 : 15, 22, c) * UiScale;
            Body.Data = NotchGeometry(w, h, r, NotchShoulder * UiScale);
        }
        else
        {
            // Compact 24/46 DIP views are pills. The 80 DIP cards are rounded
            // rectangles (24 DIP corners), not oversized 40 DIP capsules.
            double r = Lerp(Math.Min(24, collapsedH / 2), 24 * UiScale, c);
            Body.Data = RoundedRect(w, h, Math.Min(r, h / 2));
        }
        Body.Margin = new Thickness(x, y, 0, 0);
        _bodyRect = new Rect(Left + x, Top + y, w, h);

        CollapsedView.Margin = new Thickness(x + collapsedInset, y, 0, 0);
        CollapsedView.Width = Math.Max(1, contentW);
        CollapsedView.Height = collapsedH;
        // Prevent artwork/text from painting outside the rounded black Body.
        CollapsedView.Clip = Body.Data.Clone();
        CollapsedTrackView.Clip = null;
        // Clear the old layout first, morph the surface, then reveal the new layout.
        CollapsedView.Opacity = _compactMorphing ? 0
            : SmoothStep(Math.Clamp((0.16 - c) / 0.16, 0, 1));
        CollapsedView.IsHitTestVisible = !_expanded && !_compactMorphing && c < 0.1;

        ExpandedView.Margin = new Thickness(x + margin + 4 * UiScale, y + 2 * UiScale, 0, 0);
        ExpandedView.Width = ExpandedContentW - 8 * UiScale;
        ExpandedView.Height = expandedH - 2 * UiScale;
        ExpandedView.Opacity = SmoothStep(Math.Clamp((c - 0.82) / 0.16, 0, 1));
        ExpandedView.IsHitTestVisible = _expanded && ExpandedView.Opacity > 0.95;
    }

    private static Geometry RoundedRect(double w, double h, double r) =>
        new RectangleGeometry(new Rect(0, 0, w, h), r, r);

    /// <summary>Фигура выреза: «плечики» — выпуклые дуги у верхнего края экрана.</summary>
    private static Geometry NotchGeometry(double w, double h, double r, double shoulder)
    {
        double s = shoulder;
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

        _tray?.UpdateIcon(accent);

        // Мягкая тень подчёркивает форму островка без лишнего свечения.
        if (Body.Effect is DropShadowEffect shadow)
        {
            shadow.Color = accent;
            shadow.Opacity = s.GlowEnabled ? 0.35 : 0;
        }

        // Поверхность остаётся полностью непрозрачной для строгого чёрно-белого контраста.
        var bc = ThemeService.BaseColor;
        Body.Fill = new SolidColorBrush(bc);
        UpdateEqualizerColors();

        CollapsedClockBlock.Visibility = s.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Visibility = s.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ClockSecondsBlock.Visibility = s.ShowClock && s.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        BatteryPanel.Visibility = s.ShowBattery && _monitor.Stats.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        s.CollapsedTrackDisplayMode = AppSettings.NormalizeTrackDisplayMode(s.CollapsedTrackDisplayMode);
        // If the user selected a permanent card while its preview was showing,
        // clear the temporary timer without breaking playback.
        if (s.ExpandedTrackIsland && _transientKind == TransientCardKind.Track)
        {
            _transientTimer.Stop();
            _transientDeadline = DateTimeOffset.MinValue;
            _transientKind = TransientCardKind.None;
        }
        UpdateTrackDisplay();
        UpdateCollapsedWeather();
        UpdateAudioAnalysis();

        bool game = _gameMode && s.ShowStats;
        SetStatVisible("cpu", game && s.StatCpu);
        SetStatVisible("ram", game && s.StatRam);
        SetStatVisible("gpu", game && s.StatGpu && _monitor.Stats.GpuAvailable);
        SetStatVisible("cput", game);
        SetStatVisible("gput", game);
        SetStatVisible("ping", game);
        SetStatVisible("loss", game);
        UpdateStatsPanelVisibility();
        UpdateCollapsedWidth(animate: !_expanded && !_animating);

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

    private void UpdateCollapsedWidth(bool animate)
    {
        var target = TargetCollapsedSize();
        if (Math.Abs(_compactTargetWidth - target.Width) < 0.01 &&
            Math.Abs(_compactTargetHeight - target.Height) < 0.01)
            return; // Never restart an in-flight morph for the same size.

        _compactTargetWidth = target.Width;
        _compactTargetHeight = target.Height;
        CollapsedStack.Width = target.Width;

        double currentW = CollapsedWidth > 0 ? CollapsedWidth : target.Width;
        double currentH = CompactHeight > 0 ? CompactHeight : target.Height;
        bool dimensionsChanged =
            Math.Abs(currentW - target.Width) > 0.25 ||
            Math.Abs(currentH - target.Height) > 0.25;

        _compactMorphTimer.Stop();
        if (!animate || !IsLoaded || _expanded || !dimensionsChanged)
        {
            _compactMorphing = false;
            BeginAnimation(CollapsedWidthProperty, null);
            BeginAnimation(CompactHeightProperty, null);
            CollapsedWidth = target.Width;
            CompactHeight = target.Height;
            ApplyShape();
            return;
        }

        // Keep every compact view hidden for the entire Body morph.
        // The incoming view was already selected, but it cannot paint until
        // both geometry animations have fully reached their final values.
        _compactMorphing = true;
        CollapsedView.BeginAnimation(UIElement.OpacityProperty, null);
        ApplyShape();

        const int morphMilliseconds = 225;
        var easing = new CubicBezierEase { X1 = 0.2, Y1 = 0.7, X2 = 0.2, Y2 = 1 };
        if (Math.Abs(currentW - target.Width) > 0.25)
            BeginAnimation(CollapsedWidthProperty, new DoubleAnimation(
                currentW, target.Width, TimeSpan.FromMilliseconds(morphMilliseconds))
            { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd });

        if (Math.Abs(currentH - target.Height) > 0.25)
            BeginAnimation(CompactHeightProperty, new DoubleAnimation(
                currentH, target.Height, TimeSpan.FromMilliseconds(morphMilliseconds))
            { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd });

        // One render frame of safety margin after the geometry has finished.
        _compactMorphTimer.Interval = TimeSpan.FromMilliseconds(morphMilliseconds + 25);
        _compactMorphTimer.Start();
    }

    private void FinishCompactMorph()
    {
        _compactMorphTimer.Stop();

        // Set the base DP values before removing HoldEnd animations so the
        // surface never briefly jumps back to its previous dimensions.
        CollapsedWidth = _compactTargetWidth;
        CompactHeight = _compactTargetHeight;
        BeginAnimation(CollapsedWidthProperty, null);
        BeginAnimation(CompactHeightProperty, null);

        _compactMorphing = false;
        ApplyShape();

        if (!_expanded)
        {
            CollapsedView.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(115))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
        }
    }

    private void OnWeatherUpdated() => Dispatcher.BeginInvoke(
        DispatcherPriority.DataBind, new Action(() =>
        {
            UpdateCollapsedWeather();
            if (!_currentMedia.IsPlaying || !_currentMedia.HasTrack)
                UpdateCollapsedWidth(animate: !_expanded && !_animating);
        }));

    private void UpdateCollapsedWeather()
    {
        var weather = WeatherService.Info;
        bool show = _gameMode && SettingsService.Current.ShowWeatherInIsland && weather.Available;
        CollapsedWeatherPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        string icon = WeatherService.Describe(weather.Code).Icon;
        if (TryFindResource(icon) is Geometry geometry)
            CollapsedWeatherIcon.Data = geometry;
        CollapsedWeatherText.Text = $"{Math.Round(weather.Temp):0}°";
        CollapsedWeatherPanel.ToolTip = $"{weather.City}: {WeatherService.Describe(weather.Code).Text}";
    }

    private void UpdateAudioAnalysis()
    {
        if (ShowTrackEqualizer)
        {
            TrackEqualizer.ToolTip = null;
            _audioSpectrum.Start();
        }
        else
            _audioSpectrum.Stop();
    }

    private void OnAudioLevelsUpdated(float[] levels) => Dispatcher.BeginInvoke(
        DispatcherPriority.Render, new Action(() => _audioLevels = levels));

    private void OnAudioSpectrumError(string message) => Dispatcher.BeginInvoke(
        DispatcherPriority.Normal, new Action(() => TrackEqualizer.ToolTip = message));

    private void SetStatVisible(string key, bool visible)
    {
        if (_statItems.TryGetValue(key, out var item))
            item.Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatsPanelVisibility()
    {
        StatsPanel.Visibility = BatteryPanel.Visibility == Visibility.Visible ||
                                _statItems.Values.Any(item => item.Root.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
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
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = fill
        };
        barWrap.SetResourceReference(Border.BackgroundProperty, "OverlayFaintBrush");

        var value = new TextBlock
        {
            Width = 52,
            FontSize = 9, Text = $"{label} —",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecBrush")
        };

        var root = new StackPanel { Margin = new Thickness(6, 0, 0, 0), Width = 58 };
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
            Brush batteryColor = st.BatteryPercent <= 20 ? BatteryLowColor
                : st.BatteryPercent <= 80 ? BatteryMidColor : BatteryHighColor;
            BatteryIcon.Fill = batteryColor;
            BatteryText.Foreground = (Brush)FindResource("TextPriBrush");
            TrackCardBatteryIcon.Fill = batteryColor;
            TrackCardBatteryPercent.Foreground = (Brush)FindResource("TextPriBrush");
            TrackCardBatteryPercent.Text = st.BatteryPercent >= 0 ? $"{st.BatteryPercent}%" : "";
            BatteryText.Text = st.BatteryPercent >= 0 ? $"{st.BatteryPercent}%" : "—";
        }

        bool gpuVisible = _gameMode && s.ShowStats && s.StatGpu && st.GpuAvailable;
        if (_statItems.TryGetValue("gpu", out var gpuItem) &&
            (gpuItem.Root.Visibility == Visibility.Visible) != gpuVisible)
            gpuItem.Root.Visibility = gpuVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatsPanelVisibility();

        // The fixed-size compact layouts do not depend on periodic CPU/GPU polls.
        if (_gameMode)
            UpdateCollapsedWidth(animate: !_expanded && !_animating);
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
        ClockSeconds.Text = now.ToString("ss");

    }

    private void BuildTrackEqualizer()
    {
        for (int i = 0; i < 48; i++)
        {
            var bar = new Rectangle
            {
                Height = 2,
                RadiusX = 1,
                RadiusY = 1
            };
            _equalizerBars.Add(bar);
            TrackEqualizer.Children.Add(bar);
        }
        UpdateEqualizerColors();
    }

    private void UpdateEqualizerColors()
    {
        Brush[] palette =
        {
            (Brush)FindResource("AccentBrush"),
            (Brush)FindResource("AccentSecondaryBrush"),
            (Brush)FindResource("AccentTertiaryBrush")
        };
        for (int i = 0; i < _equalizerBars.Count; i++)
            _equalizerBars[i].Fill = palette[i % palette.Length];
    }

    private void AnimateTrackEqualizer()
    {
        if (!ShowTrackEqualizer || TrackEqualizer.ActualWidth <= 0)
        {
            TrackEqualizer.Visibility = Visibility.Collapsed;
            return;
        }

        TrackEqualizer.Visibility = Visibility.Visible;
        double canvasWidth = TrackEqualizer.ActualWidth;
        double canvasHeight = TrackEqualizer.ActualHeight;
        if (canvasHeight <= 0) return;
        double slotWidth = canvasWidth / _equalizerBars.Count;
        for (int i = 0; i < _equalizerBars.Count; i++)
        {
            double height = Math.Clamp(_audioLevels[i] * canvasHeight, 1, canvasHeight);
            var bar = _equalizerBars[i];
            bar.Width = Math.Max(0, slotWidth - 1);
            bar.Height = height;
            Canvas.SetLeft(bar, i * slotWidth + 1);
            Canvas.SetTop(bar, Math.Max(0, canvasHeight - bar.Height));
        }
    }

    private void OnMediaChanged(MediaSessionInfo info)
    {
        bool trackChanged = !string.Equals(_displayedTrackId, info.TrackId, StringComparison.Ordinal);
        bool artworkChanged = !ReferenceEquals(_displayedArtwork, info.Artwork);
        bool wasShowingTrack = ShowTrack;
        bool wasShowingCover = ShowTrackCover;
        bool wasShowingEqualizer = ShowTrackEqualizer;
        double previousHeight = CollapsedHeight;
        _currentMedia = info;
        if (trackChanged && info.HasTrack && !_expanded && !ShowPinnedTrackCard)
        {
            // Mini mode: preview the full card for four seconds only.
            ShowTransientCard(TransientCardKind.Track, info.Title, info.Artist,
                info.AlbumTitle, info.Artwork, TimeSpan.FromSeconds(4));
        }
        else if (trackChanged || artworkChanged || wasShowingTrack != ShowTrack)
        {
            UpdateTrackDisplay();
            // A metadata update must not extend the temporary card's deadline.
            if (_transientKind == TransientCardKind.Track || ShowPinnedTrackCard)
                UpdateFullTrackCard(info);
        }
        UpdateTrackCardTimeline();

        if (trackChanged || wasShowingTrack != ShowTrack || wasShowingCover != ShowTrackCover ||
            wasShowingEqualizer != ShowTrackEqualizer || previousHeight != CollapsedHeight)
            UpdateCollapsedWidth(animate: !_expanded && !_animating);
        UpdateAudioAnalysis();
    }

    private void UpdateTrackDisplay()
    {
        bool transient = _transientKind != TransientCardKind.None;
        bool mini = !transient && ShowTrack && !ShowPinnedTrackCard;
        bool basic = !transient && !ShowTrack;

        CollapsedStack.Visibility = basic ? Visibility.Visible : Visibility.Collapsed;
        CollapsedTrackView.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        VolumeIslandView.Visibility = _transientKind == TransientCardKind.Volume
            ? Visibility.Visible : Visibility.Collapsed;
        BrightnessIslandView.Visibility = _transientKind == TransientCardKind.Brightness
            ? Visibility.Visible : Visibility.Collapsed;
        bool trackCard = _transientKind == TransientCardKind.Track ||
            (!transient && ShowPinnedTrackCard);
        if (trackCard)
            UpdateFullTrackCard(_currentMedia);
        // Use precisely the same view for track changes and the pinned player.
        FullTrackCardView.Visibility = trackCard ? Visibility.Visible : Visibility.Collapsed;
        NotificationIslandView.Visibility = _transientKind == TransientCardKind.Notification
            ? Visibility.Visible : Visibility.Collapsed;

        string title = _currentMedia.Title;
        string artist = _currentMedia.Artist;
        string trackText = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
        SetTrackLabel(CollapsedTrackTitle, mini ? title : "", artist);
        SetTrackLabel(RepeatedTrackTitle, mini ? title : "", artist);
        CollapsedTrackView.ToolTip = mini ? trackText : null;
        _displayedTrackId = _currentMedia.TrackId;
        _displayedArtwork = _currentMedia.Artwork;
        TrackEqualizer.Visibility = Visibility.Collapsed;
        TrackCoverFrame.Visibility = Visibility.Collapsed;

        UpdateHeadphonesIndicator();
        TrackCardHeadphones.Visibility = _volumeMonitor.HeadphonesConnected
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrackCardTimeline();
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateTrackMarquee));
    }

    private void UpdateTrackCardTimeline()
    {
        if (_transientKind != TransientCardKind.Track && !ShowPinnedTrackCard) return;
        TimeSpan duration = _currentMedia.Duration;
        double total = duration.TotalSeconds;
        if (total <= 0 || double.IsNaN(total))
        {
            TrackCardPosition.Text = "00:00";
            TrackCardProgressFill.Width = 0;
            return;
        }
        double seconds = Math.Clamp(_currentMedia.Position.TotalSeconds, 0, total);
        TrackCardPosition.Text = $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
        TrackCardProgressFill.Width =
            Math.Max(0, TrackCardProgressTrack.ActualWidth * seconds / total);
    }

    private void UpdateHeadphonesIndicator()
    {
        bool connected = _volumeMonitor.HeadphonesConnected;
        HeadphoneStatus.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected) return;

        double volume = _volumeMonitor.IsMuted ? 0 : _volumeMonitor.CurrentVolumePercent;
        HeadphoneVolumeLow.Opacity = volume > 0 ? 1 : 0.2;
        HeadphoneVolumeMid.Opacity = volume >= 34 ? 1 : 0.2;
        HeadphoneVolumeHigh.Opacity = volume >= 67 ? 1 : 0.2;
    }

    private void UpdateVolumeBar()
    {
        VolumeBarFill.Width = Math.Max(0,
            VolumeBarTrack.ActualWidth * Math.Clamp(_displayVolumePercent, 0, 100) / 100);
    }

    private void UpdateBrightnessBar()
    {
        BrightnessBarFill.Width = Math.Max(0,
            BrightnessBarTrack.ActualWidth * Math.Clamp(_displayBrightnessPercent, 0, 100) / 100);
    }

    private void OnWindowsBrightnessChanged(double brightness)
    {
        // Windows supplies the actual brightness percent with this event,
        // even if WMI/DDC/CI cannot enumerate the current monitor.
        bool first = _lastBrightness < 0;
        double old = _lastBrightness;
        _lastBrightness = brightness;
        _brightnessDiagnosis = $"Windows event: {brightness:0}%";
        if (!IsLoaded || _gameMode || _expanded) return;

        // Ignore only the system's immediate baseline notification at startup.
        if (first && DateTimeOffset.UtcNow - _brightnessPowerRegisteredAt <
            TimeSpan.FromSeconds(1.5)) return;
        if (!first && Math.Abs(old - brightness) < 0.5) return;

        ShowTransientCard(TransientCardKind.Brightness, "", "", "", null,
            TimeSpan.FromSeconds(2), brightness);
    }

    private async Task PollBrightnessAsync()
    {
        if (_brightnessPolling) return;
        _brightnessPolling = true;
        try
        {
            // All WMI calls run off the dispatcher. Only built-in monitors expose
            // WmiMonitorBrightness; desktop monitors usually return unsupported.
            IntPtr hostWindow = _hwnd; // Read the window handle on the UI thread.
            var level = await Task.Run(() =>
                BrightnessMonitorService.ReadCurrentBrightness(hostWindow));
            string diagnosis = BrightnessMonitorService.LastDiagnostic;
            if (!string.Equals(diagnosis, _brightnessDiagnosis, StringComparison.Ordinal))
            {
                _brightnessDiagnosis = diagnosis;
                try
                {
                    var log = System.IO.Path.Combine(SettingsService.DataDir, "brightness.log");
                    File.AppendAllText(log, $"[{DateTime.Now:O}] {diagnosis}{Environment.NewLine}");
                }
                catch { /* logging must never interrupt the UI */ }
            }
            if (!IsLoaded || level < 0) return;

            if (_lastBrightness < 0)
            {
                _lastBrightness = level; // baseline: do not show on startup
                return;
            }
            if (Math.Abs(level - _lastBrightness) < 0.5) return;

            _lastBrightness = level;
            if (!_gameMode && !_expanded)
                ShowTransientCard(TransientCardKind.Brightness, "", "", "", null,
                    TimeSpan.FromSeconds(2), level);
        }
        catch (Exception exception)
        {
            if (_brightnessDiagnosis != exception.GetType().Name)
            {
                _brightnessDiagnosis = exception.GetType().Name;
                try
                {
                    File.AppendAllText(System.IO.Path.Combine(SettingsService.DataDir, "brightness.log"),
                        $"[{DateTime.Now:O}] Brightness error: {exception}{Environment.NewLine}");
                }
                catch { }
            }
        }
        finally
        {
            _brightnessPolling = false;
        }
    }

    private async Task InitializeNotificationsAsync()
    {
        if (!SettingsService.Current.NotificationsEnabled ||
            !NotificationListenerService.IsPackaged)
            return;

        // Windows remembers the user's decision. If the prompt was dismissed,
        // Unspecified permits asking again; Denied does not show another prompt.
        // Do not persist a separate "requested" flag that would suppress retry.
        await _notificationListener.StartAsync(requestAccess: true);
    }

    private void OnDesktopNotificationReceived(DesktopNotification notification)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            string title = string.IsNullOrWhiteSpace(notification.Title)
                ? notification.AppName : notification.Title;
            ShowTransientCard(TransientCardKind.Notification, notification.AppName,
                title, notification.Body, null, TimeSpan.FromSeconds(5));
        }));
    }

    private void OnOutputDeviceChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(OnOutputDeviceChanged));
            return;
        }
        UpdateHeadphonesIndicator();
    }

    private void OnSystemVolumeChanged(double volume, bool muted)
    {
        // The Core Audio callback arrives off the WPF dispatcher.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                new Action(() => OnSystemVolumeChanged(volume, muted)));
            return;
        }

        UpdateHeadphonesIndicator();
        if (_gameMode || !IsLoaded || _expanded) return;
        ShowTransientCard(TransientCardKind.Volume, "",
            "", "", null, TimeSpan.FromSeconds(2), muted ? 0 : volume);
    }

    private string _fullTrackCardId = "";
    private byte[]? _fullTrackCardArtwork;

    // Used by both permanent 325x80 mode and the four-second preview.
    private void UpdateFullTrackCard(MediaSessionInfo info)
    {
        FullTrackTitle.Text = string.IsNullOrWhiteSpace(info.Artist)
            ? info.Title : $"{info.Title} — {info.Artist}";
        FullTrackArtist.Text = info.Artist;
        FullTrackAlbum.Text = info.AlbumTitle;
        FullTrackAlbum.Visibility = string.IsNullOrWhiteSpace(info.AlbumTitle)
            ? Visibility.Collapsed : Visibility.Visible;
        TrackCardHeadphones.Visibility = _volumeMonitor.HeadphonesConnected
            ? Visibility.Visible : Visibility.Collapsed;

        if (!string.Equals(_fullTrackCardId, info.TrackId, StringComparison.Ordinal) ||
            !ReferenceEquals(_fullTrackCardArtwork, info.Artwork))
        {
            var bitmap = CreateTrackArtwork(info.Artwork);
            FullTrackArtwork.Source = bitmap;
            FullTrackArtwork.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
            FullTrackFallback.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
            _fullTrackCardId = info.TrackId;
            _fullTrackCardArtwork = info.Artwork;
        }
        UpdateTrackCardTimeline();
    }

    private void ShowTransientCard(TransientCardKind kind, string title, string subtitle,
        string details, byte[]? artwork, TimeSpan duration, double? progress = null)
    {
        if (_gameMode || _expanded) return;
        if (kind is TransientCardKind.Volume or TransientCardKind.Brightness &&
            _transientKind is TransientCardKind.Notification or TransientCardKind.Track)
            return;
        if (kind == TransientCardKind.Track &&
            _transientKind == TransientCardKind.Notification)
            return;

        bool sameState = _transientKind == kind;
        switch (kind)
        {
            case TransientCardKind.Track:
                UpdateFullTrackCard(_currentMedia);
                break;
            case TransientCardKind.Notification:
                NotificationAppName.Text = title;
                NotificationTitle.Text = subtitle;
                NotificationBody.Text = details;
                NotificationBody.Visibility = string.IsNullOrWhiteSpace(details)
                    ? Visibility.Collapsed : Visibility.Visible;
                break;
            case TransientCardKind.Volume:
                _displayVolumePercent = Math.Clamp(progress ?? _volumeMonitor.CurrentVolumePercent, 0, 100);
                VolumeLevelText.Text = $"{_displayVolumePercent:0}%";
                UpdateVolumeBar();
                break;
            case TransientCardKind.Brightness:
                _displayBrightnessPercent = Math.Clamp(progress ?? 0, 0, 100);
                BrightnessLevelText.Text = $"{_displayBrightnessPercent:0}%";
                UpdateBrightnessBar();
                break;
        }

        // Repeated volume/brightness steps NEVER recompose the layout, morph
        // the Body or restart the opacity animation: only the fill and number move.
        if (!sameState)
        {
            _transientKind = kind;
            _hoverExpandTimer.Stop();
            UpdateTrackDisplay();
            UpdateCollapsedWidth(animate: !_expanded);
        }

        _transientTimer.Stop();
        _transientDeadline = DateTimeOffset.UtcNow + duration;
        _transientTimer.Start();
    }

    private void HideTransientCard()
    {
        _transientTimer.Stop();
        _transientDeadline = DateTimeOffset.MinValue;
        if (_transientKind == TransientCardKind.None) return;
        _transientKind = TransientCardKind.None;
        UpdateTrackDisplay();
        UpdateCollapsedWidth(animate: !_expanded);
    }

    private void SetTrackLabel(TextBlock block, string title, string artist)
    {
        block.Inlines.Clear();
        if (string.IsNullOrWhiteSpace(title)) return;

        block.Inlines.Add(new System.Windows.Documents.Run(title)
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPriBrush")
        });
        if (!string.IsNullOrWhiteSpace(artist))
        {
            block.Inlines.Add(new System.Windows.Documents.Run("  —  ")
            {
                Foreground = (Brush)FindResource("AccentBrush")
            });
            block.Inlines.Add(new System.Windows.Documents.Run(artist)
            {
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextSecBrush")
            });
        }
    }

    private static BitmapImage? CreateTrackArtwork(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void UpdateTrackMarquee()
    {
        if (!ShowTrack || !IsLoaded || TrackTextViewport.ActualWidth <= 0)
        {
            _marqueeTimer.Stop();
            TrackTextTransform.X = 0;
            return;
        }

        CollapsedTrackTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double titleWidth = CollapsedTrackTitle.DesiredSize.Width;
        if (titleWidth <= TrackTextViewport.ActualWidth + 1)
        {
            _marqueeTimer.Stop();
            RepeatedTrackTitle.Visibility = Visibility.Collapsed;
            TrackTextStrip.Width = titleWidth;
            TrackTextTransform.X = 0;
            return;
        }

        RepeatedTrackTitle.Visibility = Visibility.Visible;
        TrackTextStrip.Width = titleWidth * 2 + 28 * UiScale;
        _marqueeOffset = 0;
        TrackTextTransform.X = 0;
        _marqueeTimer.Start();
    }

    private void AdvanceTrackMarquee()
    {
        double titleWidth = CollapsedTrackTitle.ActualWidth;
        if (!ShowTrack || titleWidth <= TrackTextViewport.ActualWidth)
        {
            UpdateTrackMarquee();
            return;
        }

        _marqueeOffset += 0.9 * UiScale;
        double loopLength = titleWidth + 28 * UiScale;
        if (_marqueeOffset >= loopLength) _marqueeOffset = 0;
        TrackTextTransform.X = -_marqueeOffset;
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

    /// <summary>Медленное, плавное раскрытие/сворачивание без пружинного перелёта.</summary>
    private void AnimateProgress(double target, bool opening)
    {
        var animation = new DoubleAnimation
        {
            From = Progress,
            To = target,
            Duration = TimeSpan.FromMilliseconds(opening ? 380 : 340),
            EasingFunction = opening
                ? new CubicBezierEase { X1 = 0.18, Y1 = 0.62, X2 = 0.24, Y2 = 1 }
                : new CubicBezierEase { X1 = 0.18, Y1 = 0.62, X2 = 0.24, Y2 = 1 }
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
                "player" => new PlayerPanel(_media),
                "weather" => new WeatherPanel(),
                "apps" => new LauncherPanel(),
                "ai" => new AiChatPanel(),
                "control" => new ControlPanel(),
                _ => new HomePanel()
            };
            _panels[key] = panel;
        }

        var currentKey = CurrentPanelKey();
        if (!ReferenceEquals(PanelHost.Content, panel))
        {
            int transitionVersion = ++_panelTransitionVersion;
            int currentIndex = Array.IndexOf(TabOrder, currentKey);
            int nextIndex = Array.IndexOf(TabOrder, key);
            double direction = nextIndex >= currentIndex ? 1 : -1;
            _panelTransition.BeginAnimation(TranslateTransform.XProperty, null);
            PanelHost.BeginAnimation(UIElement.OpacityProperty, null);

            if (PanelHost.Content is null)
            {
                PanelHost.Content = panel;
                _panelTransition.X = 0;
                PanelHost.Opacity = 1;
            }
            else
            {
                var exit = new DoubleAnimation(_panelTransition.X, -direction * 20,
                    TimeSpan.FromMilliseconds(115))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                exit.Completed += (_, _) =>
                {
                    if (transitionVersion != _panelTransitionVersion) return;
                    PanelHost.BeginAnimation(UIElement.OpacityProperty, null);
                    _panelTransition.BeginAnimation(TranslateTransform.XProperty, null);
                    PanelHost.Content = panel;
                    _panelTransition.X = direction * 20;
                    PanelHost.Opacity = 0;
                    PanelHost.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    _panelTransition.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(direction * 20, 0, TimeSpan.FromMilliseconds(230))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                };
                _panelTransition.BeginAnimation(TranslateTransform.XProperty, exit);
                PanelHost.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(115))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
            }
        }
        else
        {
            ++_panelTransitionVersion;
            _panelTransition.BeginAnimation(TranslateTransform.XProperty, null);
            PanelHost.BeginAnimation(UIElement.OpacityProperty, null);
            _panelTransition.X = 0;
            PanelHost.Opacity = 1;
        }

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
            if (!_expanded && !_gameMode &&
                _transientKind is TransientCardKind.None or TransientCardKind.Track &&
                !_hoverExpandTimer.IsEnabled)
                _hoverExpandTimer.Start();
        }
        else
        {
            _hoverExpandTimer.Stop();
            if (_expanded && IslandMenu.IsOpen != true && !_hoverCollapseTimer.IsEnabled)
                _hoverCollapseTimer.Start();
        }
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
        _brightnessPower.HandleMessage(msg, wParam, lParam);
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
