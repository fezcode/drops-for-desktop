using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Drops.Controls;

/// <summary>
/// The kit's .fz-flow: a slowly moving abstract colour field behind a tile. This is the WebGL
/// shader from fz-art.js, run by Skia instead, with the "sage" palette Drops uses.
/// The field leans toward the pointer, and gets livelier while <see cref="IsSurging"/> is set
/// (Drops sets it while a file is held over the window).
/// </summary>
public sealed class FlowSurface : AnimatedControl
{
    public static readonly StyledProperty<bool> IsSurgingProperty =
        AvaloniaProperty.Register<FlowSurface, bool>(nameof(IsSurging));

    public bool IsSurging
    {
        get => GetValue(IsSurgingProperty);
        set => SetValue(IsSurgingProperty, value);
    }

    // base, then three colours that drift through it: PALETTES.sage in fz-art.js
    static readonly float[][] Palette = [Rgb(0xdc, 0xeb, 0xe1), Rgb(0xf3, 0xf6, 0xe6), Rgb(0xa9, 0xd3, 0xba), Rgb(0xf2, 0xd9, 0xa0)];

    static float[] Rgb(int r, int g, int b) => [r / 255f, g / 255f, b / 255f];

    // The field is soft, so it is drawn small and stretched: cheap on any GPU.
    const float Scale = 0.4f;

    readonly double offset = Random.Shared.NextDouble() * 100;
    double mx = 0.5, my = 0.5, tx = 0.5, ty = 0.5, boost;

    public FlowSurface() => IsHitTestVisible = false;

    /// <summary>Tell the field where the pointer is, as 0..1 across and up the tile.</summary>
    public void LeanToward(double x, double y)
    {
        tx = x;
        ty = y;
    }

    public override void Render(DrawingContext context)
    {
        mx += (tx - mx) * 0.04;
        my += (ty - my) * 0.04;
        boost += ((IsSurging ? 1 : 0) - boost) * 0.06;
        var time = offset + Now * (1 + boost * 3);
        context.Custom(new Op(new Rect(Bounds.Size), (float)time, (float)mx, (float)my, (float)boost));
    }

    sealed class Op(Rect bounds, float time, float mx, float my, float boost) : ICustomDrawOperation
    {
        const string Source = """
            uniform float2 r; uniform float t; uniform float2 m; uniform float boost;
            uniform float3 c0; uniform float3 c1; uniform float3 c2; uniform float3 c3;
            // This Skia has no smoothstep for runtime shaders.
            float ss(float a,float b,float x){float k=clamp((x-a)/(b-a),0.,1.);return k*k*(3.-2.*k);}
            float hash(float2 p){return fract(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p); float2 f=fract(p); f=f*f*(3.-2.*f);
              return mix(mix(hash(i),hash(i+float2(1.,0.)),f.x),mix(hash(i+float2(0.,1.)),hash(i+float2(1.,1.)),f.x),f.y);}
            float fbm(float2 p){float v=0.; float a=.5; for(int i=0;i<4;i++){v+=a*noise(p);p=p*2.02+float2(1.7,9.2);a*=.5;}return v;}
            half4 main(float2 fc){
              float2 g=float2(fc.x,r.y-fc.y);
              float2 uv=g/r; float2 p=uv; p.x*=r.x/r.y;
              float tt=t*.045;
              float2 q=float2(fbm(p*1.25+tt),fbm(p*1.25+float2(5.2,1.3)-tt));
              float2 w=float2(fbm(p*1.1+2.6*q+float2(1.7,9.2)+tt*.8),fbm(p*1.1+2.6*q+float2(8.3,2.8)-tt*.6));
              float f=fbm(p*.9+2.8*w+(m-.5)*.5);
              float3 c=mix(c0,c1,ss(.25,.7,f));
              c=mix(c,c2,ss(.38,.82,w.x)*.9);
              c=mix(c,c3,ss(.5,.9,q.y*w.y*2.4+boost*.25)*(.5+boost*.45));
              c+=(hash(floor(g)+fract(t))-.5)*.03;
              return half4(half3(c),1.);
            }
            """;

        static readonly Lazy<SKRuntimeEffect?> Effect = new(() => SKRuntimeEffect.Create(Source, out _));

        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var w = (float)bounds.Width * Scale;
            var h = (float)bounds.Height * Scale;
            if (w < 1 || h < 1) return;

            using var paint = new SKPaint();
            var effect = Effect.Value;
            if (effect == null)
            {
                // No shader support: the palette's base colour, like the page before its script runs.
                paint.Color = new SKColor(0xdc, 0xeb, 0xe1);
                canvas.DrawRect(SKRect.Create((float)bounds.Width, (float)bounds.Height), paint);
                return;
            }

            var uniforms = new SKRuntimeEffectUniforms(effect);
            uniforms["r"] = new[] { w, h };
            uniforms["t"] = time;
            uniforms["m"] = new[] { mx, my };
            uniforms["boost"] = boost;
            for (var i = 0; i < 4; i++) uniforms["c" + i] = Palette[i];
            using var shader = effect.ToShader(true, uniforms);
            paint.Shader = shader;

            canvas.Save();
            canvas.Scale(1 / Scale);
            canvas.DrawRect(SKRect.Create(w, h), paint);
            canvas.Restore();
        }
    }
}
