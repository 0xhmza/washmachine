using Washmachine.Models;

namespace Washmachine.Services;

public enum TemplateScanSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One actionable finding produced by <see cref="TemplateScannerService"/>.
/// Either a per-template issue (Scope=Template), a per-snippet issue (Scope=Snippet),
/// or a catalog-wide issue (Scope=Catalog).
/// </summary>
public sealed record TemplateScanFinding(
    TemplateScanSeverity Severity,
    string Code,
    string Scope,
    string TargetId,
    string Message);

public sealed record TemplateScanReport(
    IReadOnlyList<TemplateScanFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == TemplateScanSeverity.Error);
    public int ErrorCount => Findings.Count(f => f.Severity == TemplateScanSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == TemplateScanSeverity.Warning);
    public int InfoCount => Findings.Count(f => f.Severity == TemplateScanSeverity.Info);
}

public interface ITemplateScannerService
{
    TemplateScanReport Scan();
}

/// <summary>
/// Static analyzer for the snippet/template catalog. Cross-references every
/// snippet item's <c>requires:</c> contract against every template's declared
/// placeholders to surface conflicts before a build is attempted.
///
/// Findings include:
///   E001 — snippet declares <c>requires: [&lt;token&gt;]</c> but the requirement maps
///          to a section template that does not exist in the catalog.
///   E002 — template exposes a snippet placeholder whose section template is
///          missing from the catalog (broken placeholder).
///   E003 — template has at least one snippet item declaring
///          <c>requires: [uac_bypass]</c> reachable through its placeholders, yet
///          the template itself does not declare a UAC_BYPASS placeholder.
///          (Templates that surface evasion-style snippets MUST also surface UAC.)
///   W001 — snippet declares an unknown <c>requires:</c> token (reserved for future).
///   W002 — section's stub item id is not the conventional "None".
/// </summary>
public sealed class TemplateScannerService : ITemplateScannerService
{
    private readonly IPlaybookService _catalog;

