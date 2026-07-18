using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Chain Generation page — audio-driven or manual multi-clip chained generation.
/// Uses parameterless constructor + App.Services for DI compatibility with NavigationView.
/// </summary>
public partial class ChainPage : Page
{
    public ChainPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(ChainGenerationViewModel));
    }
}
