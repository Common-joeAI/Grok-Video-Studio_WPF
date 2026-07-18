using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Activity log page — resolves its ViewModel from the global DI container.
/// FIX: Parameterless constructor for WPF UI NavigationView compatibility.
/// </summary>
public partial class ActivityLogPage : Page
{
    public ActivityLogPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ActivityLogViewModel>();
    }
}
