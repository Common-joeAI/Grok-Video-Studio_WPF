using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Generate page — resolves its ViewModel from the DI container.
/// </summary>
public partial class GeneratePage : Page
{
    public GeneratePage(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<GenerateViewModel>();
    }
}
