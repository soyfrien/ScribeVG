using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ScribeVG;

/// <summary>Visual "haptics": a shake and red flash on a miss, a poof of particles on a good stroke, a burst when a kanji is done.</summary>
public partial class MainWindow
{
    private static readonly Random FxRandom = new();

    /// <summary>Quick side-to-side shake of the writing box plus a faint red flash.</summary>
    private void ShakeMiss()
    {
        if (!_settings.ReduceMotion)
        {
            ShakeOnce();
        }

        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
        MissFlash.BeginAnimation(OpacityProperty, flash);
    }

    private void ShakeOnce()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
        double[] offsets = { -22, 18, -14, 10, -6, 3, 0 };
        for (var i = 0; i < offsets.Length; i++)
        {
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(offsets[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55 * (i + 1)))));
        }
        ShakeTransform.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    /// <summary>Small poof where an accepted stroke ended; bigger and livelier for higher scores.</summary>
    private void PoofAt(Point at, Color color, int score)
    {
        if (_settings.ReduceMotion) return;
        var count = score >= 90 ? 14 : score >= 75 ? 10 : 7;
        var reach = score >= 90 ? 95.0 : 70.0;
        Ring(at, color, 10, reach * 1.1, 420, 6);
        for (var i = 0; i < count; i++)
        {
            var angle = (2 * Math.PI * i / count) + FxRandom.NextDouble() * 0.5;
            var distance = reach * (0.6 + FxRandom.NextDouble() * 0.5);
            Particle(at, color, 10 + FxRandom.NextDouble() * 10, angle, distance, 450 + FxRandom.Next(150));
        }
    }

    /// <summary>Celebration burst from the middle of the box when the whole kanji is finished.</summary>
    private void CelebrateKanji(int average)
    {
        if (_settings.ReduceMotion) return;
        var center = new Point(FxCanvas.ActualWidth / 2, FxCanvas.ActualHeight / 2);
        if (center.X <= 0) center = new Point(542, 542);
        var color = ColorFor(average);
        Ring(center, color, 40, 520, 700, 14);
        Ring(center, Color.FromRgb(0xFF, 0xD5, 0x4F), 20, 380, 600, 8);
        for (var i = 0; i < 32; i++)
        {
            var angle = 2 * Math.PI * i / 32 + FxRandom.NextDouble() * 0.2;
            var tint = i % 3 == 0 ? Color.FromRgb(0xFF, 0xD5, 0x4F) : color;
            Particle(center, tint, 14 + FxRandom.NextDouble() * 14, angle, 260 + FxRandom.NextDouble() * 240, 700 + FxRandom.Next(300));
        }
    }

    private void Particle(Point from, Color color, double size, double angle, double distance, int ms)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color), Opacity = 0.9 };
        var move = new TranslateTransform(from.X - size / 2, from.Y - size / 2);
        var shrink = new ScaleTransform(1, 1, size / 2, size / 2);
        dot.RenderTransform = new TransformGroup { Children = { shrink, move } };
        FxCanvas.Children.Add(dot);

        var duration = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(move.X + Math.Cos(angle) * distance, duration) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(move.Y + Math.Sin(angle) * distance, duration) { EasingFunction = ease });
        shrink.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.2, duration));
        shrink.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, duration));
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) => FxCanvas.Children.Remove(dot);
        dot.BeginAnimation(OpacityProperty, fade);
    }

    private void Ring(Point center, Color color, double from, double to, int ms, double thickness)
    {
        var ring = new Ellipse { Width = 1, Height = 1, Stroke = new SolidColorBrush(color), StrokeThickness = thickness, Opacity = 0.7 };
        // Grow the ellipse's size (not a scale transform) so the stroke width stays constant.
        ring.RenderTransform = new TranslateTransform(center.X, center.Y);
        ring.Width = ring.Height = from;
        ring.Margin = new Thickness(-from / 2, -from / 2, 0, 0);
        FxCanvas.Children.Add(ring);

        var duration = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        ring.BeginAnimation(WidthProperty, grow);
        ring.BeginAnimation(HeightProperty, grow);
        ring.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(-from / 2, -from / 2, 0, 0), new Thickness(-to / 2, -to / 2, 0, 0), duration) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) => FxCanvas.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, fade);
    }
}
