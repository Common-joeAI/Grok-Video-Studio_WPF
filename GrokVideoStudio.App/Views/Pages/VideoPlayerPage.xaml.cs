using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrokVideoStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GrokVideoStudio.App.Views.Pages;

/// <summary>
/// Code-behind for the VideoPlayerPage.xaml.
/// Handles high-performance media timeline updates, seeking logic, and seamless
/// transition of MediaElement to and from a borderless fullscreen container.
///
/// FIX: Parameterless constructor for WPF UI NavigationView compatibility.
/// Resolves ViewModel from App.Services instead of constructor injection.
/// </summary>
public partial class VideoPlayerPage : Page
{
    private readonly DispatcherTimer _timer;
    private bool _isUpdatingPosition = false;
    private bool _userIsDraggingSlider = false;

    // Fullscreen management fields
    private Window? _fullscreenWindow;
    private Grid? _originalParent;

    public VideoPlayerPage()
    {
        InitializeComponent();

        var viewModel = App.Services.GetRequiredService<VideoPlayerViewModel>();
        DataContext = viewModel;

        // High-performance timer for tracking playback position
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // Bind ViewModel actions directly to MediaElement control APIs
        viewModel.PlayRequested += OnPlayRequested;
        viewModel.PauseRequested += OnPauseRequested;
        viewModel.StopRequested += OnStopRequested;
        viewModel.FullscreenToggleRequested += OnFullscreenToggleRequested;

        // Ensure resources and handles are clean when page is unloaded or disposed
        Unloaded += (s, e) =>
        {
            _timer.Stop();
            VideoMediaElement.Stop();
            if (_fullscreenWindow != null)
            {
                CloseFullscreen();
            }
        };
    }

    private void OnPlayRequested()
    {
        if (DataContext is VideoPlayerViewModel vm && vm.VideoPath != null)
        {
            // Update source if it has changed
            if (VideoMediaElement.Source == null || VideoMediaElement.Source.LocalPath != vm.VideoPath)
            {
                VideoMediaElement.Source = new Uri(vm.VideoPath);
            }
            VideoMediaElement.Play();
        }
    }

    private void OnPauseRequested()
    {
        VideoMediaElement.Pause();
    }

    private void OnStopRequested()
    {
        VideoMediaElement.Stop();
        VideoMediaElement.Position = TimeSpan.Zero;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (VideoMediaElement.Source == null) return;

        // Only update UI values if the media has a valid duration and user is not actively dragging the seek bar
        if (VideoMediaElement.NaturalDuration.HasTimeSpan && !_userIsDraggingSlider)
        {
            _isUpdatingPosition = true;

            var currentSeconds = VideoMediaElement.Position.TotalSeconds;
            var totalSeconds = VideoMediaElement.NaturalDuration.TimeSpan.TotalSeconds;

            if (DataContext is VideoPlayerViewModel vm)
            {
                vm.Position = currentSeconds;
                vm.Duration = totalSeconds;
                vm.CurrentTimeText = VideoMediaElement.Position.ToString(@"mm\:ss");
                vm.TotalTimeText = VideoMediaElement.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
            }

            _isUpdatingPosition = false;
        }
    }

    private void VideoMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoMediaElement.NaturalDuration.HasTimeSpan && DataContext is VideoPlayerViewModel vm)
        {
            vm.Duration = VideoMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
            vm.TotalTimeText = VideoMediaElement.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
            vm.CurrentTimeText = TimeSpan.Zero.ToString(@"mm\:ss");
        }
    }

    private void VideoMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (DataContext is VideoPlayerViewModel vm)
        {
            vm.IsPlaying = false;
            vm.Position = 0;
            vm.CurrentTimeText = TimeSpan.Zero.ToString(@"mm\:ss");
        }
        VideoMediaElement.Stop();
        VideoMediaElement.Position = TimeSpan.Zero;
    }

    private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _userIsDraggingSlider = true;
    }

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _userIsDraggingSlider = false;
        // User finished drag; perform absolute seek on the MediaElement
        VideoMediaElement.Position = TimeSpan.FromSeconds(PositionSlider.Value);
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Support instant-click seeks on the slider track without triggering feedback loops
        if (!_isUpdatingPosition && !_userIsDraggingSlider)
        {
            VideoMediaElement.Position = TimeSpan.FromSeconds(e.NewValue);
        }
    }

    private void OnFullscreenToggleRequested()
    {
        if (_fullscreenWindow == null)
        {
            OpenFullscreen();
        }
        else
        {
            CloseFullscreen();
        }
    }

    private void OpenFullscreen()
    {
        _fullscreenWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            Topmost = true,
            Background = Brushes.Black,
            Title = "GrokVideoStudio Fullscreen Player"
        };

        // Exit fullscreen if user hits the Escape key
        _fullscreenWindow.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseFullscreen();
            }
        };

        // Safely transfer MediaElement from Page's tree to the new fullscreen Window's tree
        _originalParent = PlayerDisplayGrid;
        _originalParent.Children.Remove(VideoMediaElement);

        _fullscreenWindow.Content = VideoMediaElement;
        _fullscreenWindow.Show();
    }

    private void CloseFullscreen()
    {
        if (_fullscreenWindow == null) return;

        // Tear down fullscreen window and restore MediaElement to the page
        _fullscreenWindow.Content = null;
        _fullscreenWindow.Close();
        _fullscreenWindow = null;

        if (_originalParent != null && !_originalParent.Children.Contains(VideoMediaElement))
        {
            _originalParent.Children.Add(VideoMediaElement);
        }
    }
}
