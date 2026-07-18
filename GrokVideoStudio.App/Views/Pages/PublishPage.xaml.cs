using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

public partial class PublishPage : Page
{
    public PublishPage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<PublishViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PublishViewModel vm)
            await vm.LoadVideosCommand.ExecuteAsync(null);
    }
}
