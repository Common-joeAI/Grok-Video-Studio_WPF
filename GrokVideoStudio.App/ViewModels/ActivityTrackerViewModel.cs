using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Lightweight ViewModel for the always-visible activity tracker strip at
/// the bottom of the MainWindow. Subscribes to IActivityLogService and surfaces
/// the latest entry + a bounded list of recent entries for the expandable panel.
/// </summary>
public partial class ActivityTrackerViewModel : ObservableObject
{
    private readonly IActivityLogService _activityLog;

    /// <summary>Recent entries shown in the expanded panel (bounded to 100).</summary>
    [ObservableProperty]
    private ObservableCollection<ActivityEntry> _recentEntries = [];

    /// <summary>The most recent log entry — shown in the collapsed strip.</summary>
    [ObservableProperty]
    private ActivityEntry? _latestEntry;

    /// <summary>True when the panel is expanded to show the full list.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Pulse animation trigger — fires on each new entry so the UI can animate.</summary>
    [ObservableProperty]
    private bool _pulse;

    /// <summary>Count of entries with Error level since last clear.</summary>
    [ObservableProperty]
    private int _errorCount;

    /// <summary>Count of entries with Warning level since last clear.</summary>
    [ObservableProperty]
    private int _warningCount;

    public ActivityTrackerViewModel(IActivityLogService activityLog)
    {
        _activityLog = activityLog;
        _activityLog.EntryAdded += OnEntryAdded;

        // Pre-load recent entries
        foreach (var entry in _activityLog.GetRecentEntries(100))
            RecentEntries.Add(entry);

        if (RecentEntries.Count > 0)
            LatestEntry = RecentEntries[^1];
    }

    private void OnEntryAdded(ActivityEntry entry)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            RecentEntries.Add(entry);
            if (RecentEntries.Count > 100)
                RecentEntries.RemoveAt(0);

            LatestEntry = entry;

            if (entry.Level == LogLevel.Error)
                ErrorCount++;
            else if (entry.Level == LogLevel.Warning)
                WarningCount++;

            // Toggle pulse to trigger animation
            Pulse = !Pulse;
        });
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void Clear()
    {
        RecentEntries.Clear();
        LatestEntry = null;
        ErrorCount = 0;
        WarningCount = 0;
    }
}
