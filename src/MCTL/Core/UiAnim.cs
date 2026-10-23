using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MCTL.Core;

/// <summary>轻量界面动画辅助：淡入、位移展开、缩放。</summary>
public static class UiAnim
{
    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    /// <summary>淡入 + 从指定纵向偏移滑入（用于展开面板、详情页）。</summary>
    public static void ExpandIn(FrameworkElement element, double offsetY = 10, int milliseconds = 220)
    {
        if (element == null) return;
        var ease = EaseOut();
        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform(0, offsetY);
            element.RenderTransform = translate;
        }
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }

    /// <summary>只在透明度上做淡入。</summary>
    public static void FadeIn(UIElement element, int milliseconds = 200)
    {
        if (element == null) return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut() });
    }

    /// <summary>从右下角展开（缩放 + 位移 + 淡入），用于模组管理等浮层。</summary>
    public static void ExpandFromCorner(FrameworkElement element, double offsetX = 26, double offsetY = 26,
        int milliseconds = 220)
    {
        if (element == null) return;
        var ease = EaseOut();
        var group = new TransformGroup();
        var scale = new ScaleTransform(0.94, 0.94);
        var translate = new TranslateTransform(offsetX, offsetY);
        group.Children.Add(scale);
        group.Children.Add(translate);
        element.RenderTransformOrigin = new Point(1, 1);
        element.RenderTransform = group;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(offsetX, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }

    /// <summary>向右下角缩回（收起浮层），动画结束后执行回调隐藏元素。</summary>
    public static void CollapseToCorner(FrameworkElement element, Action onCompleted,
        double offsetX = 26, double offsetY = 26, int milliseconds = 160)
    {
        if (element == null || element.Visibility != Visibility.Visible)
        {
            onCompleted?.Invoke();
            return;
        }

        var ease = EaseOut();
        if (element.RenderTransform is not TransformGroup group || group.Children.Count < 2 ||
            group.Children[0] is not ScaleTransform scale || group.Children[1] is not TranslateTransform translate)
        {
            group = new TransformGroup();
            scale = new ScaleTransform(1, 1);
            translate = new TranslateTransform(0, 0);
            group.Children.Add(scale);
            group.Children.Add(translate);
            element.RenderTransformOrigin = new Point(1, 1);
            element.RenderTransform = group;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            onCompleted?.Invoke();
        };

        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, offsetX, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, offsetY, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }
}
