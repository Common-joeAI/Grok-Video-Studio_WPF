using System.Windows;
using System.Windows.Controls;

namespace GrokVideoStudio.App.Helpers;

/// <summary>
/// Attached property to add Spacing support to StackPanel in WPF.
/// Usage: &lt;StackPanel helpers:StackPanelHelper.Spacing="8"&gt;
/// </summary>
public static class StackPanelHelper
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.RegisterAttached(
            "Spacing",
            typeof(double),
            typeof(StackPanelHelper),
            new PropertyMetadata(0.0, OnSpacingChanged));

    public static double GetSpacing(DependencyObject obj)
    {
        return (double)obj.GetValue(SpacingProperty);
    }

    public static void SetSpacing(DependencyObject obj, double value)
    {
        obj.SetValue(SpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StackPanel stackPanel)
        {
            stackPanel.Loaded -= StackPanel_Loaded;
            stackPanel.Loaded += StackPanel_Loaded;

            if (stackPanel.IsLoaded)
            {
                ApplySpacing(stackPanel);
            }
        }
    }

    private static void StackPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is StackPanel stackPanel)
        {
            ApplySpacing(stackPanel);
        }
    }

    private static void ApplySpacing(StackPanel stackPanel)
    {
        double spacing = GetSpacing(stackPanel);

        if (spacing <= 0)
            return;

        for (int i = 0; i < stackPanel.Children.Count; i++)
        {
            if (stackPanel.Children[i] is FrameworkElement child)
            {
                if (stackPanel.Orientation == Orientation.Horizontal)
                {
                    // Add spacing to the right of all elements except the last one
                    if (i < stackPanel.Children.Count - 1 )
                    {
                        var margin = child.Margin;
                        child.Margin = new Thickness(margin.Left, margin.Top, margin.Right + spacing, margin.Bottom);
                    }
                }
                else // Vertical
                {
                    // Add spacing to the bottom of all elements except the last one
                    if (i < stackPanel.Children.Count - 1)
                    {
                        var margin = child.Margin;
                        child.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + spacing);
                    }
                }
            }
        }
    }
}
