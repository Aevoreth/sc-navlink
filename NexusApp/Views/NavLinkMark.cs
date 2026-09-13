using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NexusApp.Services;

namespace NexusApp.Views;

/// <summary>
/// Animated SC-navLink mark: two waypoints joined by a route that draws toward
/// a destination chevron. Geometry lives in a 64x64 unit space. Honors Reduce
/// animations and pauses off-screen.
/// </summary>
public sealed class NavLinkMark : Viewbox
{
    private const double Cycle = 4.4;

    private readonly Canvas _canvas = new() { Width = 64, Height = 64 };
    private Storyboard? _master;
    private Storyboard? _pulse;
    private bool _built;
    private static bool _loggedOnce;

    public NavLinkMark()
    {
        Stretch = Stretch.Uniform;
        Child = _canvas;
        Loaded += (_, _) => { Build(); Start(); };
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Start(); else Stop(); };
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        _master = new Storyboard { Duration = TimeSpan.FromSeconds(Cycle), RepeatBehavior = RepeatBehavior.Forever };

        var cyan = Brush("CyanBrush", Color.FromRgb(0x7F, 0xE9, 0xE0));
        var bright = Brush("AccentHoverBrush", Color.FromRgb(0xB8, 0xFF, 0xF6));
        var dim = new SolidColorBrush(Color.FromArgb(0x66, 0x7F, 0xE9, 0xE0));

        // Origin waypoint
        AddNode(16, 46, 5.2, dim);
        var originCore = AddNode(16, 46, 2.4, cyan);

        // Route: origin -> elbow -> destination
        var route = new Path
        {
            Data = Geometry.Parse("M16,46 L28,28 L48,18"),
            Stroke = cyan,
            StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        const double dash = 22;
        route.StrokeDashArray = new DoubleCollection { dash, dash };
        _canvas.Children.Add(route);
        AddDashTrack(route, dash);

        // Destination waypoint
        AddNode(48, 18, 6.2, dim);
        var dest = AddNode(48, 18, 3.0, bright);

        // Next-move chevron past the destination
        var chevron = new Path
        {
            Data = Geometry.Parse("M54,14 L59,18 L54,22"),
            Stroke = bright,
            StrokeThickness = 2.0,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.85,
        };
        _canvas.Children.Add(chevron);
        AddOpacityTrack(chevron, (0, 0.35), (0.18, 0.35), (0.32, 1), (0.72, 1), (0.88, 0.35), (1, 0.35));

        _pulse = MicroLoop(dest, UIElement.OpacityProperty, 1, 0.72, 1.1);
        _ = originCore;

        if (Motion.Reduced)
        {
            _master = null;
            _pulse = null;
            route.StrokeDashArray = null;
            chevron.Opacity = 1;
        }

        if (!_loggedOnce) { _loggedOnce = true; Logger.Info("[UI] nav-link mark active"); }
    }

    private Ellipse AddNode(double cx, double cy, double r, Brush fill)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        _canvas.Children.Add(e);
        return e;
    }

    private static Brush Brush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        return new SolidColorBrush(fallback);
    }

    private void AddDashTrack(Shape line, double dashUnits)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(Cycle) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(dashUnits, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.28), ease));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.72)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-dashUnits, KeyTime.FromPercent(0.92), ease));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-dashUnits, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, line);
        Storyboard.SetTargetProperty(anim, new PropertyPath(Shape.StrokeDashOffsetProperty));
        _master!.Children.Add(anim);
    }

    private void AddOpacityTrack(UIElement el, params (double t, double v)[] keys)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(Cycle) };
        foreach (var (t, v) in keys)
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromPercent(Math.Min(t, 1))));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
        _master!.Children.Add(anim);
    }

    private static Storyboard MicroLoop(UIElement el, DependencyProperty prop, double from, double to, double seconds)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
        sb.Children.Add(anim);
        return sb;
    }

    private void Start()
    {
        if (Motion.Reduced) return;
        _master?.Begin(this, true);
        _pulse?.Begin(this, true);
    }

    private void Stop()
    {
        _master?.Stop(this);
        _pulse?.Stop(this);
    }
}
