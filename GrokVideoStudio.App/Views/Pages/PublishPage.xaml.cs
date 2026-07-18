using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Publish page — resolves its ViewModel from the global DI container.
/// FIX: Parameterless constructor for WPF UI NavigationView compatibility.
/// </summary>
public partial class PublishPage : Page
{
    public PublishPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<PublishViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PublishViewModel vm)
        {
            await vm.LoadVideosCommand.ExecuteAsync(null);
        }
    }
}
