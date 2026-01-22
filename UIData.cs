using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Washmachine.Models;

/// <summary>
/// Snapshot of a WinUI control tree, keyed by element name with fallback to type name.
/// </summary>
public sealed class UiData
{
    public Dictionary<string, string> TextBoxes { get; } = new();
    public Dictionary<string, string> ComboBoxes { get; } = new();
    public Dictionary<string, List<string>> ListBoxes { get; } = new();

    public UiData(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var element in EnumerateAllElements(root))
        {
            string key = string.IsNullOrWhiteSpace(element.Name) ? element.GetType().Name : element.Name;

            switch (element)
            {
                case TextBox textBox:
                    TextBoxes[key] = textBox.Text ?? string.Empty;
                    break;
                case ComboBox comboBox:
                    ComboBoxes[key] = comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? string.Empty;
                    break;
                case ListBox listBox:
                    ListBoxes[key] = listBox.SelectedItems
                        .Cast<object>()
                        .Select(item => item?.ToString() ?? string.Empty)
                        .ToList();
                    break;
                case ListView listView:
                    ListBoxes[key] = listView.SelectedItems
                        .Cast<object>()
                        .Select(item => item?.ToString() ?? string.Empty)
                        .ToList();
                    break;
            }
        }
    }

    private static IEnumerable<FrameworkElement> EnumerateAllElements(FrameworkElement root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is FrameworkElement element)
                yield return element;

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                stack.Push(VisualTreeHelper.GetChild(current, i));
            }
        }
    }
}
