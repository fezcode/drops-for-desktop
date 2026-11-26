using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Drops.Controls;

/// <summary>
/// The kit's logo mark (.fz-mark): a block that keeps quietly changing shape and colour.
/// 9s loop: terra square, sage circle, brass leaf, back to the square.
/// </summary>
public sealed class MorphMark : AnimatedControl
{
    static readonly Color Terra = Color.Parse("#c96442");
    static readonly Color Sage = Color.Parse("#3f7a5c");
    static readonly Color Brass = Color.Parse("#e3b65b");

    // top-left, top-right, bottom-right, bottom-left radius; rotation; colour
    readonly record struct Shape(double Tl, double Tr, double Br, double Bl, double Angle, Color Color);

    static readonly Shape Square = new(8, 8, 8, 8, 0, Terra);
    static readonly Shape Circle = new(12, 12, 12, 12, 90, Sage);
    static readonly Shape Leaf = new(12, 12, 8, 12, 225, Brass);
    static readonly Shape SquareAgain = new(8, 8, 8, 8, 360, Terra);

    public MorphMark()
    {
        Width = 24;
        Height = 24;
    }

    public override void Render(DrawingContext context)
    {
        var p = Now % 9 / 9;
        Shape a, b;
        double t;
        if (p < 0.22) (a, b, t) = (Square, Square, 0);
        else if (p < 0.33) (a, b, t) = (Square, Circle, Ease(Span(0.22, 0.33, p)));
        else if (p < 0.55) (a, b, t) = (Circle, Circle, 0);
        else if (p < 0.66) (a, b, t) = (Circle, Leaf, Ease(Span(0.55, 0.66, p)));
        else if (p < 0.88) (a, b, t) = (Leaf, Leaf, 0);
        else (a, b, t) = (Leaf, SquareAgain, Ease(Span(0.88, 1, p)));

        var color = Color.FromRgb(
            (byte)Lerp(a.Color.R, b.Color.R, t), (byte)Lerp(a.Color.G, b.Color.G, t), (byte)Lerp(a.Color.B, b.Color.B, t));
        var rect = new RoundedRect(new Rect(0, 0, 24, 24),
            new CornerRadius(Lerp(a.Tl, b.Tl, t), Lerp(a.Tr, b.Tr, t), Lerp(a.Br, b.Br, t), Lerp(a.Bl, b.Bl, t)));

        var spin = Matrix.CreateTranslation(-12, -12)
                   * Matrix.CreateRotation(Lerp(a.Angle, b.Angle, t) * System.Math.PI / 180)
                   * Matrix.CreateTranslation(12, 12);
        using (context.PushTransform(spin))
            context.DrawRectangle(new ImmutableSolidColorBrush(color), null, rect);
    }
}
