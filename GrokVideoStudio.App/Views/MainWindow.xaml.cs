using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Animation;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace GrokVideoStudio.App.Views;

/// <summary>
/// Main window code-behind.
/// WPF UI NavigationView handles page routing automatically via TargetPageType.
/// Each page has a parameterless constructor that resolves its ViewModel from
/// the global App.Services DI container.
///
/// SPLASH: A loading overlay is shown on launch and fades out once the
/// NavigationView has navigated to the first page, preventing the blank
/// window flash.
///
/// ACTIVITY TRACKER: A live strip at the bottom of the window shows the
/// latest log entry at all times. Expandable to a scrollable list. The
/// ActivityTrackerViewModel is a singleton so it persists across navigation.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ActivityTrackerViewModel _tracker;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        // Resolve the main ViewModel from DI
        DataContext = services.GetRequiredService<MainViewModel>();

        // Set up the activity tracker panel
        _tracker = services.GetRequiredService<ActivityTrackerViewModel>();
        ActivityTrackerPanel.DataContext = _tracker;

        // Auto-scroll the expanded log to the bottom when new entries arrive
        _tracker.RecentEntries.CollectionChanged += OnRecentEntriesChanged;

        // When NavigationView finishes loading, it auto-selects the first
        // menu item (Generate). We listen for Loaded to trigger splash fade-out.
        RootNavigation.Loaded += OnNavigationLoaded;
    }

    private void OnRecentEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && TrackerLogList.Items.Count > 0)
        {
            // Scroll to the latest entry
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var lastItem = TrackerLogList.Items[^1];
                TrackerLogList.ScrollIntoView(lastItem);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        // Give the NavigationView a beat to render the first page,
        // then fade the splash overlay out.
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await Task.Delay(600);

            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOut.Completed += (_, _) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
            };

            SplashOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}
