using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CompanionEyes.Controls;

public enum CompanionMode { Idle, Tracking, Searching, Sleeping, Attention, Typing, Voice }
public enum CompanionEmotion { Neutral, Happy, Curious, Surprised, Shy, Annoyed, Worried }

/// <summary>
/// Transparent eye companion for an existing Dynamic Island window.
/// The atlas contains one authored eye per cell; the second eye is mirrored at render time.
/// </summary>
public sealed class CompanionEyesControl : FrameworkElement, IDisposable
{
    private const int CellW = 384, CellH = 256, Columns = 4, FrameCount = 16;
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();
    private readonly BitmapSource[] _frames = new BitmapSource[FrameCount];
    private CompanionMode _mode = CompanionMode.Idle;
    private CompanionEmotion _emotion = CompanionEmotion.Neutral;
    private DateTime _modeUntil = DateTime.MinValue;
    private DateTime _nextBlink = DateTime.Now.AddSeconds(2.5);
    private DateTime _blinkStart = DateTime.MinValue;
    private DateTime _blinkEnd = DateTime.MinValue;
    private DateTime _nextIdleLook = DateTime.Now.AddSeconds(2);
    private DateTime _idleLookEnd = DateTime.MinValue;
    private int _frame;
    private int _searchStep;
    private double _voiceLevel;
    private bool _typing;
    private bool _pointerInside;
    private Point _targetGaze = new(.5, .5);
    private Point _gaze = new(.5, .5);
    private bool _disposed;

    public CompanionMode Mode => _mode;
    public CompanionEmotion Emotion => _emotion;

