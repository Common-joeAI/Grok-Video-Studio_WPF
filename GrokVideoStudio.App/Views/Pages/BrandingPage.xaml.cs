using System.Windows.Controls;
using GrokVideoStudio.App.ViewModels;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Branding page — configure brand identity applied to all generated videos.
/// </summary>
public partial class BrandingPage : Page
{
    public BrandingPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(BrandingViewModel));
    }
}
