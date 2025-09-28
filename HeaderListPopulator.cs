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
        combo.DisplayMember = nameof(SnippetListEntry.Display);
        combo.Items.Clear();

        foreach (var entry in items)
        {
            combo.Items.Add(entry);
        }

        combo.Items.Add(SnippetListEntry.Empty);
        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        combo.EndUpdate();
    }

    public void PopulateListFromHeaderSection(ListBox list, string sectionName)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        var items = LoadEntries(sectionName);

        list.BeginUpdate();
        list.DisplayMember = nameof(SnippetListEntry.Display);
        list.Items.Clear();

        foreach (var entry in items)
        {
            list.Items.Add(entry);
        }

        list.EndUpdate();
    }

    private IReadOnlyList<SnippetListEntry> LoadEntries(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name is required", nameof(sectionName));

        var section = _catalog.GetSectionByHeader(sectionName);
        return section.Items
            .Select(item => new SnippetListEntry(item.Id, item.Display))
            .ToList();
    }

    private sealed class SnippetListEntry
    {
        public static readonly SnippetListEntry Empty = new(string.Empty, string.Empty);

        public SnippetListEntry(string id, string display)
        {
            Id = id ?? string.Empty;
            Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        }

        public string Id { get; }
        public string Display { get; }

        public override string ToString() => Id;
    }
}
