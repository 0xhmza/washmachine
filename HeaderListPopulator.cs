using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Washmachine.Services;

public sealed class HeaderListProvider : IHeaderListProvider
{
    private readonly ICodeSnippetCatalogService _catalog;

    public HeaderListProvider(ICodeSnippetCatalogService catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void PopulateComboFromHeaderSection(ComboBox combo, string sectionName)
    {
        if (combo == null) throw new ArgumentNullException(nameof(combo));
        var items = LoadEntries(sectionName);

        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.AddRange(items.ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        combo.EndUpdate();
    }

    public void PopulateListFromHeaderSection(ListBox list, string sectionName)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        var items = LoadEntries(sectionName);

        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(items.ToArray());
        list.EndUpdate();
    }

    private List<string> LoadEntries(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name is required", nameof(sectionName));

        var section = _catalog.GetSectionByHeader(sectionName);
        return section.Items.Select(item => item.Id).ToList();
    }
}
