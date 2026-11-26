using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Drops.Controls;

public enum ShapesKind
{
    /// <summary>The drop zone: two dashed rings turning slowly in opposite directions.</summary>
    Rings,
    /// <summary>The sign-in tile: three shapes falling and the ripples they leave.</summary>
    Falling,
}

/// <summary>
/// The kit's .fz-shapes: abstract sage shapes drifting behind a tile's text. Drawn from the same
/// numbers as the SVG in the page, in the SVG's own coordinates.
/// </summary>
public sealed class Shapes : AnimatedControl
{
    public static readonly StyledProperty<ShapesKind> KindProperty =
        AvaloniaProperty.Register<Shapes, ShapesKind>(nameof(Kind));

    public ShapesKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    static readonly Color Sage = Color.Parse("#3f7a5c");

    static ImmutableSolidColorBrush Tone(double opacity) => new(Sage, opacity);

    public Shapes()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (Kind == ShapesKind.Rings) Rings(context, w, h);
        else Falling(context, w, h);
    }

    // viewBox 0 0 640 300, preserveAspectRatio xMidYMid slice
    static void Rings(DrawingContext context, double w, double h)
    {
        var scale = Math.Max(w / 640, h / 300);
        var fit = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((w - 640 * scale) / 2, (h - 300 * scale) / 2);
        using (context.PushTransform(fit))
        {
            // Dash lengths are in pen widths here, in pixels in SVG: "3 14" and "60 22" at width 2.
            Ring(context, 128, Now / 40 * 360, 0.35, 1.5, 7);
            Ring(context, 196, -Now / 64 * 360, 0.22, 30, 11);
        }
    }

    static void Ring(DrawingContext context, double radius, double degrees, double opacity, double dash, double gap)
    {
        var pen = new Pen(Tone(opacity), 2, new DashStyle([dash, gap], 0), PenLineCap.Round);
        var spin = Matrix.CreateTranslation(-320, -150) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(320, 150);
        using (context.PushTransform(spin))
            context.DrawEllipse(null, pen, new Point(320, 150), radius, radius);
    }

    // viewBox 0 0 560 300, preserveAspectRatio xMaxYMin slice
    static void Falling(DrawingContext context, double w, double h)
    {
        var scale = Math.Max(w / 560, h / 300);
        var fit = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(w - 560 * scale, 0);
        using (context.PushTransform(fit))
        {
            const double loop = 4.8;
            for (var i = 0; i < 3; i++)
            {
                // animation-delay: 0, -1.6s, -3.2s
                var p = (Now + i * 1.6) % loop / loop;

                // fz-fall: drops from -70 to +150 over the first 88%, fading in and out at the ends.
                var y = Lerp(-70, 150, Bezier(0.5, 0, 0.9, 0.6, Span(0, 0.88, p)));
                var fade = p < 0.12 ? Bezier(0.5, 0, 0.9, 0.6, Span(0, 0.12, p))
                    : p < 0.78 ? 1
                    : 1 - Bezier(0.5, 0, 0.9, 0.6, Span(0.78, 0.88, p));
                if (fade > 0)
                {
                    var brush = Tone(0.45 * fade);
                    using (context.PushTransform(Matrix.CreateTranslation(0, y)))
                    {
                        if (i == 0) context.DrawEllipse(brush, null, new Point(470, 50), 9, 9);
                        else if (i == 1) context.DrawRectangle(brush, null, new RoundedRect(new Rect(416, 38, 18, 18), 5));
                        else context.DrawEllipse(brush, null, new Point(516, 44), 6, 6);
                    }
                }

                // fz-ripple: nothing for 80% of the loop, then a ring that opens from 0.2 to full size and fades.
                if (p > 0.8)
                {
                    var (cx, rx, ry) = i switch { 0 => (470.0, 84.0, 20.0), 1 => (425.0, 64.0, 15.0), _ => (516.0, 46.0, 11.0) };
                    var grow = Lerp(0.2, 1, EaseOut(Span(0.8, 1, p)));
                    var alpha = p < 0.84 ? Lerp(0, 0.5, EaseOut(Span(0.8, 0.84, p))) : Lerp(0.5, 0, EaseOut(Span(0.84, 1, p)));
                    // The SVG scales the whole ellipse, stroke included.
                    var pen = new Pen(Tone(alpha), 2 * grow);
                    context.DrawEllipse(null, pen, new Point(cx, 208), rx * grow, ry * grow);
                }
            }
        }
    }
}
