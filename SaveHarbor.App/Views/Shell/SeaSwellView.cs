using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SaveHarbor.App.Views.Shell;

// Windrose's ambient effect: a slow swell rolling along the bottom of the window and a faint compass rose turning
// behind everything. Light by design: four shapes, each drawn once into a cached bitmap and only moved, about 20 frames
// a second, nothing per frame allocated. Created when shown, stopped when unloaded; still when Windows animations are off.
public sealed class SeaSwellView : Grid
{
    private const double WavePeriod = 240;
    private const int FrameRate = 20;

    public static readonly DependencyProperty SwellBrushProperty =
        DependencyProperty.Register(nameof(SwellBrush), typeof(Brush), typeof(SeaSwellView), new PropertyMetadata(Brushes.SlateGray));

    // Each layer: amplitude, height from the bottom, opacity, seconds per period, seconds per bob.
    private static readonly (double Amplitude, double Bottom, double Opacity, double Seconds, double BobSeconds)[] Layers =
    [
        (7, 64, 0.10, 46, 7.5),
        (9, 38, 0.15, 34, 6.1),
        (11, 12, 0.22, 25, 5.2)
    ];

    private readonly List<Path> shapes = [];
    private readonly List<(Path Wave, double Amplitude)> waves = [];

    // The waves are as wide as the window plus one period, so their cached bitmaps stay small; rebuilt when it grows.
    private double waveWidth;

    public SeaSwellView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Create();
        Unloaded += (_, _) => Clear();
        SizeChanged += (_, e) => WidenWaves(e.NewSize.Width);
    }

    public Brush SwellBrush
    {
        get => (Brush)GetValue(SwellBrushProperty);
        set => SetValue(SwellBrushProperty, value);
    }

    private void Create()
    {
        Clear();
        var animate = SystemParameters.ClientAreaAnimation;
        waveWidth = Math.Max(ActualWidth, 1280) + WavePeriod;

        foreach (var (amplitude, bottom, opacity, seconds, bobSeconds) in Layers)
        {
            var wave = new Path
            {
                Data = WaveGeometry(waveWidth, amplitude),
                Stroke = SwellBrush,
                StrokeThickness = 1.4,
                Opacity = opacity,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, bottom),
                RenderTransform = new TranslateTransform(),
                CacheMode = new BitmapCache()
            };
            Add(wave);
            waves.Add((wave, amplitude));
            if (animate)
            {
                // Rolling sideways by exactly one period loops without a seam; the bob lifts it gently.
                Run(wave.RenderTransform, TranslateTransform.XProperty, new DoubleAnimation(0, -WavePeriod, TimeSpan.FromSeconds(seconds)));
                Run(wave.RenderTransform, TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(bobSeconds))
                {
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
        }

        var rose = new Path
        {
            Data = CompassGeometry(),
            Stroke = SwellBrush,
            StrokeThickness = 1,
            Opacity = 0.06,
            Width = 520,
            Height = 520,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -140, -170),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            CacheMode = new BitmapCache()
        };
        Add(rose);
        if (animate)
        {
            Run(rose.RenderTransform, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMinutes(6)));
        }
    }

    private void Add(Path shape)
    {
        shapes.Add(shape);
        Children.Add(shape);
    }

    private static void Run(Animatable target, DependencyProperty property, DoubleAnimation animation)
    {
        animation.RepeatBehavior = RepeatBehavior.Forever;
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
    }

    private void Clear()
    {
        foreach (var shape in shapes)
        {
            if (shape.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
            }
            else
            {
                shape.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        shapes.Clear();
        waves.Clear();
        Children.Clear();
    }

    private void WidenWaves(double width)
    {
        if (waves.Count == 0 || width + WavePeriod <= waveWidth)
        {
            return;
        }

        waveWidth = width + WavePeriod * 2;
        foreach (var (wave, amplitude) in waves)
        {
            wave.Data = WaveGeometry(waveWidth, amplitude);
        }
    }

    // A smooth swell of the given width, which includes the period it rolls into.
    private static Geometry WaveGeometry(double width, double amplitude)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, amplitude), false, false);
            for (var x = 0d; x < width; x += WavePeriod)
            {
                context.BezierTo(new Point(x + WavePeriod * 0.25, -amplitude * 0.6), new Point(x + WavePeriod * 0.25, -amplitude * 0.6), new Point(x + WavePeriod * 0.5, amplitude * 0.15), true, true);
                context.BezierTo(new Point(x + WavePeriod * 0.75, amplitude * 1.9), new Point(x + WavePeriod * 0.75, amplitude * 1.9), new Point(x + WavePeriod, amplitude), true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    // A navigator's compass rose: two rings, eight points and a fine inner star.
    private static Geometry CompassGeometry()
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(100, 100), 96, 96));
        group.Children.Add(new EllipseGeometry(new Point(100, 100), 88, 88));
        group.Children.Add(new EllipseGeometry(new Point(100, 100), 8, 8));
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI / 4;
            var length = index % 2 == 0 ? 92 : 62;
            var side = angle + Math.PI / 2;
            var tip = new Point(100 + Math.Cos(angle) * length, 100 + Math.Sin(angle) * length);
            var left = new Point(100 + Math.Cos(side) * 11, 100 + Math.Sin(side) * 11);
            var right = new Point(100 - Math.Cos(side) * 11, 100 - Math.Sin(side) * 11);
            group.Children.Add(new PathGeometry([new PathFigure(left, [new LineSegment(tip, true), new LineSegment(right, true)], false)]));
            group.Children.Add(new LineGeometry(new Point(100, 100), tip));
        }

        for (var tick = 0; tick < 32; tick++)
        {
            var angle = tick * Math.PI / 16;
            var inner = tick % 4 == 0 ? 80 : 84;
            group.Children.Add(new LineGeometry(
                new Point(100 + Math.Cos(angle) * inner, 100 + Math.Sin(angle) * inner),
                new Point(100 + Math.Cos(angle) * 88, 100 + Math.Sin(angle) * 88)));
        }

        group.Freeze();
        return group;
    }
}
