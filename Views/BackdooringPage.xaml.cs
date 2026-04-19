using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class BackdooringPage : Page
{
    public static BackdooringPage? Instance { get; private set; }

    private readonly AppPaths _paths;
    private readonly PeBackdoorService _backdoorService;
    private readonly PeAnalyzerService _analyzerService;
    private PeInfo? _currentPeInfo;
    private PeAnalysisResult? _analysisResult;
    private int _estimatedPayloadSize = 4096; // Default estimated size

    public BackdooringPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;

        _paths = new AppPaths();
        var logger = new Logging.ConsoleLogger();
        _backdoorService = new PeBackdoorService(_paths, logger);
        _analyzerService = new PeAnalyzerService(logger);

        CarrierInvokeCombo.SelectionChanged += BackdoorOptions_Changed;
        EncryptionCombo.SelectionChanged += BackdoorOptions_Changed;
        EncryptionCombo.SelectionChanged += EncryptionCombo_SelectionChanged;
        PreserveEntryCheck.Checked += BackdoorOptionToggle_Changed;
        PreserveEntryCheck.Unchecked += BackdoorOptionToggle_Changed;
    }

    #region Public Properties for Pipeline

    public bool IsBackdooringEnabled => EnableBackdooringToggle.IsOn;

    public string? TargetPeFilePath => IsBackdooringEnabled && !string.IsNullOrEmpty(TargetPePath.Text)
        ? TargetPePath.Text
        : null;

    public PeInfo? CurrentPeInfo => _currentPeInfo;

    public PeAnalysisResult? AnalysisResult => _analysisResult;

    public InjectionMethod SelectedInjectionMethod => InjectionMethodCombo.SelectedIndex switch
    {
        0 => InjectionMethod.CodeCave,
        1 => InjectionMethod.NewSection,
        2 => InjectionMethod.SectionExtension,
        _ => InjectionMethod.NewSection
    };

    public CarrierInvoke SelectedCarrierInvoke => CarrierInvokeCombo.SelectedIndex switch
    {
        0 => CarrierInvoke.EntryPointHijack,
        1 => CarrierInvoke.EntryFunctionBackdoor,
        2 => CarrierInvoke.TlsCallback,
        _ => CarrierInvoke.EntryPointHijack
    };

    public bool PreserveOriginalEntry => PreserveEntryCheck.IsChecked == true;
    public bool PatchIat => PatchIatCheck.IsChecked == true;
    public bool RemoveSignature => RemoveSignatureCheck.IsChecked == true;
    public bool PatchSubsystemToGui => PatchSubsystemCheck.IsChecked == true;

    public bool PatchExit => PatchExitCheck.IsChecked == true;
    public bool DryRun => DryRunCheck.IsChecked == true;
    public string? XorKey => XorKeyInput?.Text.Trim() is { Length: > 0 } s ? s : null;
    public string? CustomSectionName => SectionNameInput?.Text.Trim() is { Length: > 0 } s ? s : null;
    public int CaveMinSize => (int)(CaveMinSizeBox?.Value is double v && !double.IsNaN(v) ? v : 64);

    public PayloadEncryption SelectedEncryption=> EncryptionCombo.SelectedIndex switch
    {
        1 => PayloadEncryption.Xor,
        2 => PayloadEncryption.Xor2,
        3 => PayloadEncryption.Rc4,
        _ => PayloadEncryption.None
    };

    public bool IsInjectionValid { get; private set; } = false;

    public void SetEstimatedPayloadSize(int size)
    {
        _estimatedPayloadSize = size;
        UpdateInjectionFeasibility();
    }

    public void ApplyRecipe(BackdoorRecipe recipe)
    {
        EnableBackdooringToggle.IsOn = recipe.Enabled;
        if (!string.IsNullOrEmpty(recipe.TargetPePath)) TargetPePath.Text = recipe.TargetPePath;

        if (Enum.TryParse<InjectionMethod>(recipe.InjectionMethod, out var im))
        {
            InjectionMethodCombo.SelectedIndex = im switch
            {
                InjectionMethod.CodeCave => 0,
                InjectionMethod.NewSection => 1,
                InjectionMethod.SectionExtension => 2,
                _ => 1
            };
        }
        if (Enum.TryParse<CarrierInvoke>(recipe.CarrierInvoke, out var ci))
        {
            CarrierInvokeCombo.SelectedIndex = ci switch
            {
                CarrierInvoke.EntryPointHijack => 0,
                CarrierInvoke.EntryFunctionBackdoor => 1,
                CarrierInvoke.TlsCallback => 2,
                _ => 0
            };
        }

        PreserveEntryCheck.IsChecked = recipe.PreserveEntry;
        PatchIatCheck.IsChecked = recipe.PatchIat;
        RemoveSignatureCheck.IsChecked = recipe.RemoveSignature;
        PatchSubsystemCheck.IsChecked = recipe.PatchSubsystem;
        PatchExitCheck.IsChecked = recipe.PatchExit;
        DryRunCheck.IsChecked = recipe.DryRun;

        if (recipe.XorKey != null && XorKeyInput != null) XorKeyInput.Text = recipe.XorKey;
        if (recipe.SectionName != null && SectionNameInput != null) SectionNameInput.Text = recipe.SectionName;
        if (CaveMinSizeBox != null) CaveMinSizeBox.Value = recipe.CaveMinSize;

        if (Enum.TryParse<PayloadEncryption>(recipe.Encryption, out var enc))
        {
            EncryptionCombo.SelectedIndex = enc switch
            {
                PayloadEncryption.Xor => 1,
                PayloadEncryption.Xor2 => 2,
                PayloadEncryption.Rc4 => 3,
                _ => 0
            };
        }
    }

    #endregion

    private void EnableBackdooringToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var enabled = EnableBackdooringToggle.IsOn;
        BackdoorConfigPanel.Opacity = enabled ? 1.0 : 0.4;
        BackdoorConfigPanel.IsHitTestVisible = enabled;
        UpdateInjectionFeasibility();
    }

    private async void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".dll");

        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            TargetPePath.Text = file.Path;
            await PerformDeepAnalysis(file.Path);
        }
    }

    private async Task PerformDeepAnalysis(string path)
    {
        AnalysisProgressPanel.Visibility = Visibility.Visible;
        PeInfoCard.Visibility = Visibility.Collapsed;

        try
        {
            // Get basic PE info for backward compatibility
            _currentPeInfo = await _backdoorService.AnalyzePeAsync(path);

            // Perform deep analysis
            _analysisResult = await _analyzerService.AnalyzeAsync(path);

            // Update quick info card
            UpdateQuickInfoCard();

            // Update deep analysis section
            UpdateDeepAnalysisSection();

            // Update injection feasibility
            UpdateInjectionFeasibility();

            DeepAnalysisExpander.IsExpanded = true;
        }
        catch (Exception ex)
        {
            PeInfoCard.Visibility = Visibility.Collapsed;
            _currentPeInfo = null;
            _analysisResult = null;

            ShowInjectionBlocked($"Failed to analyze PE file: {ex.Message}");
        }
        finally
        {
            AnalysisProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateQuickInfoCard()
    {
        if (_analysisResult == null) return;

        PeInfoCard.Visibility = Visibility.Visible;
        PeFileName.Text = _analysisResult.FileName;
        PeArchText.Text = _analysisResult.Is64Bit ? "x64" : "x86";
        PeTypeText.Text = _analysisResult.IsDll ? "DLL" : "EXE";

        // Security badges
        PeAslrBadge.Visibility = _analysisResult.Security?.HasAslr == true ? Visibility.Visible : Visibility.Collapsed;
        PeDepBadge.Visibility = _analysisResult.Security?.HasDep == true ? Visibility.Visible : Visibility.Collapsed;
        PeCfgBadge.Visibility = _analysisResult.Security?.HasCfg == true ? Visibility.Visible : Visibility.Collapsed;

        // Packer detection
        if (!string.IsNullOrEmpty(_analysisResult.PackerDetection))
        {
            PePackedBadge.Visibility = Visibility.Visible;
            PePackedText.Text = _analysisResult.PackerDetection;
        }
        else if (_analysisResult.OverallEntropy > 7.0)
        {
            PePackedBadge.Visibility = Visibility.Visible;
            PePackedText.Text = "Likely Packed";
        }
        else
        {
            PePackedBadge.Visibility = Visibility.Collapsed;
        }

        // Stats
        PeSectionsCount.Text = _analysisResult.Sections?.Count.ToString() ?? "0";
        PeEntryPoint.Text = $"0x{_analysisResult.OptionalHeader?.AddressOfEntryPoint ?? 0:X}";
        PeFileSize.Text = FormatFileSize(_analysisResult.FileSize);
        PeEntropy.Text = $"{_analysisResult.OverallEntropy:F1}";

        // Code caves
        var totalCaves = _analysisResult.Sections?.Sum(s => s.CodeCaves?.Count ?? 0) ?? 0;
        var largestCave = _analysisResult.Sections?
            .SelectMany(s => s.CodeCaves ?? new List<CodeCaveAnalysis>())
            .OrderByDescending(c => c.Size)
            .FirstOrDefault();

        PeCodeCaves.Text = totalCaves > 0
            ? $"{totalCaves} ({FormatFileSize(largestCave?.Size ?? 0)} max)"
            : "None";

        // Icon
        PeTypeIcon.Glyph = _analysisResult.IsDll ? "\uE943" : "\uE8A5";
    }

    private void UpdateDeepAnalysisSection()
    {
        if (_analysisResult == null) return;

        // ASCII Visualization
        AsciiVisualization.Text = GenerateAsciiVisualization();

        // Sections ListView
        UpdateSectionsListView();

        // Code caves ListView
        UpdateCodeCavesListView();

        // Imports
        UpdateImportsDisplay();

        // Security analysis
        UpdateSecurityAnalysis();
    }

    private string GenerateAsciiVisualization()
    {
        if (_analysisResult == null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        var totalSize = Math.Max(1, _analysisResult.FileSize);

        sb.AppendLine("======================================================================");
        sb.AppendLine($"PE FILE      : {_analysisResult.FileName}");
        sb.AppendLine($"ARCH         : {(_analysisResult.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}");
        sb.AppendLine($"TYPE         : {(_analysisResult.IsDll ? "DLL" : "EXE")}");
        sb.AppendLine($"FILE SIZE    : {FormatFileSize(_analysisResult.FileSize)}");
        sb.AppendLine($"MACHINE      : {_analysisResult.FileHeader?.MachineString ?? "Unknown"}");
        sb.AppendLine($"ENTRY POINT  : 0x{_analysisResult.OptionalHeader?.AddressOfEntryPoint ?? 0:X8}");
        sb.AppendLine($"IMAGE BASE   : 0x{_analysisResult.OptionalHeader?.ImageBase ?? 0:X16}");
        sb.AppendLine("======================================================================");
        sb.AppendLine("SECTIONS");
        sb.AppendLine("----------------------------------------------------------------------");

        foreach (var section in _analysisResult.Sections)
        {
            var ratio = section.RawSize / (double)totalSize;
            var bar = GenerateMiniBar(ratio, 48);
            var perms = GetPermissionString(section.Characteristics);
            sb.AppendLine(
                $"[{section.Name,-8}] VA=0x{section.VirtualAddress:X8} RAW={FormatFileSize(section.RawSize),8} " +
                $"PERM={perms} ENT={section.Entropy:F2}");
            sb.AppendLine($"  {bar}");

            if (section.CodeCaves.Count > 0)
            {
                sb.AppendLine($"  caves: {section.CodeCaves.Count} / {FormatFileSize(section.TotalCaveSpace)} total");
            }
        }

        sb.AppendLine("----------------------------------------------------------------------");
        var entropyStatus = _analysisResult.OverallEntropy > 7.0 ? "HIGH (likely packed)"
            : _analysisResult.OverallEntropy > 6.0 ? "MODERATE"
            : "NORMAL";
        sb.AppendLine($"OVERALL ENTROPY: {_analysisResult.OverallEntropy:F2}/8.00 [{entropyStatus}]");
        sb.AppendLine("======================================================================");

        return sb.ToString();
    }

    private static string GetPermissionString(uint characteristics)
    {
        var r = (characteristics & 0x40000000) != 0 ? "R" : "-";
        var w = (characteristics & 0x80000000) != 0 ? "W" : "-";
        var x = (characteristics & 0x20000000) != 0 ? "X" : "-";
        return $"{r}{w}{x}";
    }

    private void UpdateSectionsListView()
    {
        if (_analysisResult?.Sections == null) return;

        var sectionViewModels = _analysisResult.Sections.Select(s => new SectionViewModel
        {
            Name = s.Name,
            VirtualAddressHex = $"0x{s.VirtualAddress:X8}",
            SizeFormatted = FormatFileSize(s.RawSize),
            PermissionsString = GetPermissionString(s.Characteristics),
            EntropyFormatted = $"{s.Entropy:F1}",
            AsciiBar = GenerateMiniBar(s.Entropy / 8.0, 20),
            CaveInfo = s.CodeCaves?.Count > 0 ? $"{s.CodeCaves.Count} caves" : ""
        }).ToList();

        SectionsListView.ItemsSource = sectionViewModels;
    }

    private static string GenerateMiniBar(double ratio, int width)
    {
        ratio = Math.Max(0, Math.Min(1, ratio));
        var filled = (int)(ratio * width);
        return new string('#', filled) + new string('-', width - filled);
    }

    private void UpdateCodeCavesListView()
    {
        if (_analysisResult?.Sections == null) return;

        var allCaves = _analysisResult.Sections
            .SelectMany(s => (s.CodeCaves ?? new List<CodeCaveAnalysis>())
                .Select(c => new CodeCaveViewModel
                {
                    SectionName = s.Name,
                    AddressHex = $"0x{c.FileOffset:X8}",
                    SizeFormatted = FormatFileSize(c.Size),
                    StatusIcon = c.Size >= _estimatedPayloadSize ? "\uE73E" : "\uE711",
                    StatusColor = c.Size >= _estimatedPayloadSize
                        ? new SolidColorBrush(Colors.Green)
                        : new SolidColorBrush(Colors.Orange),
                    Assessment = c.Size >= _estimatedPayloadSize
                        ? "Suitable for payload"
                        : c.Size >= 100 ? "Usable" : "Too small"
                }))
            .OrderByDescending(c => ParseSize(c.SizeFormatted))
            .ToList();

        CodeCavesListView.ItemsSource = allCaves;

        var totalCaveSize = _analysisResult.Sections
            .SelectMany(s => s.CodeCaves ?? new List<CodeCaveAnalysis>())
            .Sum(c => c.Size);

        CodeCavesSummary.Text = $"({allCaves.Count} found, {FormatFileSize(totalCaveSize)} total)";
    }

    private static long ParseSize(string formatted)
    {
        // Simple parser for "1.2 KB" style strings
        var parts = formatted.Split(' ');
        if (parts.Length != 2) return 0;
        if (!double.TryParse(parts[0], out var num)) return 0;
        return parts[1] switch
        {
            "B" => (long)num,
            "KB" => (long)(num * 1024),
            "MB" => (long)(num * 1024 * 1024),
            "GB" => (long)(num * 1024 * 1024 * 1024),
            _ => 0
        };
    }

    private void UpdateImportsDisplay()
    {
        if (_analysisResult?.Imports == null) return;

        var importViewModels = _analysisResult.Imports.Select(dll => new ImportDllViewModel
        {
            DllName = dll.Name,
            FunctionCount = $"({dll.Functions?.Count ?? 0} functions)",
            Functions = dll.Functions?.Select(f => f.Name ?? $"Ordinal {f.Ordinal}").ToList() ?? new List<string>()
        }).ToList();

        ImportsItemsControl.ItemsSource = importViewModels;
        ImportsSummary.Text = $"({_analysisResult.Imports.Count} DLLs, {_analysisResult.Imports.Sum(d => d.Functions?.Count ?? 0)} functions)";
    }

    private void UpdateSecurityAnalysis()
    {
        if (_analysisResult?.Security == null) return;

        var security = _analysisResult.Security;
        var score = CalculateSecurityScore(security);

        SecurityScore.Text = score.ToString();
        SecurityProgressBar.Value = score;

        SecurityAssessment.Text = score switch
        {
            >= 80 => "Strong security posture - injection may be difficult",
            >= 60 => "Moderate security - some protections enabled",
            >= 40 => "Weak security - few protections",
            _ => "Minimal security - ideal for injection"
        };

        var protections = new List<ProtectionItem>
        {
            new() { Name = "ASLR (Address Randomization)", Icon = security.HasAslr ? "\uE73E" : "\uE711", Color = security.HasAslr ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "DEP (Data Execution Prevention)", Icon = security.HasDep ? "\uE73E" : "\uE711", Color = security.HasDep ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "CFG (Control Flow Guard)", Icon = security.HasCfg ? "\uE73E" : "\uE711", Color = security.HasCfg ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "Authenticode Signature", Icon = security.HasAuthenticode ? "\uE73E" : "\uE711", Color = security.HasAuthenticode ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "High Entropy VA", Icon = security.HasHighEntropyVa ? "\uE73E" : "\uE711", Color = security.HasHighEntropyVa ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "SafeSEH", Icon = security.HasSafeSeh ? "\uE73E" : "\uE711", Color = security.HasSafeSeh ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
            new() { Name = "Force Integrity Check", Icon = security.ForceIntegrity ? "\uE73E" : "\uE711", Color = security.ForceIntegrity ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red) },
        };

        ProtectionsItemsControl.ItemsSource = protections;
    }

    private static int CalculateSecurityScore(PeSecurityInfo security)
    {
        var score = 0;
        if (security.HasAslr) score += 15;
        if (security.HasDep) score += 15;
        if (security.HasCfg) score += 20;
        if (security.HasAuthenticode) score += 15;
        if (security.HasHighEntropyVa) score += 10;
        if (security.HasSafeSeh) score += 10;
        if (security.ForceIntegrity) score += 10;
        if (security.GuardCf) score += 5;
        return Math.Min(100, score);
    }

    private void UpdateInjectionFeasibility()
    {
        if (_analysisResult == null)
        {
            IsInjectionValid = false;
            return;
        }

        // Reset panels
        InjectionWarningPanel.Visibility = Visibility.Collapsed;
        InjectionBlockedPanel.Visibility = Visibility.Collapsed;
        RecommendedMethodPanel.Visibility = Visibility.Collapsed;

        var warnings = new List<string>();
        var hardBlocks = new List<string>();

        if (SelectedCarrierInvoke != CarrierInvoke.EntryPointHijack)
        {
            hardBlocks.Add("Only Entry Point Hijack is currently implemented. Function Backdoor and TLS carriers are not available yet.");
        }

        if (SelectedEncryption != PayloadEncryption.None)
        {
            hardBlocks.Add("Backdoor-stage encryption is not supported. Inject a ready-to-run flat .bin payload here.");
        }

        if (!PreserveOriginalEntry)
        {
            hardBlocks.Add("Disabling original entry-point preservation is not implemented. The current carrier always resumes the target's original entry point.");
        }

        if (hardBlocks.Count > 0)
        {
            ShowInjectionBlocked(string.Join("\n", hardBlocks));
            return;
        }

        // Check for .NET assembly
        if (_analysisResult.IsDotNet)
        {
            warnings.Add(".NET assembly detected: native payload injection may fail.");
        }

        // Check for packing
        if (!string.IsNullOrEmpty(_analysisResult.PackerDetection))
        {
            warnings.Add($"PE appears packed ({_analysisResult.PackerDetection}). Injection reliability is reduced.");
        }
        else if (_analysisResult.OverallEntropy > 7.2)
        {
            warnings.Add("High entropy suggests the PE may be packed or encrypted.");
        }

        // Check code cave feasibility
        var largestCave = _analysisResult.Sections?
            .SelectMany(s => s.CodeCaves ?? new List<CodeCaveAnalysis>())
            .OrderByDescending(c => c.Size)
            .FirstOrDefault();

        var codeCaveOk = largestCave != null && largestCave.Size >= _estimatedPayloadSize;

        // Method-specific checks
        var selectedMethod = SelectedInjectionMethod;
        switch (selectedMethod)
        {
            case InjectionMethod.CodeCave:
                if (!codeCaveOk)
                {
                    var caveSize = largestCave?.Size ?? 0;
                    if (caveSize > 0)
                        warnings.Add($"Largest code cave ({FormatFileSize(caveSize)}) is smaller than estimated payload ({FormatFileSize(_estimatedPayloadSize)}). Consider New Section method.");
                    else
                        warnings.Add("No code caves found. Consider New Section or Section Extension method.");
                }
                MethodFeasibilityText.Text = codeCaveOk
                    ? $"✓ Feasible - {FormatFileSize(largestCave?.Size ?? 0)} available"
                    : $"✗ Insufficient space - need {FormatFileSize(_estimatedPayloadSize)}";
                break;

            case InjectionMethod.NewSection:
                MethodFeasibilityText.Text = "✓ Always feasible - adds new .extra section";
                break;

            case InjectionMethod.SectionExtension:
                MethodFeasibilityText.Text = "✓ Usually feasible - extends .text section";
                break;
        }

        // Check for signature
        if (_analysisResult.Security?.HasAuthenticode == true)
        {
            warnings.Add("PE has Authenticode signature. It will be invalidated after modification.");
        }

        // Show warnings (do not block selection)
        IsInjectionValid = true;
        if (warnings.Count > 0)
        {
            InjectionWarningPanel.Visibility = Visibility.Visible;
            InjectionWarningTitle.Text = warnings.Count == 1 ? "Warning" : $"{warnings.Count} Warnings";
            InjectionWarningText.Text = string.Join("\n", warnings);
        }

        // Recommend best method
        if (IsInjectionValid)
        {
            string recommendation;
            if (codeCaveOk)
                recommendation = "Recommended: Code Cave injection - stealthiest option, no PE structure changes.";
            else
                recommendation = "Recommended: New Section injection - reliable, works with any PE.";

            RecommendedMethodPanel.Visibility = Visibility.Visible;
            RecommendedMethodText.Text = recommendation;
        }
    }

    private void InjectionMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInjectionFeasibility();
    }

    private void BackdoorOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateInjectionFeasibility();
    }

    private void BackdoorOptionToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateInjectionFeasibility();
    }

    private void EncryptionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (XorKeyPanel == null) return;
        var idx = EncryptionCombo.SelectedIndex;
        XorKeyPanel.Visibility = (idx == 1 || idx == 2) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowInjectionBlocked(string message)
    {
        InjectionBlockedPanel.Visibility = Visibility.Visible;
        InjectionBlockedText.Text = message;
        IsInjectionValid = false;
    }

    private void GoToPackingPage_Click(object sender, RoutedEventArgs e)
    {
        if (App.ActiveWindow is MainWindow mainWindow)
        {
            var navView = mainWindow.Content as NavigationView;
            if (navView != null)
            {
                var item = navView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == "PackingPage");
                if (item != null)
                {
                    navView.SelectedItem = item;
                }
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    #region View Models

    private class SectionViewModel
    {
        public string Name { get; set; } = "";
        public string VirtualAddressHex { get; set; } = "";
        public string SizeFormatted { get; set; } = "";
        public string PermissionsString { get; set; } = "";
        public string EntropyFormatted { get; set; } = "";
        public string AsciiBar { get; set; } = "";
        public string CaveInfo { get; set; } = "";
    }

    private class CodeCaveViewModel
    {
        public string SectionName { get; set; } = "";
        public string AddressHex { get; set; } = "";
        public string SizeFormatted { get; set; } = "";
        public string StatusIcon { get; set; } = "";
        public SolidColorBrush StatusColor { get; set; } = new(Colors.Gray);
        public string Assessment { get; set; } = "";
    }

    private class ImportDllViewModel
    {
        public string DllName { get; set; } = "";
        public string FunctionCount { get; set; } = "";
        public List<string> Functions { get; set; } = new();
    }

    private class ProtectionItem
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public SolidColorBrush Color { get; set; } = new(Colors.Gray);
    }

    #endregion
}
