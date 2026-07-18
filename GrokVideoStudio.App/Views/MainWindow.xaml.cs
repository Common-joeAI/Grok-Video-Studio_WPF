using System.Windows;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace GrokVideoStudio.App.Views;

/// <summary>
/// Main window code-behind.
/// WPF UI NavigationView handles page routing automatically via TargetPageType.
/// We just resolve the ViewModel from DI and set it as DataContext.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        // Resolve the main ViewModel from DI
        DataContext = services.GetRequiredService<MainViewModel>();

        // Set up cached page navigation (WPF UI handles this via TargetPageType).
        // Each page resolves its own ViewModel from the global container in its constructor.
        RootNavigation.Navigate(typeof(Views.Pages.GeneratePage));
    }
}
