namespace CipherVault.UI.Helpers;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

public static class AnimationHelper
{
    public static void FadeIn(UIElement target, double durationMs = 250, double delayMs = 0)
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void FadeOut(UIElement target, double durationMs = 200)
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void SlideIn(UIElement target, double offsetY = 10, double durationMs = 300)
    {
        target.RenderTransform = new TranslateTransform(0, offsetY);
        target.Opacity = 0;

        var slideAnim = new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs));

        ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideAnim);
        target.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    public static void BouncePress(UIElement target, double scale = 0.95)
    {
        var transform = target.RenderTransform as ScaleTransform
            ?? new ScaleTransform(1, 1, target.RenderSize.Width / 2, target.RenderSize.Height / 2);
        target.RenderTransform = transform;

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(scale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)),
            new KeySpline(0.175, 0.885, 0.32, 1.275)));

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
    }

    public static void Shake(UIElement target, double amplitude = 8, double durationMs = 400)
    {
        var transform = new TranslateTransform();
        target.RenderTransform = transform;

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-amplitude, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(amplitude, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-amplitude * 0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(amplitude * 0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-amplitude * 0.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(amplitude * 0.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs)),
            new KeySpline(0, 0, 0.2, 1)));

        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
