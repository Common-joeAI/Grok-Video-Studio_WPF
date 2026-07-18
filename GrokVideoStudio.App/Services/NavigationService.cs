using System.Windows;

namespace GrokVideoStudio.App.Services;

/// <summary>
/// Simple page navigation service for the WPF UI NavigationView.
/// Modernized: uses a callback-based approach so ViewModels can trigger
/// navigation without referencing the View layer (kept in App project only).
/// </summary>
public interface INavigationService
{
    void NavigateTo(string pageKey);
    event Action<string>? NavigationRequested;
}

public sealed class NavigationService : INavigationService
{
    public event Action<string>? NavigationRequested;

    public void NavigateTo(string pageKey)
    {
        NavigationRequested?.Invoke(pageKey);
    }
}
