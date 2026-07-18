using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Main shell ViewModel — manages navigation between pages.
/// Uses CommunityToolkit.Mvvm source generators for properties and commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPage = "Generate";

    [ObservableProperty]
    private string _appTitle = "Grok Video Studio";

    /// <summary>Navigation items for the sidebar.</summary>
    public ObservableCollection<NavItem> NavigationItems { get; } =
    [
        new("Generate", "\uE7B9", "Create"),        // Segoe Fluent Icon: film
        new("History", "\uE7C3", "Library"),         // Segoe Fluent Icon: library
        new("Settings", "\uE713", "Settings"),       // Segoe Fluent Icon: settings
    ];

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;
}

/// <summary>Represents a navigation item in the sidebar.</summary>
public sealed record NavItem(string Key, string IconGlyph, string Label);
