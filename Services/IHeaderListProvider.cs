using System.Windows.Forms;

namespace Washmachine.Services;

public interface IHeaderListProvider
{
    void PopulateComboFromHeaderSection(ComboBox combo, string sectionName);
    void PopulateListFromHeaderSection(ListBox list, string sectionName);
}
