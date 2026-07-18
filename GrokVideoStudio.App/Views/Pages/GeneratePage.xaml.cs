using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Generate page — resolves its ViewModel from the global DI container.
/// FIX: Uses parameterless constructor because WPF UI NavigationView creates
/// pages via Activator.CreateInstance (no constructor injection available).
/// </summary>
public partial class GeneratePage : Page
{
    public GeneratePage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<GenerateViewModel>();
    }
}
