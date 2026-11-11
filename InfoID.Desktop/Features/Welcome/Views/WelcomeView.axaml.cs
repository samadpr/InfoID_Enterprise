using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;


namespace InfoID.Desktop.Features.Welcome.Views;

public partial class WelcomeView : UserControl
{
    // Slide timing is view-only behaviour, so it lives here rather than in the ViewModel.
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _heroPaused;

    public WelcomeView()
    {
        InitializeComponent();
        _heroTimer.Tick += (_, _) => { if (!_heroPaused) MoveHero(1); };
        HeroCarousel.SelectionChanged += (_, _) => UpdateHeroDots();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (HeroCarousel.SelectedIndex < 0) HeroCarousel.SelectedIndex = 0;
        UpdateHeroDots();
        _heroTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _heroTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void MoveHero(int step)
    {
        var count = HeroCarousel.ItemCount;
        if (count == 0) return;
        var current = Math.Max(0, HeroCarousel.SelectedIndex);
        HeroCarousel.SelectedIndex = (current + step + count) % count;
    }

    /// <summary>A manual move restarts the countdown, so the slide doesn't jump right after a click.</summary>
    private void RestartHeroTimer()
    {
        _heroTimer.Stop();
        _heroTimer.Start();
    }

    private void OnHeroPrevious(object? sender, RoutedEventArgs e) { MoveHero(-1); RestartHeroTimer(); }

    private void OnHeroNext(object? sender, RoutedEventArgs e) { MoveHero(1); RestartHeroTimer(); }

    private void OnHeroDotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            HeroCarousel.SelectedIndex = index;
            RestartHeroTimer();
        }
    }

    private void OnHeroPointerEntered(object? sender, PointerEventArgs e) => _heroPaused = true;

    private void OnHeroPointerExited(object? sender, PointerEventArgs e) => _heroPaused = false;

    private void UpdateHeroDots()
    {
        var selected = HeroCarousel.SelectedIndex;
        HeroDot0.Classes.Set("active", selected == 0);
        HeroDot1.Classes.Set("active", selected == 1);
        HeroDot2.Classes.Set("active", selected == 2);
    }
}
