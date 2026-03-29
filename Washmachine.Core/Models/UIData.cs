namespace Washmachine.Models;

/// <summary>
/// Snapshot of UI state, keyed by control name.
/// Use the dictionary constructor for headless/CLI scenarios.
/// For WinUI visual-tree extraction, use <c>UiDataFactory.FromVisualTree</c> in the GUI layer.
/// </summary>
public sealed class UiData
{
    public Dictionary<string, string> TextBoxes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ComboBoxes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> ListBoxes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public UiData(
        Dictionary<string, string> textBoxes,
        Dictionary<string, string> comboBoxes,
        Dictionary<string, List<string>>? listBoxes = null)
    {
        foreach (var kv in textBoxes) TextBoxes[kv.Key] = kv.Value;
        foreach (var kv in comboBoxes) ComboBoxes[kv.Key] = kv.Value;
        if (listBoxes != null)
            foreach (var kv in listBoxes) ListBoxes[kv.Key] = kv.Value;
    }
}