    public CompanionEyesControl()
    {
        var atlas = new BitmapImage(new Uri("pack://application:,,,/Assets/eyes-atlas.png", UriKind.Absolute));
        for (int i = 0; i < FrameCount; i++)
        {
            var crop = new CroppedBitmap(atlas, new Int32Rect((i % Columns) * CellW, (i / Columns) * CellH, CellW, CellH));
            crop.Freeze();
            _frames[i] = crop;
        }

        Width = 430;
        Height = 285;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        IsHitTestVisible = false; // Let the Dynamic Island receive clicks; forward pointer data from its owner.
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void SetMode(CompanionMode mode)
    {
        _mode = mode;
        _modeUntil = mode switch
        {
            CompanionMode.Attention => DateTime.Now.AddSeconds(2.0),
            CompanionMode.Searching => DateTime.Now.AddSeconds(3.2),
            _ => DateTime.MinValue
        };
        _searchStep = 0;
        if (mode == CompanionMode.Sleeping) _voiceLevel = 0;
        InvalidateVisual();
    }

    public void SetEmotion(CompanionEmotion emotion)
    {
        _emotion = emotion;
        if (emotion == CompanionEmotion.Surprised)
        {
            _mode = CompanionMode.Attention;
            _modeUntil = DateTime.Now.AddSeconds(1.35);
        }
        InvalidateVisual();
    }

    public void NotifyAttention() { SetEmotion(CompanionEmotion.Surprised); SetMode(CompanionMode.Attention); }

    public void SetTyping(bool value)
    {
        _typing = value;
        if (value && _mode != CompanionMode.Sleeping) SetMode(CompanionMode.Typing);
        else if (!value && _mode == CompanionMode.Typing) SetMode(CompanionMode.Idle);
    }

    /// <summary>Pass normalized audio amplitude/RMS in the range 0..1.</summary>
    public void SetVoiceLevel(double level)
    {
        _voiceLevel = Math.Clamp(level, 0, 1);
        if (_voiceLevel > .035 && _mode != CompanionMode.Sleeping) _mode = CompanionMode.Voice;
        else if (_mode == CompanionMode.Voice) _mode = _typing ? CompanionMode.Typing : CompanionMode.Idle;
    }

    /// <summary>Pass pointer position in this control's coordinate space.</summary>
    public void TrackPointer(Point point)
    {
        if (_mode == CompanionMode.Sleeping) Wake();
        _pointerInside = true;
        _targetGaze = new Point(Math.Clamp(point.X / Math.Max(1, ActualWidth), 0, 1),
                                Math.Clamp(point.Y / Math.Max(1, ActualHeight), 0, 1));
        if (_mode == CompanionMode.Idle) _mode = CompanionMode.Tracking;
    }

    public void PointerLeft()
    {
        _pointerInside = false;
        if (_mode == CompanionMode.Tracking) _mode = _typing ? CompanionMode.Typing : CompanionMode.Idle;
    }

    public void Wake()
    {
        if (_mode == CompanionMode.Sleeping) _mode = CompanionMode.Idle;
        _nextBlink = DateTime.Now.AddSeconds(1.5);
        _emotion = CompanionEmotion.Neutral;
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var now = DateTime.Now;
        // Smooth gaze motion. The atlas provides authored gaze poses; this keeps transitions calm.
        _gaze = new Point(Lerp(_gaze.X, _targetGaze.X, .18), Lerp(_gaze.Y, _targetGaze.Y, .18));

        if ((_mode == CompanionMode.Attention || _mode == CompanionMode.Searching) && now >= _modeUntil)
            _mode = _typing ? CompanionMode.Typing : (_pointerInside ? CompanionMode.Tracking : CompanionMode.Idle);

        if (_mode == CompanionMode.Sleeping) { _frame = 10; InvalidateVisual(); return; }

        if (now < _blinkEnd)
        {
            var elapsed = (now - _blinkStart).TotalMilliseconds;
            _frame = elapsed < 65 ? 8 : elapsed < 125 ? 9 : 10;
            InvalidateVisual();
            return;
        }
        if (now >= _nextBlink)
        {
            _blinkStart = now;
            _blinkEnd = now.AddMilliseconds(190);
            _nextBlink = now.AddSeconds(2.8 + _rng.NextDouble() * 3.8);
            _frame = 8;
            InvalidateVisual();
            return;
        }

        switch (_mode)
        {
            case CompanionMode.Searching:
                int[] search = { 0, 1, 2, 3, 4, 5, 2, 0 };
                _frame = search[(_searchStep++ / 3) % search.Length];
                break;
            case CompanionMode.Attention:
                _frame = _emotion == CompanionEmotion.Shy ? 15 : 0;
                break;
            case CompanionMode.Sleeping:
                _frame = 10;
                break;
            case CompanionMode.Typing:
                _frame = ((int)(now.TimeOfDay.TotalMilliseconds / 520) % 2 == 0) ? 1 : 5;
                break;
            case CompanionMode.Voice:
                _frame = _voiceLevel > .65 ? 0 : _voiceLevel > .2 ? 6 : 7;
                break;
            case CompanionMode.Tracking:
                _frame = PickGazeFrame(_gaze);
                break;
            default:
                if (now >= _nextIdleLook)
                {
                    _idleLookEnd = now.AddMilliseconds(500);
                    _nextIdleLook = now.AddSeconds(2.5 + _rng.NextDouble() * 3.5);
                }
                if (now < _idleLookEnd) _frame = _rng.NextDouble() < .5 ? 1 : 5;
                else _frame = EmotionFrame(_emotion);
                break;
        }
        InvalidateVisual();
    }

    private static int PickGazeFrame(Point p)
    {
        if (p.X < .22) return 3;
        if (p.X < .40) return 2;
        if (p.X > .78) return 4;
        if (p.X > .60) return 5;
        if (p.Y < .28) return 7;
        if (p.Y > .72) return 6;
        return 0;
    }

    private static int EmotionFrame(CompanionEmotion emotion) => emotion switch
    {
        CompanionEmotion.Happy => 6,
        CompanionEmotion.Curious => 2,
        CompanionEmotion.Surprised => 0,
        CompanionEmotion.Shy => 15,
        CompanionEmotion.Annoyed => 9,
        CompanionEmotion.Worried => 8,
        _ => 0
    };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var img = _frames[Math.Clamp(_frame, 0, FrameCount - 1)];
        double h = ActualHeight * .94;
        double w = h * (CellW / (double)CellH) * .49;
        double gap = ActualWidth * .025;
        double total = w * 2 + gap;
        double x = (ActualWidth - total) / 2;
        double y = (ActualHeight - h) / 2;
        double scale = _mode == CompanionMode.Voice ? 1 + _voiceLevel * .018 : 1;
        dc.PushTransform(new ScaleTransform(scale, scale, ActualWidth / 2, ActualHeight / 2));
        dc.DrawImage(img, new Rect(x, y, w, h));
        // Mirror the authored eye to form a symmetrical pair. The atlas itself has alpha transparency.
        dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, 2 * (x + w) + gap, 0));
        dc.DrawImage(img, new Rect(x, y, w, h));
        dc.Pop();
        dc.Pop();
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
