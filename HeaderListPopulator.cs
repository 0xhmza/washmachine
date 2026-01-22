namespace Washmachine.Services;

public interface IHeaderListProvider
{
    void PopulateComboFromHeaderSection(ComboBox combo, string sectionName);
    void PopulateListFromHeaderSection(ListBox list, string sectionName);
}

public sealed class HeaderListProvider : IHeaderListProvider
{
    private readonly ICodeSnippetCatalogService _catalog;

    public HeaderListProvider(ICodeSnippetCatalogService catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void PopulateComboFromHeaderSection(ComboBox combo, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(combo);
        var items = LoadEntries(sectionName);

        combo.BeginUpdate();
        try
        {
            combo.DisplayMember = nameof(SnippetListEntry.Display);
            combo.Items.Clear();

            foreach (var entry in items)
            {
                combo.Items.Add(entry);
            }

            combo.Items.Add(SnippetListEntry.Empty);
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            combo.EndUpdate();
        }
    }

    public void PopulateListFromHeaderSection(ListBox list, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(list);
        var items = LoadEntries(sectionName);

        list.BeginUpdate();
        try
        {
            list.DisplayMember = nameof(SnippetListEntry.Display);
            list.Items.Clear();

            foreach (var entry in items)
            {
                list.Items.Add(entry);
            }
        }
        finally
        {
            list.EndUpdate();
        }
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
