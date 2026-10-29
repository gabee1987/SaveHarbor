using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SaveHarbor.App.Views.Shell;

// Enshrouded's ambient effect: faint rune circles turning slowly behind the panels, the shroud's blue mist and violet
// haze drifting across the window, and shroud spores floating up through it. Light by design: the circles and glows
// are drawn once into cached bitmaps and only moved or turned, the spores are soft dots without effects, and nothing
// runs faster than 20 frames a second. Created when shown, stopped when unloaded; still when Windows animations are off.
public sealed class ShroudMistView : Grid
{
    private const int FrameRate = 20;
    private const int SporeCount = 26;

    public static readonly DependencyProperty MistColorProperty =
        DependencyProperty.Register(nameof(MistColor), typeof(Color), typeof(ShroudMistView), new PropertyMetadata(Colors.SlateGray));

    public static readonly DependencyProperty ShroudColorProperty =
        DependencyProperty.Register(nameof(ShroudColor), typeof(Color), typeof(ShroudMistView), new PropertyMetadata(Colors.SlateBlue));

    public static readonly DependencyProperty RuneColorProperty =
        DependencyProperty.Register(nameof(RuneColor), typeof(Color), typeof(ShroudMistView), new PropertyMetadata(Colors.Goldenrod));

    // Each glow: size, where it sits, how far and how slowly it drifts, how strong it is, and whether it is the shroud.
    private static readonly (double Width, double Height, HorizontalAlignment Horizontal, VerticalAlignment Vertical, double DriftX, double DriftY, double Seconds, double Opacity, bool Shroud)[] Glows =
    [
        (1100, 700, HorizontalAlignment.Left, VerticalAlignment.Top, 360, 120, 52, 0.06, false),
        (1200, 760, HorizontalAlignment.Right, VerticalAlignment.Bottom, -420, -90, 59, 0.045, false),
        (1600, 520, HorizontalAlignment.Center, VerticalAlignment.Bottom, 260, -40, 47, 0.07, true)
    ];

    // Each rune circle: diameter, where it sits, its offset from that edge, seconds per turn (negative turns back), strength.
    private static readonly (double Size, HorizontalAlignment Horizontal, VerticalAlignment Vertical, double OffsetX, double OffsetY, double Seconds, double Opacity)[] Circles =
    [
        (1000, HorizontalAlignment.Right, VerticalAlignment.Top, -300, -470, 240, 0.16),
        (680, HorizontalAlignment.Left, VerticalAlignment.Bottom, -220, -200, -180, 0.13),
        (460, HorizontalAlignment.Left, VerticalAlignment.Top, -170, -250, 150, 0.12)
    ];

    private readonly Random random = new(12345);
    private readonly List<FrameworkElement> animated = [];
    private readonly List<(Ellipse Spore, double RelativeX, double RelativeY)> spores = [];
    private readonly Canvas sporeField = new() { ClipToBounds = true };

