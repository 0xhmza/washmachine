using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Creates <see cref="UiData"/> snapshots from WinUI visual trees.
/// This factory lives in the GUI layer because it depends on WinUI types.
/// </summary>
public static class UiDataFactory
{
    public static UiData FromVisualTree(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var textBoxes = new Dictionary<string, string>();
        var comboBoxes = new Dictionary<string, string>();
        var listBoxes = new Dictionary<string, List<string>>();

        foreach (var control in EnumerateAllControls(root))
        {
            string key = control is FrameworkElement element && !string.IsNullOrWhiteSpace(element.Name)
                ? element.Name
                : control.GetType().Name;

            switch (control)
            {
                case TextBox textBox:
                    textBoxes[key] = textBox.Text ?? string.Empty;
                    break;
                case NumberBox numberBox:
                    textBoxes[key] = numberBox.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case CheckBox checkBox:
                    textBoxes[key] = checkBox.IsChecked == true
                        ? bool.TrueString
                        : bool.FalseString;
                    break;
                case ComboBox comboBox:
                    comboBoxes[key] = comboBox.SelectedValue?.ToString()
                                      ?? comboBox.SelectedItem?.ToString()
                                      ?? string.Empty;
                    break;
                case ListBox listBox:
                    var items = listBox.SelectedItems.Cast<object>()
                        .Select(item => item?.ToString() ?? string.Empty)
                        .ToList();
                    listBoxes[key] = items;
                    break;
            }
        }

        return new UiData(textBoxes, comboBoxes, listBoxes);
    }

    private static IEnumerable<DependencyObject> EnumerateAllControls(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
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
