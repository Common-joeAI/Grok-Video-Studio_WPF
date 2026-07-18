using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Activity log ViewModel — shows real-time log entries from all services.
/// FEATURE PARITY: The original Python app has an Activity Log panel showing
/// execution traces, errors, and diagnostics. This WPF version subscribes to
/// the IActivityLogService event stream.
/// </summary>
public partial class ActivityLogViewModel : ObservableObject
{
    private readonly IActivityLogService _activityLog;

    [ObservableProperty]
    private ObservableCollection<ActivityEntry> _entries = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ActivityLogViewModel(IActivityLogService activityLog)
    {
        _activityLog = activityLog;

        // Subscribe to real-time log entries
        _activityLog.EntryAdded += OnEntryAdded;

        // Load any existing entries
        foreach (var entry in _activityLog.GetRecentEntries(100))
            Entries.Add(entry);
    }

    private void OnEntryAdded(ActivityEntry entry)
    {
        // Marshal to UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Add(entry);
            // Keep max 500 entries to prevent memory issues
            if (Entries.Count > 500)
                Entries.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        StatusMessage = "Log cleared.";
    }

    [RelayCommand]
    private void ExportLog()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Activity Log",
            Filter = "Text Files|*.txt|All Files|*.*",
            FileName = $"activity_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dialog.ShowDialog() == true)
        {
            var lines = Entries.Select(e => $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] [{e.Level}] {e.Message}");
            File.WriteAllLines(dialog.FileName, lines);
            StatusMessage = $"Exported to {dialog.FileName}";
        }
    }
}
