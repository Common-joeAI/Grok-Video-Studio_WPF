using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Stitch page — resolves its ViewModel from the global DI container.
/// FIX: Parameterless constructor for WPF UI NavigationView compatibility.
/// </summary>
public partial class StitchPage : Page
{
    public StitchPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<StitchViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is StitchViewModel vm)
        {
            await vm.LoadVideosCommand.ExecuteAsync(null);
        }
    }
}
