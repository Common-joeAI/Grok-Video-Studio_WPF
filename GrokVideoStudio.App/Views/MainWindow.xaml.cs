using System.Windows;
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
/// FIX: Removed direct Navigate call — WPF UI NavigationView auto-navigates to
/// the first menu item on load. Explicit navigation before the window is fully
/// rendered can cause null reference exceptions.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        // Resolve the main ViewModel from DI
        DataContext = services.GetRequiredService<MainViewModel>();

        // WPF UI NavigationView auto-selects the first menu item.
        // No need to manually navigate — the first NavigationViewItem
        // (Generate) is selected automatically when the window loads.
    }
}