    /// <summary>
    /// Capability-token → required snippet section template (case-insensitive).
    /// Kept in sync with the matching map in <c>CompilerService</c>.
    /// </summary>
    private static readonly Dictionary<string, string> RequiresTokenToSectionTemplate =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["uac_bypass"] = "uacb",
        };

    private const string StubSelectionId = "None";

    public TemplateScannerService(IPlaybookService catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public TemplateScanReport Scan()
    {
        var findings = new List<TemplateScanFinding>();
        var sections = _catalog.GetAllSections();
        var templates = _catalog.GetTemplates();

        var sectionsByTemplate = new Dictionary<string, CodeSnippetSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Template))
                continue;
            sectionsByTemplate[section.Template] = section;
        }

        ScanRequiresTokens(findings, sections);
        ScanStubConvention(findings, sections);
        ScanTemplatePlaceholders(findings, templates, sectionsByTemplate);

        return new TemplateScanReport(findings);
    }

    private static void ScanRequiresTokens(
        List<TemplateScanFinding> findings,
        IReadOnlyList<CodeSnippetSection> sections)
    {
        foreach (var section in sections)
        {
            foreach (var item in section.Items)
            {
                if (item.Requires.Count == 0)
                    continue;

                foreach (var token in item.Requires)
                {
                    if (RequiresTokenToSectionTemplate.ContainsKey(token))
                        continue;

                    findings.Add(new TemplateScanFinding(
                        TemplateScanSeverity.Warning,
                        Code: "W001",
                        Scope: "Snippet",
                        TargetId: $"{section.Template}:{item.Id}",
                        Message: $"Unknown requires token '{token}'. Reserved for future use; will be ignored at compile time."));
                }
            }
        }
    }

    private static void ScanStubConvention(
        List<TemplateScanFinding> findings,
        IReadOnlyList<CodeSnippetSection> sections)
    {
        foreach (var section in sections)
        {
            // Only sections that include a default-skip option benefit from the stub
            // convention. Heuristic: if any item is marked default AND the section
            // has more than one item, the default item should be id="None".
            if (section.Items.Count <= 1)
                continue;

            var defaultItem = section.Items.FirstOrDefault(i => i.IsDefault);
            if (defaultItem == null)
                continue;

            if (string.Equals(defaultItem.Id, StubSelectionId, StringComparison.OrdinalIgnoreCase))
                continue;

            findings.Add(new TemplateScanFinding(
                TemplateScanSeverity.Info,
                Code: "I001",
                Scope: "Section",
                TargetId: section.Template,
                Message: $"Default item '{defaultItem.Id}' is not '{StubSelectionId}'. Compiler treats only id='{StubSelectionId}' as a stub for requires-contract checks."));
        }
    }

    private static void ScanTemplatePlaceholders(
        List<TemplateScanFinding> findings,
        IReadOnlyList<CodeTemplateDefinition> templates,
        IReadOnlyDictionary<string, CodeSnippetSection> sectionsByTemplate)
    {
        foreach (var template in templates)
        {
            // Snippet placeholders whose section template is not in the catalog.
            foreach (var placeholder in template.Placeholders)
            {
                if (placeholder.Kind != TemplatePlaceholderKind.Snippet)
                    continue;
                if (string.IsNullOrWhiteSpace(placeholder.SnippetTemplateKey))
                    continue;
                if (sectionsByTemplate.ContainsKey(placeholder.SnippetTemplateKey))
                    continue;

                findings.Add(new TemplateScanFinding(
                    TemplateScanSeverity.Error,
                    Code: "E002",
                    Scope: "Template",
                    TargetId: template.Id,
                    Message: $"Placeholder '{placeholder.Name}' references missing snippet section '{placeholder.SnippetTemplateKey}'."));
            }

            ScanReachableRequires(findings, template, sectionsByTemplate);
        }
    }

    /// <summary>
    /// For each snippet section the template exposes, look at every item that
    /// declares <c>requires:</c> and verify the dependency's section is ALSO
    /// exposed by the same template.
    /// E003 (Error)   — every non-stub item in the section requires the missing
    ///                  capability; the user is FORCED into a conflict.
    /// W003 (Warning) — only some non-stub items require it; the user can avoid
    ///                  the conflict by choosing a different item.
    /// </summary>
    private static void ScanReachableRequires(
        List<TemplateScanFinding> findings,
        CodeTemplateDefinition template,
        IReadOnlyDictionary<string, CodeSnippetSection> sectionsByTemplate)
    {
        var exposedSectionTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placeholder in template.Placeholders)
        {
            if (placeholder.Kind == TemplatePlaceholderKind.Snippet &&
                !string.IsNullOrWhiteSpace(placeholder.SnippetTemplateKey))
            {
                exposedSectionTemplates.Add(placeholder.SnippetTemplateKey.Trim());
            }
        }

        foreach (var sectionTemplate in exposedSectionTemplates)
        {
            if (!sectionsByTemplate.TryGetValue(sectionTemplate, out var section))
                continue;

            // Gather the (token → conflicting items) map for items that require
            // a capability whose section is NOT exposed by this template.
            var unsatisfiable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Count non-stub items in the section so we can decide forced vs. avoidable.
            var nonStubItems = section.Items
                .Where(i => !string.Equals(i.Id, StubSelectionId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var item in nonStubItems)
            {
                if (item.Requires.Count == 0)
                    continue;

                foreach (var token in item.Requires)
                {
                    if (!RequiresTokenToSectionTemplate.TryGetValue(token, out var requiredSection))
                    {
                        findings.Add(new TemplateScanFinding(
                            TemplateScanSeverity.Error,
                            Code: "E001",
                            Scope: "Snippet",
                            TargetId: $"{section.Template}:{item.Id}",
                            Message: $"Required token '{token}' has no section template mapping. The compiler cannot enforce this dependency."));
                        continue;
                    }

                    if (exposedSectionTemplates.Contains(requiredSection))
                        continue;

                    var key = $"{token}::{requiredSection}";
                    if (!unsatisfiable.TryGetValue(key, out var ids))
                    {
                        ids = new List<string>();
                        unsatisfiable[key] = ids;
                    }
                    ids.Add(item.Id);
                }
            }

            foreach (var (key, ids) in unsatisfiable)
            {
                var parts = key.Split("::");
                var token = parts[0];
                var requiredSection = parts[1];

                bool everyItemConflicts = nonStubItems.Count > 0 &&
                                          ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == nonStubItems.Count;

                if (everyItemConflicts)
                {
                    findings.Add(new TemplateScanFinding(
                        TemplateScanSeverity.Error,
                        Code: "E003",
                        Scope: "Template",
                        TargetId: template.Id,
                        Message: $"Template exposes '{section.Template}' but every non-stub item requires '{token}', and the template does not expose the matching '{requiredSection}' placeholder. Add a {requiredSection.ToUpperInvariant()} placeholder or drop the {section.Template} placeholder."));
                }
                else
                {
                    var idList = string.Join(", ", ids.Distinct(StringComparer.OrdinalIgnoreCase));
                    findings.Add(new TemplateScanFinding(
                        TemplateScanSeverity.Warning,
                        Code: "W003",
                        Scope: "Template",
                        TargetId: template.Id,
                        Message: $"Template exposes '{section.Template}' items [{idList}] that require '{token}', but the template does not expose '{requiredSection}'. Picking those items will fail at compile time. Either add a {requiredSection.ToUpperInvariant()} placeholder or pick a different item from '{section.Template}'."));
                }
            }
        }
    }
}
