using System.Windows;
using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// History page — resolves its ViewModel from the global DI container.
/// FIX: Parameterless constructor for WPF UI NavigationView compatibility.
/// </summary>
public partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<HistoryViewModel>();
    }
}
