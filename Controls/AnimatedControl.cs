using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace Drops.Controls;

/// <summary>
/// A control that repaints every frame while it can be seen. It stops asking for frames
/// when its window is hidden (Drops spends most of its life in the tray).
/// </summary>
public abstract class AnimatedControl : Control
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    TopLevel? top;
    bool running;

    /// <summary>Seconds since the app started.</summary>
    protected static double Now => Clock.Elapsed.TotalSeconds;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        top = TopLevel.GetTopLevel(this);
        if (top != null) top.PropertyChanged += OnTopChanged;
        Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (top != null) top.PropertyChanged -= OnTopChanged;
        top = null;
        running = false;
    }

    void OnTopChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) Start();
    }

    void Start()
    {
        if (running || top is not { IsVisible: true }) return;
        running = true;
        top.RequestAnimationFrame(Tick);
    }

    void Tick(TimeSpan _)
    {
        if (top is not { IsVisible: true })
        {
            running = false;
            return;
        }
        if (IsEffectivelyVisible) InvalidateVisual();
        top.RequestAnimationFrame(Tick);
    }

    /// <summary>CSS cubic-bezier(x1, y1, x2, y2) evaluated at x.</summary>
    protected static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 24; i++)
        {
            var cx = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
            if (Math.Abs(cx - x) < 1e-5) break;
            if (cx < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
    }

    /// <summary>The kit's --ease: cubic-bezier(0.2, 0.8, 0.2, 1).</summary>
    protected static double Ease(double x) => Bezier(0.2, 0.8, 0.2, 1, x);

    /// <summary>CSS "ease-out".</summary>
    protected static double EaseOut(double x) => Bezier(0, 0, 0.58, 1, x);

    protected static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>How far x is through [from, to], clamped to 0..1.</summary>
    protected static double Span(double from, double to, double x) => Math.Clamp((x - from) / (to - from), 0, 1);
}
