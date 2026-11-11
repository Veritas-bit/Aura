using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Controls;

public enum EyeEmotion { Neutral, Sleepy }

/// <summary>
/// Компаньон AURA — полностью векторные аниме-глаза в стиле Genshin Impact:
/// миндалевидная форма, острый угловатый лайнер со стрелкой, градиентная
/// радужка с бликами. Всё процедурно и непрерывно: радужка плавно следует
/// за курсором (пружинное сглаживание каждый кадр), моргание — плавный
/// спуск верхнего века, сон — закрытые глаза, пробуждение — взгляд «ищет»
/// указатель по экрану и фокусируется на нём. Контур адаптивен:
/// чёрный на светлой теме, белый — на тёмной; радужка — акцентный цвет.
/// </summary>
public sealed class AtlasEyesControl : FrameworkElement, IDisposable
{
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;

    private bool _lineLight = true;   // светлая тема → чёрный контур; тёмная → белый
    private Color _accent = (Color)ColorConverter.ConvertFromString("#A855F7");

    private enum Mode { Idle, Tracking, Searching, Sleeping }

    private Mode _mode = Mode.Idle;
    private EyeEmotion _emotion = EyeEmotion.Neutral;

    // --- непрерывное состояние анимации ---
    private double _gazeX, _gazeY;          // текущее смещение взгляда (-1..1)
    private double _gazeTargetX, _gazeTargetY;
    private double _open = 1.0;             // открытость века 0..1 (плавная)
    private double _openTarget = 1.0;

    private DateTime _nextBlink = DateTime.Now.AddSeconds(2.2);
    private DateTime _blinkShutUntil = DateTime.MinValue;
    private DateTime _nextIdleLook = DateTime.Now.AddSeconds(2);
    private DateTime _searchStart;
    private DateTime _searchUntil;
    private int _searchStep;
    private bool _hooksAttached;
    private bool _disposed;

    public EyeEmotion CurrentEmotion => _emotion;

    /// <summary>Масштаб компаньона (1.0 = 30 px в высоту, два глаза + зазор).</summary>
    public double Scale
    {
        get => Height / 30.0;
        set { Height = 30 * value; Width = Height * 2.15; }
    }

