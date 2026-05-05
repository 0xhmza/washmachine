namespace Washmachine.Models;

/// <summary>
/// Authoritative table of <c>requires:</c> capability tokens that snippet items
/// can declare in the YAML catalog, mapped to the section template they imply
/// must also be selected.
///
/// Adding a new token here is the contract: validation runs at catalog load,
/// so a token used in YAML but missing from this map is a hard error rather
/// than a silently-reserved no-op.
/// </summary>
public static class KnownRequiresTokens
{
    public static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["uac_bypass"] = "UACB",
        };
}
