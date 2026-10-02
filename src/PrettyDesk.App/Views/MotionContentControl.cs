using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrettyDesk.App.Views;

/// <summary>Brief navigation feedback without delaying input or animating layout.</summary>
public sealed class MotionContentControl : ContentControl
{
    private readonly TranslateTransform _offset = new();
    private readonly Dictionary<object, FrameworkElement> _pages = [];
    private readonly Grid _surface = new();
    private FrameworkElement? _activePage;

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(MotionContentControl), new PropertyMetadata(null, OnPageChanged));

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private static void OnPageChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var host = (MotionContentControl)owner;
        if (host._activePage is { } previous) { previous.Visibility = Visibility.Collapsed; }
        if (args.NewValue is not { } model) { host._activePage = null; return; }
        if (!host._pages.TryGetValue(model, out var view))
        {
            var template = (DataTemplate)host.FindResource(new DataTemplateKey(model.GetType()));
            view = (FrameworkElement)template.LoadContent();
            view.DataContext = model;
            host._pages.Add(model, view);
            host._surface.Children.Add(view);
        }
        host._activePage = view;
        view.Visibility = Visibility.Visible;
        host.AnimateNavigation();
    }

    internal void ReleasePages()
    {
        Content = null;
        foreach (var page in _pages.Values) { page.DataContext = null; }
        _pages.Clear();
        _surface.Children.Clear();
        _activePage = null;
    }

    public MotionContentControl()
    {
        RenderTransform = _offset;
        Content = _surface;
        Unloaded += (_, _) => ResetMotion();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { ResetMotion(); } };
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        AnimateNavigation();
    }

    private void AnimateNavigation()
    {
        ResetMotion();
        if (!IsLoaded || !IsVisible || Content is null || SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(160);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, duration) { FillBehavior = FillBehavior.Stop });
        _offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    private void ResetMotion()
    {
        BeginAnimation(OpacityProperty, null);
        _offset.BeginAnimation(TranslateTransform.YProperty, null);
    }
}
