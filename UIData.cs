namespace Washmachine.Models;

/// <summary>
/// Snapshot of a WinForms control tree, keyed by control name with fallback to type name.
/// </summary>
public sealed class UiData
{
    public Dictionary<string, string> TextBoxes { get; } = new();
    public Dictionary<string, string> ComboBoxes { get; } = new();
    public Dictionary<string, List<string>> ListBoxes { get; } = new();

    public UiData(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var control in EnumerateAllControls(root))
        {
            string key = string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : control.Name;

            switch (control)
            {
                case TextBox textBox:
                    TextBoxes[key] = textBox.Text;
                    break;
                case ComboBox comboBox:
                    ComboBoxes[key] = comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? string.Empty;
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

    private static IEnumerable<Control> EnumerateAllControls(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            // Depth-first walk keeps memory small; order is irrelevant for snapshots.
            yield return current;

            foreach (Control child in current.Controls)
                stack.Push(child);
        }
    }
}