    public ShroudMistView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Create();
        Unloaded += (_, _) => Clear();
        SizeChanged += (_, _) => PositionSpores();
    }

    public Color MistColor
    {
        get => (Color)GetValue(MistColorProperty);
        set => SetValue(MistColorProperty, value);
    }

    public Color ShroudColor
    {
        get => (Color)GetValue(ShroudColorProperty);
        set => SetValue(ShroudColorProperty, value);
    }

    public Color RuneColor
    {
        get => (Color)GetValue(RuneColorProperty);
        set => SetValue(RuneColorProperty, value);
    }

    private void Create()
    {
        Clear();
        var animate = SystemParameters.ClientAreaAnimation;
        CreateCircles(animate);
        CreateGlows(animate);
        Children.Add(sporeField);
        CreateSpores(animate);
    }

    private void CreateCircles(bool animate)
    {
        var stroke = new SolidColorBrush(RuneColor);
        stroke.Freeze();
        foreach (var (size, horizontal, vertical, offsetX, offsetY, seconds, opacity) in Circles)
        {
            var circle = new Path
            {
                Data = RuneCircle(size / 2),
                Stroke = stroke,
                StrokeThickness = 1,
                Width = size,
                Height = size,
                Opacity = opacity,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Margin = EdgeOffset(horizontal, vertical, offsetX, offsetY),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(),
                CacheMode = new BitmapCache()
            };
            animated.Add(circle);
            Children.Add(circle);
            if (animate)
            {
                var turn = new DoubleAnimation(0, Math.Sign(seconds) * 360, TimeSpan.FromSeconds(Math.Abs(seconds))) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(turn, FrameRate);
                circle.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, turn);
            }
        }
    }

    private void CreateGlows(bool animate)
    {
        foreach (var (width, height, horizontal, vertical, driftX, driftY, seconds, opacity, shroud) in Glows)
        {
            var color = shroud ? ShroudColor : MistColor;
            var fill = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
            fill.Freeze();
            var glow = new Ellipse
            {
                Width = width,
                Height = height,
                Fill = fill,
                Opacity = opacity,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Margin = new Thickness(-width / 3, -height / 3, -width / 3, -height / 3),
                RenderTransform = new TranslateTransform(),
                CacheMode = new BitmapCache()
            };
            animated.Add(glow);
            Children.Add(glow);
            if (animate)
            {
                Sway(glow.RenderTransform, TranslateTransform.XProperty, driftX, seconds);
                Sway(glow.RenderTransform, TranslateTransform.YProperty, driftY, seconds * 0.7);
                if (shroud)
                {
                    // The shroud breathes: it creeps in and falls back.
                    var breathe = new DoubleAnimation(opacity * 0.5, opacity, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                    Timeline.SetDesiredFrameRate(breathe, FrameRate);
                    glow.BeginAnimation(OpacityProperty, breathe);
                }
            }
        }
    }

    // Soft dots in the mist and shroud colours that rise slowly, sway and fade in and out at random times.
    private void CreateSpores(bool animate)
    {
        for (var index = 0; index < SporeCount; index++)
        {
            var color = index % 3 == 0 ? MistColor : ShroudColor;
            var fill = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
            fill.Freeze();
            var size = 4 + random.NextDouble() * 6;
            var spore = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = fill,
                Opacity = animate ? 0 : 0.25,
                RenderTransform = new TranslateTransform()
            };
            sporeField.Children.Add(spore);
            spores.Add((spore, random.NextDouble(), 0.15 + random.NextDouble() * 0.85));
            animated.Add(spore);
            if (animate)
            {
                Float(spore);
            }
        }

        PositionSpores();
    }

    private void Float(Ellipse spore)
    {
        var duration = TimeSpan.FromSeconds(14 + random.NextDouble() * 12);
        var delay = TimeSpan.FromSeconds(random.NextDouble() * 14);
        var transform = (TranslateTransform)spore.RenderTransform;

        var rise = new DoubleAnimation(0, -(60 + random.NextDouble() * 120), duration) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever };
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = duration, RepeatBehavior = RepeatBehavior.Forever };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.5 + random.NextDouble() * 0.4, KeyTime.FromPercent(0.35)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Timeline.SetDesiredFrameRate(rise, FrameRate);
        Timeline.SetDesiredFrameRate(fade, FrameRate);

        transform.BeginAnimation(TranslateTransform.YProperty, rise);
        Sway(transform, TranslateTransform.XProperty, (random.NextDouble() - 0.5) * 50, 5 + random.NextDouble() * 4);
        spore.BeginAnimation(OpacityProperty, fade);
    }

    private static void Sway(Animatable target, DependencyProperty property, double distance, double seconds)
    {
        var animation = new DoubleAnimation(0, distance, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever
        };
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
    }

    private void PositionSpores()
    {
        foreach (var (spore, relativeX, relativeY) in spores)
        {
            Canvas.SetLeft(spore, relativeX * ActualWidth);
            Canvas.SetTop(spore, relativeY * ActualHeight);
        }
    }

    // A rune circle on a square of 2 x radius: an outer band with ticks, a ring of nodes, an eight-point star and a
    // small centre ring joined to the star by spokes. Original shapes, not taken from the game.
    private static Geometry RuneCircle(double radius)
    {
        var centre = new Point(radius, radius);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            void Ring(double r) => AddCircle(context, centre, r);

            Ring(radius - 1);
            Ring(radius * 0.94);
            Ring(radius * 0.72);
            Ring(radius * 0.66);
            Ring(radius * 0.22);

            for (var tick = 0; tick < 72; tick++)
            {
                var length = tick % 6 == 0 ? 0.88 : 0.94;
                Line(context, centre, radius * length, radius - 1, tick * 5);
            }

            for (var node = 0; node < 12; node++)
            {
                AddCircle(context, Polar(centre, radius * 0.83, node * 30 + 15), radius * 0.025);
            }

            foreach (var start in new[] { 0.0, 45.0 })
            {
                var corners = Enumerable.Range(0, 4).Select(corner => Polar(centre, radius * 0.66, start + corner * 90)).ToArray();
                context.BeginFigure(corners[0], false, true);
                context.PolyLineTo(corners[1..], true, false);
            }

            for (var spoke = 0; spoke < 8; spoke++)
            {
                Line(context, centre, radius * 0.22, radius * 0.47, spoke * 45 + 22.5);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void AddCircle(StreamGeometryContext context, Point centre, double radius)
    {
        var size = new Size(radius, radius);
        context.BeginFigure(new Point(centre.X + radius, centre.Y), false, true);
        context.ArcTo(new Point(centre.X - radius, centre.Y), size, 0, false, SweepDirection.Clockwise, true, false);
        context.ArcTo(new Point(centre.X + radius, centre.Y), size, 0, false, SweepDirection.Clockwise, true, false);
    }

    private static void Line(StreamGeometryContext context, Point centre, double from, double to, double degrees)
    {
        context.BeginFigure(Polar(centre, from, degrees), false, false);
        context.LineTo(Polar(centre, to, degrees), true, false);
    }

    private static Point Polar(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(radians), centre.Y - radius * Math.Cos(radians));
    }

    // A negative offset pushes the shape past the edge it is aligned to, so only part of it shows.
    private static Thickness EdgeOffset(HorizontalAlignment horizontal, VerticalAlignment vertical, double x, double y) =>
        new(
            horizontal == HorizontalAlignment.Left ? x : 0,
            vertical == VerticalAlignment.Top ? y : 0,
            horizontal == HorizontalAlignment.Right ? x : 0,
            vertical == VerticalAlignment.Bottom ? y : 0);

    private void Clear()
    {
        foreach (var element in animated)
        {
            element.BeginAnimation(OpacityProperty, null);
            switch (element.RenderTransform)
            {
                case TranslateTransform translate:
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    break;
                case RotateTransform rotate:
                    rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    break;
            }
        }

        animated.Clear();
        spores.Clear();
        sporeField.Children.Clear();
        Children.Clear();
    }
}
