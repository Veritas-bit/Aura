using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Controls;

/// <summary>
/// Minimal AURA companion: two solid vertical capsule eyes.
/// The accent brush is supplied by the active application theme.
/// Animation runs only while the control is loaded and visible.
/// </summary>
public sealed class CapsuleEyesControl : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(CapsuleEyesControl),
            new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private DateTime _nextBlink = DateTime.Now.AddSeconds(2.4);
    private DateTime _blinkStarted;
    private bool _isBlinking;
    private bool _disposed;
    private double _gazeX;
    private double _gazeY;
    private double _targetGazeX;
    private double _targetGazeY;

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public CapsuleEyesControl()
    {
        Width = 30;
        Height = 22;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += OnAnimationTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateTimerState();

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateTimerState();

    private void UpdateTimerState()
    {
        if (_disposed) return;

        if (IsLoaded && IsVisible)
        {
            if (!_timer.IsEnabled)
            {
                _nextBlink = DateTime.Now.AddSeconds(1.5 + _random.NextDouble() * 2.0);
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>Receives pointer coordinates relative to this control from its parent island.</summary>
    public void TrackPointer(Point point)
    {
        _targetGazeX = Math.Clamp((point.X / Math.Max(1, ActualWidth) - 0.5) * 2, -1, 1);
        _targetGazeY = Math.Clamp((point.Y / Math.Max(1, ActualHeight) - 0.5) * 2, -1, 1);
    }

    public void PointerLeft()
    {
        _targetGazeX = 0;
        _targetGazeY = 0;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsVisible) return;

        _gazeX += (_targetGazeX - _gazeX) * 0.16;
        _gazeY += (_targetGazeY - _gazeY) * 0.16;

        var now = DateTime.Now;
        if (_isBlinking)
        {
            if ((now - _blinkStarted).TotalMilliseconds >= 170)
            {
                _isBlinking = false;
                _nextBlink = now.AddSeconds(2.8 + _random.NextDouble() * 3.7);
            }
        }
        else if (now >= _nextBlink)
        {
            _isBlinking = true;
            _blinkStarted = now;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double eyeWidth = Math.Min(width * 0.22, 7.5);
        double eyeHeight = height * 0.72;
        double gap = width * 0.13;

        // A small, smooth squeeze creates a natural blink without changing the capsule silhouette.
        if (_isBlinking)
        {
            double elapsed = (DateTime.Now - _blinkStarted).TotalMilliseconds;
            double phase = Math.Clamp(elapsed / 170.0, 0, 1);
            double squeeze = phase < 0.5
                ? 1.0 - SmoothStep(phase * 2.0) * 0.92
                : 0.08 + SmoothStep((phase - 0.5) * 2.0) * 0.92;
            eyeHeight *= squeeze;
        }

        double totalWidth = eyeWidth * 2 + gap;
        double left = (width - totalWidth) / 2;
        double top = (height - eyeHeight) / 2 + _gazeY * 0.7;
        left += _gazeX * 1.5;
        var brush = AccentBrush ?? Brushes.MediumPurple;

        DrawCapsule(drawingContext, brush, left, top, eyeWidth, eyeHeight);
        DrawCapsule(drawingContext, brush, left + eyeWidth + gap, top, eyeWidth, eyeHeight);
    }

    private static void DrawCapsule(DrawingContext dc, Brush brush,
        double x, double y, double width, double height)
    {
        double radius = Math.Min(width, height) / 2;
        dc.DrawRoundedRectangle(brush, null,
            new Rect(x, y, width, Math.Max(1, height)),
            radius, radius);
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnAnimationTick;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        IsVisibleChanged -= OnIsVisibleChanged;
    }
}
