namespace Washmachine.Models;

public sealed record MsvcToolchain(
    string VcVarsPath,
    string ClPath,
    string InstallationRoot,
    string VersionLabel);
