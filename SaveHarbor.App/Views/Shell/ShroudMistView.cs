using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SaveHarbor.App.Views.Shell;

// Enshrouded's ambient effect: two large, faint glows in the shroud's blue drifting across the window, out of phase.
// Light by design: two shapes, each drawn once into a cached bitmap and only moved, about 15 frames a second. Created
// when shown, stopped when unloaded; still when Windows animations are off.
public sealed class ShroudMistView : Grid
{
    private const int FrameRate = 15;

    public static readonly DependencyProperty MistColorProperty =
        DependencyProperty.Register(nameof(MistColor), typeof(Color), typeof(ShroudMistView), new PropertyMetadata(Colors.SlateGray));

    // Each glow: size, where it starts, how far and how slowly it drifts, how strong it is.
    private static readonly (double Width, double Height, HorizontalAlignment Horizontal, VerticalAlignment Vertical, double DriftX, double DriftY, double Seconds, double Opacity)[] Glows =
    [
        (1100, 700, HorizontalAlignment.Left, VerticalAlignment.Top, 360, 120, 52, 0.06),
        (1200, 760, HorizontalAlignment.Right, VerticalAlignment.Bottom, -420, -90, 59, 0.045)
    ];

    private readonly List<Ellipse> glows = [];

    public ShroudMistView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Create();
        Unloaded += (_, _) => Clear();
    }

    public Color MistColor
    {
        get => (Color)GetValue(MistColorProperty);
        set => SetValue(MistColorProperty, value);
    }

    private void Create()
    {
        Clear();
        var animate = SystemParameters.ClientAreaAnimation;
        foreach (var (width, height, horizontal, vertical, driftX, driftY, seconds, opacity) in Glows)
        {
            var fill = new RadialGradientBrush(MistColor, Color.FromArgb(0, MistColor.R, MistColor.G, MistColor.B));
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
            glows.Add(glow);
            Children.Add(glow);
            if (animate)
            {
                Run(glow.RenderTransform, TranslateTransform.XProperty, new DoubleAnimation(0, driftX, TimeSpan.FromSeconds(seconds)));
                Run(glow.RenderTransform, TranslateTransform.YProperty, new DoubleAnimation(0, driftY, TimeSpan.FromSeconds(seconds * 0.7)));
            }
        }
    }

    private static void Run(Animatable target, DependencyProperty property, DoubleAnimation animation)
    {
        animation.AutoReverse = true;
        animation.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        animation.RepeatBehavior = RepeatBehavior.Forever;
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
    }

    private void Clear()
    {
        foreach (var glow in glows)
        {
            glow.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
            glow.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
        }

        glows.Clear();
        Children.Clear();
    }
}
