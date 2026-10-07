using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SaveHarbor.App.Views.Shell;

// Slow, faint embers drifting upwards, like the sparks on the game's menus. Purely decorative: it ignores input,
// creates its particles when shown and drops them when unloaded. Hosted only while "Ambient effects" is on.
public sealed class EmberFieldView : Canvas
{
    private const int EmberCount = 18;

    public static readonly DependencyProperty EmberBrushProperty =
        DependencyProperty.Register(nameof(EmberBrush), typeof(Brush), typeof(EmberFieldView), new PropertyMetadata(Brushes.Orange));

    private readonly Random random = new(12345);
    private readonly List<(Ellipse Ember, double RelativeX)> embers = [];

    public EmberFieldView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => CreateEmbers();
        Unloaded += (_, _) => ClearEmbers();
        SizeChanged += (_, _) => PositionEmbers();
    }

    public Brush EmberBrush
    {
        get => (Brush)GetValue(EmberBrushProperty);
        set => SetValue(EmberBrushProperty, value);
    }

    private void CreateEmbers()
    {
        ClearEmbers();
        for (var index = 0; index < EmberCount; index++)
        {
            var size = 1.5 + random.NextDouble() * 2.5;
            var ember = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = EmberBrush,
                Opacity = 0,
                RenderTransform = new TranslateTransform(),
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 1.5 }
            };
            Children.Add(ember);
            embers.Add((ember, random.NextDouble()));
            Animate(ember);
        }

        PositionEmbers();
    }

    private void Animate(Ellipse ember)
    {
        var duration = TimeSpan.FromSeconds(10 + random.NextDouble() * 9);
        var delay = TimeSpan.FromSeconds(random.NextDouble() * 12);
        var height = Math.Max(ActualHeight, 600);
        var transform = (TranslateTransform)ember.RenderTransform;

        var rise = new DoubleAnimation(height + 10, height * (0.15 + random.NextDouble() * 0.4), duration)
        {
            BeginTime = delay,
            RepeatBehavior = RepeatBehavior.Forever
        };
        var drift = new DoubleAnimation(0, (random.NextDouble() - 0.5) * 90, duration)
        {
            BeginTime = delay,
            RepeatBehavior = RepeatBehavior.Forever
        };
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = duration, RepeatBehavior = RepeatBehavior.Forever };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.55 + random.NextDouble() * 0.35, KeyTime.FromPercent(0.25)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

        transform.BeginAnimation(TranslateTransform.YProperty, rise);
        transform.BeginAnimation(TranslateTransform.XProperty, drift);
        ember.BeginAnimation(OpacityProperty, fade);
    }

    private void PositionEmbers()
    {
        foreach (var (ember, relativeX) in embers)
        {
            SetLeft(ember, relativeX * ActualWidth);
        }
    }

    private void ClearEmbers()
    {
        foreach (var (ember, _) in embers)
        {
            ember.BeginAnimation(OpacityProperty, null);
            ember.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ember.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        embers.Clear();
        Children.Clear();
    }
}
