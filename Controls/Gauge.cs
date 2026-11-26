using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Drops.Controls;

/// <summary>
/// The thin bar under each file (.gauge): how much time it has left, or how much of it has been sent.
/// The fill slides to a new value instead of jumping.
/// </summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Gauge, IBrush?>(nameof(Fill));

    static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(18, 22, 19, 15)); // rgba(22,19,15,.07)

    static Gauge() => AffectsRender<Gauge>(ValueProperty, FillProperty);

    /// <param name="slide">How long the fill takes to reach a new value: 0.6s for time left, 0.2s while sending.</param>
    public Gauge(TimeSpan slide)
    {
        Height = 6;
        Transitions = [new DoubleTransition { Property = ValueProperty, Duration = slide }];
    }

    public Gauge() : this(TimeSpan.FromSeconds(0.6)) { }

    /// <summary>0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var track = new RoundedRect(new Rect(Bounds.Size), 3);
        context.DrawRectangle(Track, null, track);
        var width = Bounds.Width * Math.Clamp(Value, 0, 1);
        if (width <= 0 || Fill == null) return;
        using (context.PushClip(track))
            context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, width, Bounds.Height), 3));
    }
}
