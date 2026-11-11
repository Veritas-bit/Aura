using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>
/// Easing-функция по кубической кривой Безье — как cubic-bezier() в CSS.
/// Даёт идеально гладкую кривую без перелёта: быстрый старт и мягкое
/// затухание в конце (или наоборот — зависит от контрольных точек).
/// </summary>
public class CubicBezierEase : EasingFunctionBase
{
    public double X1 { get; set; } = 0.25;
    public double Y1 { get; set; } = 0.1;
    public double X2 { get; set; } = 0.25;
    public double Y2 { get; set; } = 1.0;

    protected override double EaseInCore(double normalizedTime)
    {
        // Решаем x(t) = normalizedTime методом Ньютона, возвращаем y(t)
        double t = normalizedTime;
        for (int i = 0; i < 8; i++)
        {
            double x = Bez(t, X1, X2) - normalizedTime;
            if (Math.Abs(x) < 1e-7) break;
            double d = BezDeriv(t, X1, X2);
            if (Math.Abs(d) < 1e-7) break;
            t -= x / d;
        }
        return Bez(Math.Clamp(t, 0, 1), Y1, Y2);
    }

    private static double Bez(double t, double p1, double p2)
    {
        double u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    private static double BezDeriv(double t, double p1, double p2)
    {
        double u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();
}
