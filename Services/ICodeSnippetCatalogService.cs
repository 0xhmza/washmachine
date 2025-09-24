using System.Collections.Generic;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICodeSnippetCatalogService
{
    CodeSnippetSection GetSectionByHeader(string header);
    bool TryGetSectionByHeader(string header, out CodeSnippetSection section);
    IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header);
}
