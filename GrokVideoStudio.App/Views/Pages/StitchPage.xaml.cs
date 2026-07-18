using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

public partial class StitchPage : Page
{
    public StitchPage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<StitchViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is StitchViewModel vm)
            await vm.LoadVideosCommand.ExecuteAsync(null);
    }
}
