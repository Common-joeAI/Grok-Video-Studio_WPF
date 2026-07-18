using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<SettingsViewModel>();
    }
}
