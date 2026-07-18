using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

public partial class HistoryPage : Page
{
    public HistoryPage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<HistoryViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-refresh when navigating to this page
        if (DataContext is HistoryViewModel vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
        }
    }
}
