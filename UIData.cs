using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Washmachine.Models;

/// <summary>
/// Snapshot of a WinUI 3 visual tree, keyed by control name with fallback to type name.
/// </summary>
public sealed class UiData
{
    public Dictionary<string, string> TextBoxes { get; } = new();
    public Dictionary<string, string> ComboBoxes { get; } = new();
    public Dictionary<string, List<string>> ListBoxes { get; } = new();

    public UiData(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var control in EnumerateAllControls(root))
        {
            string key = control is FrameworkElement element && !string.IsNullOrWhiteSpace(element.Name)
                ? element.Name
                : control.GetType().Name;

            switch (control)
            {
                case TextBox textBox:
                    TextBoxes[key] = textBox.Text ?? string.Empty;
                    break;
                case ComboBox comboBox:
                    ComboBoxes[key] = comboBox.SelectedValue?.ToString()
                                      ?? comboBox.SelectedItem?.ToString()
                                      ?? string.Empty;
                    break;
                case ListBox listBox:
                    var items = listBox.SelectedItems.Cast<object>()
                        .Select(item => item?.ToString() ?? string.Empty)
                        .ToList();
                    ListBoxes[key] = items;
                    break;
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateAllControls(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            // Depth-first walk keeps memory small; order is irrelevant for snapshots.
            yield return current;

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child != null)
                    stack.Push(child);
            }
        }
    }
}
