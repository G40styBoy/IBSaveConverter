using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace IBSaveConverter.Views.Animations;

/// <summary>
/// Switching between the start menu and a game page: the old page fades out while the new one fades in and rises
/// a little into place. Short, so it shows where you went without slowing you down.
/// </summary>
public sealed class FadeRiseTransition : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(260);

    /// <summary>How far the new page rises, in pixels.</summary>
    public double Rise { get; set; } = 14;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var running = new List<Task>();
        if (from is not null)
            running.Add(Fade(1, 0, 0, 0, TimeSpan.FromTicks(Duration.Ticks / 2), new CubicEaseIn()).RunAsync(from, cancellationToken));
        if (to is not null)
        {
            to.IsVisible = true;
            running.Add(Fade(0, 1, Rise, 0, Duration, new CubicEaseOut()).RunAsync(to, cancellationToken));
        }
        await Task.WhenAll(running);

        if (from is not null && !cancellationToken.IsCancellationRequested)
            from.IsVisible = false;
    }

    private static Animation Fade(double fromOpacity, double toOpacity, double fromY, double toY, TimeSpan duration, Easing easing) => new()
    {
        Duration = duration,
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.YProperty, fromY) },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.YProperty, toY) },
            },
        },
    };
}
