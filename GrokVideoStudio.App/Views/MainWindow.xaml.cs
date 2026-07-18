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
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        // Resolve the main ViewModel from DI
        DataContext = services.GetRequiredService<MainViewModel>();

        // When NavigationView finishes loading, it auto-selects the first
        // menu item (Generate). We listen for Loaded to trigger splash fade-out.
        RootNavigation.Loaded += OnNavigationLoaded;
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