    public AtlasEyesControl()
    {
        Scale = 1.0;
        IsHitTestVisible = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_hooksAttached) return;
        _hooksAttached = true;
        _lastFrame = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Detach()
    {
        if (!_hooksAttached) return;
        _hooksAttached = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    // -------------------- внешнее управление --------------------

    /// <summary>Контур под тему: светлая → чёрные линии, тёмная → белые.</summary>
    public void SetLineTheme(bool light)
    {
        if (_lineLight == light) return;
        _lineLight = light;
        InvalidateVisual();
    }

    /// <summary>Радужка перекрашивается под акцентный цвет.</summary>
    public void SetEyeColor(Color accent)
    {
        if (_accent == accent) return;
        _accent = accent;
        InvalidateVisual();
    }

    public void SetEmotion(EyeEmotion emotion)
    {
        _emotion = emotion;
        if (emotion == EyeEmotion.Sleepy)
        {
            _mode = Mode.Sleeping;
            _openTarget = 0;
        }
        else if (_mode == Mode.Sleeping)
        {
            WakeUp();
        }
    }

    /// <summary>Пробуждение: глаза открываются и «ищут» указатель взглядом
    /// по экрану плавными движениями, затем фокусируются на нём.</summary>
    public void WakeUp()
    {
        bool wasSleeping = _mode == Mode.Sleeping;
        _emotion = EyeEmotion.Neutral;
        if (wasSleeping)
        {
            _mode = Mode.Searching;
            _searchStart = DateTime.Now;
            _searchUntil = _searchStart.AddSeconds(2.4);
            _searchStep = 0;
        }
        else if (_mode != Mode.Searching)
        {
            _mode = Mode.Tracking;
        }
        _nextBlink = DateTime.Now.AddSeconds(1.6);
    }

    // -------------------- поведение (каждый кадр) --------------------

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var nowTs = _clock.Elapsed;
        double dt = Math.Min((nowTs - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = nowTs;
        if (dt <= 0) return;
        var now = DateTime.Now;

        switch (_mode)
        {
            case Mode.Sleeping:
                _openTarget = 0;
                break;

            case Mode.Searching:
                UpdateSearch(now);
                break;

            case Mode.Tracking:
                UpdateTracking();
                break;

            default: // Idle
                UpdateIdle(now);
                break;
        }

        // моргание поверх любого режима, кроме сна
        if (_mode != Mode.Sleeping)
        {
            if (now >= _nextBlink && now >= _blinkShutUntil)
            {
                _blinkShutUntil = now.AddMilliseconds(85);
                _nextBlink = now.AddSeconds(_rng.NextDouble() < 0.16
                    ? 0.45 : 2.4 + _rng.NextDouble() * 3.8);
            }
            _openTarget = now < _blinkShutUntil ? 0 : _mode == Mode.Sleeping ? 0 : OpenBaseTarget();
        }

        // пружинное сглаживание: взгляд — мягкая пружина, веко — быстрая
        double gazeK = 1 - Math.Exp(-dt * 9.0);
        double openK = 1 - Math.Exp(-dt * (_openTarget < _open ? 26.0 : 13.0));
        _gazeX += (_gazeTargetX - _gazeX) * gazeK;
        _gazeY += (_gazeTargetY - _gazeY) * gazeK;
        _open += (_openTarget - _open) * openK;
        if (Math.Abs(_open - _openTarget) < 0.002) _open = _openTarget;

        InvalidateVisual();
    }

    /// <summary>Базовая цель открытости: лёгкий щур, когда курсор прямо на компаньоне.</summary>
    private double OpenBaseTarget()
    {
        if (_mode == Mode.Tracking && CursorOnSelf()) return 0.55;
        return 1.0;
    }

    private void UpdateIdle(DateTime now)
    {
        if (now >= _nextIdleLook)
        {
            _gazeTargetX = _rng.NextDouble() * 1.4 - 0.7;
            _gazeTargetY = _rng.NextDouble() * 0.8 - 0.4;
            _nextIdleLook = now.AddSeconds(2.5 + _rng.NextDouble() * 3.5);
        }
        if (CursorNear(420)) _mode = Mode.Tracking;
    }

    private void UpdateTracking()
    {
        if (!TryCursorOffset(out double dx, out double dy))
        {
            _gazeTargetX = 0; _gazeTargetY = 0;
            return;
        }
        _gazeTargetX = Math.Clamp(dx / 450.0, -1, 1);
        _gazeTargetY = Math.Clamp(dy / 320.0, -1, 1);
    }

    private void UpdateSearch(DateTime now)
    {
        // взгляд «ищет» указатель: плавно обводит стороны экрана и центр
        (double x, double y)[] sweep =
            { (-1, 0.2), (1, -0.2), (0.4, -1), (-0.5, 1), (0.9, 0.5), (-0.9, -0.4), (0, 0) };
        double stepLen = (_searchUntil - _searchStart).TotalMilliseconds / sweep.Length;
        int idx = Math.Clamp((int)((now - _searchStart).TotalMilliseconds / stepLen), 0, sweep.Length - 1);
        if (idx != _searchStep) _searchStep = idx;
        _gazeTargetX = sweep[idx].x;
        _gazeTargetY = sweep[idx].y;
        if (now >= _searchUntil) _mode = Mode.Tracking;
    }

    // -------------------- курсор --------------------

    private bool TryCursorOffset(out double dx, out double dy)
    {
        dx = dy = 0;
        try
        {
            GetCursorPos(out var pt);
            var origin = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            dx = pt.X - origin.X;
            dy = pt.Y - origin.Y;
            return true;
        }
        catch { return false; }
    }

    private bool CursorNear(double range)
        => TryCursorOffset(out double dx, out double dy)
           && Math.Abs(dx) < range && Math.Abs(dy) < range * 0.6;

    private bool CursorOnSelf()
        => TryCursorOffset(out double dx, out double dy)
           && Math.Abs(dx) < 70 && Math.Abs(dy) < 55;

    // -------------------- отрисовка --------------------

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        double h = ActualHeight * 0.98;
        double w = h;
        double gap = w * 0.16;          // аккуратный аниме-зазор между глазами
        double total = w * 2 + gap;
        double x = (ActualWidth - total) / 2;
        double y = (ActualHeight - h) / 2;

        DrawEye(dc, x, y, w, h, true);              // левый: стрелка наружу (влево)
        DrawEye(dc, x + w + gap, y, w, h, false);   // правый: стрелка наружу (вправо)
    }

    /// <summary>Один глаз; правый — зеркальная копия левого.</summary>
    private void DrawEye(DrawingContext dc, double x, double y, double w, double h, bool mirror)
    {
        // геометрия строится в локальных координатах глаза (0..w, 0..h),
        // сюда — перенос (и зеркало для правого) в позицию глаза
        dc.PushTransform(mirror
            ? new MatrixTransform(-1, 0, 0, 1, x + w, y)
            : new MatrixTransform(1, 0, 0, 1, x, y));

        double o = _open;
        // лёгкое «прищуривание» формы даже при открытом глазе — аниме-подача
        double shape = 0.12 + 0.88 * o;

        var (upper, lower, liner, flick) = BuildLidGeometry(w, h, shape);

        Color linerColor = _lineLight ? Color.FromRgb(0x14, 0x16, 0x1C) : Colors.White;
        Color scleraColor = _lineLight ? Colors.White : Color.FromRgb(0xF4, 0xF5, 0xFA);

        // белок
        dc.DrawGeometry(new SolidColorBrush(scleraColor), null, upper);

        // радужка — под клип белка, чтобы не вылезала за веко
        dc.PushClip(upper);
        DrawIris(dc, w, h, shape, linerColor);
        // мягкая тень от верхнего века на белке
        var lidShade = new LinearGradientBrush(
            Color.FromArgb(60, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), new Point(0.5, 0), new Point(0.5, 1));
        lidShade.Freeze();
        dc.DrawRectangle(lidShade, null, new Rect(0, 0, w, h * 0.34 * shape));
        dc.Pop();

        // нижнее веко — тонкая линия
        var lowerPen = new Pen(new SolidColorBrush(linerColor) { Opacity = 0.45 },
            Math.Max(1.0, h * 0.028)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        lowerPen.Freeze();
        dc.DrawGeometry(null, lowerPen, lower);

        // верхний лайнер — толстая угловатая линия + стрелка
        double linerW = Math.Max(1.4, h * 0.085);
        var linerPen = new Pen(new SolidColorBrush(linerColor), linerW)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        linerPen.Freeze();
        dc.DrawGeometry(null, linerPen, liner);

        var flickPen = new Pen(new SolidColorBrush(linerColor), Math.Max(1.2, linerW * 0.72))
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Triangle };
        flickPen.Freeze();
        dc.DrawGeometry(null, flickPen, flick);

        dc.Pop();
    }

    /// <summary>Геометрия век: верхняя линия анимируется параметром shape (0=закрыт, 1=открыт).</summary>
    private static (PathGeometry fill, PathGeometry lower, PathGeometry liner, PathGeometry flick)
        BuildLidGeometry(double w, double h, double shape)
    {
        // опорные точки (в долях): внутренний угол I и внешний O
        double ix = 0.05 * w, iy = 0.60 * h;
        double ox = 0.95 * w, oy = 0.47 * h;

        // верхнее веко при полностью открытом глазе — ломаная дуга с изломами
        // (миндалевидная форма Genshin: подъём, лёгкий угол на пике, спуск к стрелке)
        (double u, double x, double y)[] up =
        {
            (0.00, ix,         iy),
            (0.25, 0.27 * w,   0.17 * h),
            (0.50, 0.50 * w,   0.11 * h),
            (0.72, 0.70 * w,   0.13 * h),
            (1.00, ox,         oy),
        };

        // нижнее веко — мягкая квадратичная дуга I→O
        double cx = 0.50 * w, cy = 0.86 * h;
        double Ly(double u)
        {
            double v = 1 - u;
            return v * v * iy + 2 * u * v * cy + u * u * oy;
        }

        // текущая верхняя линия: интерполяция нижнее→открытое по shape
        var pts = new Point[up.Length];
        for (int i = 0; i < up.Length; i++)
        {
            double lowY = Ly(up[i].u);
            pts[i] = new Point(up[i].x, lowY + (up[i].y - lowY) * shape);
        }

        // заливка белка: верхняя линия + обратный проход по нижнему веку
        var fill = new PathGeometry();
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        AddSmoothPolyline(fig, pts, -0.02 * h * shape);
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx, cy), new Point(ix, iy), true));
        fill.Figures.Add(fig);
        fill.Freeze();

        // нижнее веко отдельно (тонкая линия)
        var lower = new PathGeometry();
        var lfig = new PathFigure { StartPoint = new Point(ix, iy) };
        lfig.Segments.Add(new QuadraticBezierSegment(new Point(cx, cy), new Point(ox, oy), true));
        lower.Figures.Add(lfig);
        lower.Freeze();

        // лайнер — только верхняя линия
        var liner = new PathGeometry();
        var linerFig = new PathFigure { StartPoint = pts[0] };
        AddSmoothPolyline(linerFig, pts, -0.02 * h * shape);
        liner.Figures.Add(linerFig);
        liner.Freeze();

        // стрелка от внешнего угла вверх-наружу
        var flick = new PathGeometry();
        var ffig = new PathFigure { StartPoint = pts[^1] };
        ffig.Segments.Add(new QuadraticBezierSegment(
            new Point(ox + 0.02 * w, oy - 0.16 * h * shape),
            new Point(0.99 * w, oy - 0.22 * h * shape), true));
        flick.Figures.Add(ffig);
        flick.Freeze();

        return (fill, lower, liner, flick);
    }

    /// <summary>Скруглённая полилиния через точки: квадратичные сегменты
    /// с контрольными точками в серединах, приподнятыми на lift.</summary>
    private static void AddSmoothPolyline(PathFigure fig, Point[] pts, double lift)
    {
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var mid = new Point((pts[i].X + pts[i + 1].X) / 2,
                                (pts[i].Y + pts[i + 1].Y) / 2 + lift);
            fig.Segments.Add(new QuadraticBezierSegment(mid, pts[i + 1], true));
        }
    }

    /// <summary>Радужка с градиентом, зрачком и бликами; смещается взглядом.</summary>
    private void DrawIris(DrawingContext dc, double w, double h, double shape, Color linerColor)
    {
        double rw = 0.185 * w;
        double rh = 0.315 * h * (0.35 + 0.65 * shape);
        double cx = 0.50 * w + _gazeX * 0.13 * w;
        double cy = 0.50 * h + _gazeY * 0.10 * h + (1 - shape) * 0.06 * h;

        var irisLight = Lerp(_accent, Colors.White, 0.42);
        var irisDark = Lerp(_accent, Colors.Black, 0.45);
        var pupil = Lerp(_accent, Colors.Black, 0.84);

        var grad = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.30),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.62,
            RadiusY = 0.62,
        };
        grad.GradientStops.Add(new GradientStop(irisLight, 0.0));
        grad.GradientStops.Add(new GradientStop(_accent, 0.55));
        grad.GradientStops.Add(new GradientStop(irisDark, 1.0));
        grad.Freeze();
        dc.DrawEllipse(grad, null, new Point(cx, cy), rw, rh);

        // тёмный ободок радужки
        var rimPen = new Pen(new SolidColorBrush(Lerp(_accent, Colors.Black, 0.6)),
            Math.Max(1.0, h * 0.022));
        rimPen.Freeze();
        dc.DrawEllipse(null, rimPen, new Point(cx, cy), rw * 0.97, rh * 0.97);

        // зрачок
        var pupilBrush = new SolidColorBrush(pupil);
        pupilBrush.Freeze();
        dc.DrawEllipse(pupilBrush, null, new Point(cx, cy), rw * 0.44, rh * 0.46);

        // блики: на светлой теме — белые, на тёмной — чёрные (по тону линий)
        Color hl = _lineLight ? Colors.White : Color.FromRgb(0x10, 0x10, 0x14);
        var hlBrush = new SolidColorBrush(hl) { Opacity = _lineLight ? 0.92 : 0.55 };
        hlBrush.Freeze();
        dc.DrawEllipse(hlBrush, null, new Point(cx - rw * 0.34, cy - rh * 0.38), rw * 0.24, rh * 0.20);
        dc.DrawEllipse(hlBrush, null, new Point(cx + rw * 0.36, cy + rh * 0.30), rw * 0.11, rh * 0.09);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
