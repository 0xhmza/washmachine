/********************************************************************************************
 *                                ██████╗ ██╗   ██╗██╗██████╗  █████╗ ████████╗ █████╗      
 *                                ██╔══██╗██║   ██║██║██╔══██╗██╔══██╗╚══██╔══╝██╔══██╗     
 *                                ██████╔╝██║   ██║██║██████╔╝███████║   ██║   ███████║     
 *                                ██╔═══╝ ██║   ██║██║██╔══██╗██╔══██║   ██║   ██╔══██║     
 *                                ██║     ╚██████╔╝██║██║  ██║██║  ██║   ██║   ██║  ██║     
 *                                ╚═╝      ╚═════╝ ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝   ╚═╝  ╚═╝     
 *                                                                                          
 *  UiData – A snapshot of the Form’s state, distilled into clean dictionaries.             
 *                                                                                          
 *  ┌───────────────────────────────────────────────────────────────────────────────┐      
 *  │ PURPOSE                                                                       │      
 *  │   Collect all user input from the form controls into structured containers.   │      
 *  │   Each dictionary is keyed by control.Name (or fallback type name).           │      
 *  │                                                                               │      
 *  │   This allows you to serialize, debug, or compile the UI state with ease.     │      
 *  └───────────────────────────────────────────────────────────────────────────────┘      
 *                                                                                          
 *  ┌───────────────────────────────────────────────────────────────────────────────┐      
 *  │ DATA STRUCTURE OVERVIEW                                                       │      
 *  │                                                                               │      
 *  │   TextBoxes   : Dictionary<string, string>                                    │      
 *  │                 • key   → Control name                                        │      
 *  │                 • value → TextBox.Text                                        │      
 *  │                                                                               │      
 *  │   ComboBoxes  : Dictionary<string, string>                                    │      
 *  │                 • key   → Control name                                        │      
 *  │                 • value → SelectedItem.ToString() or ComboBox.Text            │      
 *  │                                                                               │      
 *  │   ListBoxes   : Dictionary<string, List<string>>                              │      
 *  │                 • key   → Control name                                        │      
 *  │                 • value → All currently SELECTED items as strings             │      
 *  └───────────────────────────────────────────────────────────────────────────────┘      
 *                                                                                          
 *  Think of UiData as your **black box recorder** for the form.                     
 *  When the user hits “Submit”, every important control’s state is captured here.   
 *                                                                                          
 ********************************************************************************************/
// Sry for the unnecessary drama lol

namespace Washmachine.Models;

public sealed class UiData
{
    public Dictionary<string, string> TextBoxes { get; } = new();          // name -> text
    public Dictionary<string, string> ComboBoxes { get; } = new();         // name -> selected text (or Text if not bound)
    public Dictionary<string, List<string>> ListBoxes { get; } = new();    // name -> selected items

    public UiData(Control root)
    {
        foreach (var c in EnumerateAllControls(root))
        {
            // Prefer the control's Name; fall back to the type name
            string key = string.IsNullOrWhiteSpace(c.Name) ? c.GetType().Name : c.Name;

            if (c is TextBox tb)
            {
                TextBoxes[key] = tb.Text;
            }
            else if (c is ComboBox cb)
            {
                // Selected item if available; otherwise the ComboBox.Text
                ComboBoxes[key] = cb.SelectedItem?.ToString() ?? cb.Text ?? string.Empty;
            }
            else if (c is ListBox lb)
            {
                // Collect the SELECTED items
                var items = lb.SelectedItems.Cast<object>()
                                            .Select(o => o?.ToString() ?? string.Empty)
                                            .ToList();
                ListBoxes[key] = items;
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
            // yield after pushing children keeps root included; order isn't important here
            yield return current;

            foreach (Control child in current.Controls)
                stack.Push(child);
        }
    }
}
