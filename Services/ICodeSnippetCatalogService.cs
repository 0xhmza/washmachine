using System.Collections.Generic;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICodeSnippetCatalogService
{
    IReadOnlyList<CodeSnippetSection> GetAllSections();
    CodeSnippetSection GetSectionByHeader(string header);
    bool TryGetSectionByHeader(string header, out CodeSnippetSection section);
    IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header);
    bool TryGetSectionByTemplate(string template, out CodeSnippetSection section);
    IReadOnlyList<CodeTemplateDefinition> GetTemplates();
    CodeTemplateDefinition GetTemplate(string templateId);
    bool TryGetTemplate(string templateId, out CodeTemplateDefinition template);
}
