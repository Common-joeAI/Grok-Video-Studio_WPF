using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

public partial class ActivityLogPage : Page
{
    public ActivityLogPage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<ActivityLogViewModel>();
    }
}
