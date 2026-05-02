using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Washmachine.Models;
using Washmachine.Services;
using WinRT.Interop;
using Path = System.IO.Path;

namespace Washmachine.Views;

public sealed partial class PipelinePage : Page
{
    private readonly AppPaths _paths = new();
    private readonly ICodeSnippetCatalogService _snippets;

    public PipelinePage()
    {
        InitializeComponent();
        _snippets = new YamlCodeSnippetCatalogService(_paths);

        Loaded += (_, _) => Render();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Render();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();

    private async void ExportRecipe_Click(object sender, RoutedEventArgs e)
    {
        var compile = CompilePage.Instance;
        if (compile == null)
        {
            await ShowRecipeUnavailableDialogAsync("export");
            return;
        }
        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        if (await compile.ExportRecipeAsync(hwnd))
            TelemetryLine.Text = "Recipe exported successfully.";
    }

    private async void ImportRecipe_Click(object sender, RoutedEventArgs e)
    {
        var compile = CompilePage.Instance;
        if (compile == null)
        {
            await ShowRecipeUnavailableDialogAsync("import");
            return;
        }
        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        if (await compile.ImportRecipeAsync(hwnd))
        {
            TelemetryLine.Text = "Recipe imported — re-syncing.";
            Render();
        }
    }

    private async Task ShowRecipeUnavailableDialogAsync(string action)
    {
        var dlg = new ContentDialog
        {
            Title = "Recipe service not initialized",
            Content = $"Open the Compile page once before you {action} a recipe — that step boots the recipe pipeline.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dlg.ShowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Render
    // ═══════════════════════════════════════════════════════════════════════
    private void Render()
    {
        if (StepsList == null) return;

        StepsList.Items.Clear();
        SpecPanelHost.Children.Clear();

        var snap = Snapshot.Capture(_snippets);

        // Header / summary
        RecipeSummaryText.Text = string.IsNullOrWhiteSpace(snap.RecipeOneLiner)
            ? "Configure stages on the other pages to build your recipe."
            : snap.RecipeOneLiner;

        // Stage count chip
        int active = snap.Stages.Count(s => s.Enabled);
        StageCountText.Text = $"{active} of {snap.Stages.Count} active";

        // Warning bar
        if (snap.Warnings.Count > 0)
        {
            WarningBar.Title   = "Pipeline review required";
            WarningBar.Message = string.Join("\n", snap.Warnings);
            WarningBar.IsOpen  = true;
        }
        else
        {
            WarningBar.IsOpen = false;
        }

        // Stage cards
        for (int i = 0; i < snap.Stages.Count; i++)
            StepsList.Items.Add(BuildStageCard(i + 1, snap.Stages[i], isLast: i == snap.Stages.Count - 1));

        // Spec sidebar
        BuildSpecSidebar(snap);

        // Status line
        TelemetryLine.Text =
            $"Stages: {snap.Stages.Count}  ·  Active: {active}  ·  Artifacts: {snap.ExpectedArtifacts.Count}  ·  Warnings: {snap.Warnings.Count}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stage card builder — uses native WinUI ThemeResource styling
    // ═══════════════════════════════════════════════════════════════════════
    private FrameworkElement BuildStageCard(int index, Stage stage, bool isLast)
    {
        var container = new StackPanel();

        var accentBrush = stage.Severity switch
        {
            Severity.Warning => (Brush)Resources["PipeWarnBrush"],
            Severity.Skipped => (Brush)Resources["PipeSkipBrush"],
            _                => (Brush)Resources["PipeAccentBrush"],
        };

        // Card
        var card = new Border
        {
            Background      = (Brush)Resources["PipeCardBgBrush"],
            BorderBrush     = (Brush)Resources["PipeCardBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Opacity         = stage.Enabled ? 1.0 : 0.55,
        };

        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left accent rail
        var rail = new Border
        {
            Background          = accentBrush,
            CornerRadius        = new CornerRadius(5, 0, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Opacity             = stage.Severity == Severity.Skipped ? 0.3 : 0.7,
        };
        Grid.SetColumn(rail, 0);
        innerGrid.Children.Add(rail);

        // Step number
        var stepNumBlock = new TextBlock
        {
            Text                = $"{index:00}",
            FontSize            = 18,
            FontWeight          = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin              = new Thickness(4, 13, 4, 0),
            Foreground          = accentBrush,
        };
        Grid.SetColumn(stepNumBlock, 1);
        innerGrid.Children.Add(stepNumBlock);

        // Body
        var body = new StackPanel { Spacing = 4, Margin = new Thickness(8, 12, 14, 14) };

        // Title row: icon + name + status chip
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new FontIcon
        {
            Glyph               = stage.Glyph,
            FontSize            = 13,
            Foreground          = accentBrush,
            VerticalAlignment   = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text              = stage.Title,
            FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(stage.StatusChip))
        {
            titleRow.Children.Add(new Border
            {
                BorderBrush     = accentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(3),
                Padding         = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child           = new TextBlock
                {
                    Text       = stage.StatusChip,
                    FontSize   = 10,
                    Foreground = accentBrush,
                },
            });
        }
        body.Children.Add(titleRow);

        // Subtitle
        if (!string.IsNullOrWhiteSpace(stage.Subtitle))
        {
            body.Children.Add(new TextBlock
            {
                Text           = stage.Subtitle,
                FontSize       = 12,
                Foreground     = (Brush)Resources["PipeTextDimBrush"],
                TextWrapping   = TextWrapping.Wrap,
                Margin         = new Thickness(0, 2, 0, 0),
            });
        }

        // Params grid
        if (stage.Params.Count > 0)
        {
            var paramGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < stage.Params.Count; r++)
            {
                paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (k, v) = stage.Params[r];

                var keyTb = new TextBlock
                {
                    Text       = k,
                    FontSize   = 11,
                    FontFamily = MonoFont,
                    Foreground = (Brush)Resources["PipeTextDimBrush"],
                    Margin     = new Thickness(0, 1, 8, 1),
                };
                Grid.SetRow(keyTb, r);
                paramGrid.Children.Add(keyTb);

                var valTb = new TextBlock
                {
                    Text         = v,
                    FontSize     = 11,
                    FontFamily   = MonoFont,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 1, 0, 1),
                };
                Grid.SetRow(valTb, r);
                Grid.SetColumn(valTb, 1);
                paramGrid.Children.Add(valTb);
            }
            body.Children.Add(paramGrid);
        }

        // Command line code box
        if (!string.IsNullOrWhiteSpace(stage.CommandLine))
        {
            body.Children.Add(new Border
            {
                Background      = (Brush)Resources["PipeCodeBgBrush"],
                BorderBrush     = (Brush)Resources["PipeDividerBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(8, 5, 8, 5),
                Margin          = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text         = $"$ {stage.CommandLine}",
                    FontFamily   = MonoFont,
                    FontSize     = 11,
                    Foreground   = (Brush)Resources["PipeTextDimBrush"],
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        Grid.SetColumn(body, 2);
        innerGrid.Children.Add(body);

        card.Child = innerGrid;
        container.Children.Add(card);

        // Connector between cards (thin vertical line)
        if (!isLast)
        {
            container.Children.Add(new Border
            {
                Width               = 1,
                Height              = 14,
                Background          = (Brush)Resources["PipeDividerBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(28, 0, 0, 0),
            });
        }

        return container;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Spec sidebar — uses native WinUI Expander controls
    // ═══════════════════════════════════════════════════════════════════════
    private void BuildSpecSidebar(Snapshot snap)
    {
        AddSpecExpander("Target Signature",  snap.SignatureSpec,       isFirst: true);
        AddSpecExpander("Encoding",          snap.EncodingSpec);
        AddSpecExpander("Execution",         snap.ExecutionSpec);
        AddSpecExpander("Carrier",           snap.CarrierSpec);
        AddSpecExpander("Post-processing",   snap.PostProcessingSpec);
        AddSpecExpander("Compile Target",    snap.CompileSpec);
        AddSpecExpander("Artifacts",         snap.ArtifactSpec);
    }

    private void AddSpecExpander(string title, IReadOnlyList<KeyValuePair<string, string>> rows, bool isFirst = false)
    {
        var content = BuildSpecContent(rows);

        var expander = new Expander
        {
            Header                     = title,
            Content                    = content,
            IsExpanded                 = isFirst,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        SpecPanelHost.Children.Add(expander);
    }

    private FrameworkElement BuildSpecContent(IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };

        if (rows.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text       = "No data",
                FontSize   = 12,
                Foreground = (Brush)Resources["PipeTextDimBrush"],
                FontStyle  = Windows.UI.Text.FontStyle.Italic,
            });
            return stack;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (k, v) = (rows[r].Key, rows[r].Value);

            var keyTb = new TextBlock
            {
                Text              = k,
                FontSize          = 11,
                FontFamily        = MonoFont,
                Foreground        = (Brush)Resources["PipeTextDimBrush"],
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(0, 1, 8, 1),
            };
            Grid.SetRow(keyTb, r);
            grid.Children.Add(keyTb);

            var valTb = new TextBlock
            {
                Text         = v,
                FontSize     = 11,
                FontFamily   = MonoFont,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 1, 0, 1),
            };
            Grid.SetRow(valTb, r);
            Grid.SetColumn(valTb, 1);
            grid.Children.Add(valTb);
        }

        stack.Children.Add(grid);
        return stack;
    }

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    // ═══════════════════════════════════════════════════════════════════════
    // Snapshot — reads entire user config from all pages
    // ═══════════════════════════════════════════════════════════════════════
    private enum Severity { Normal, Warning, Skipped }

    private sealed record Stage(
        string Title,
        string Glyph,
        string? Subtitle,
        IReadOnlyList<(string Key, string Value)> Params,
        string? CommandLine,
        bool Enabled,
        Severity Severity,
        string? StatusChip);

    private sealed class Snapshot
    {
        public List<Stage> Stages { get; } = new();
        public List<KeyValuePair<string, string>> SignatureSpec      { get; } = new();
        public List<KeyValuePair<string, string>> EncodingSpec       { get; } = new();
        public List<KeyValuePair<string, string>> ExecutionSpec      { get; } = new();
        public List<KeyValuePair<string, string>> CarrierSpec        { get; } = new();
        public List<KeyValuePair<string, string>> PostProcessingSpec { get; } = new();
        public List<KeyValuePair<string, string>> CompileSpec        { get; } = new();
        public List<KeyValuePair<string, string>> ArtifactSpec       { get; } = new();
        public List<string> Warnings          { get; } = new();
        public List<string> ExpectedArtifacts { get; } = new();
        public string OutputArchHint  { get; set; } = "x64-PE";
        public string RecipeOneLiner  { get; set; } = "";

        public static Snapshot Capture(ICodeSnippetCatalogService snippets)
        {
            var snap     = new Snapshot();
            var main     = MainPage.Instance;
            var backdoor = BackdooringPage.Instance;
            var packing  = PackingPage.Instance;
            var finalize = FinalizePage.Instance;
            var compile  = CompilePage.Instance;

            // ─── Sidebar: Target Signature ──────────────────────────────
            var src      = main?.CurrentShellcodeSource;
            string srcLabel = "(unconfigured)";
            string srcPath  = "—";
            string srcSize  = "—";
            string srcKind  = src?.ToString() ?? "—";
            if (main != null)
            {
                switch (src)
                {
                    case ShellcodeSource.File:
                        srcPath = main.ShellcodeFileTextBox.Text.Trim();
                        if (!string.IsNullOrEmpty(srcPath))
                        {
                            srcLabel = Path.GetFileName(srcPath);
                            if (File.Exists(srcPath)) srcSize = $"{new FileInfo(srcPath).Length:N0} B";
                        }
                        else { srcLabel = "(file not selected)"; }
                        break;
                    case ShellcodeSource.Raw:
                        var hex = main.ShellcodeRawTextBox.Text.Trim();
                        srcLabel = $"raw-hex:{hex.Length / 2}B";
                        srcSize  = $"{hex.Length / 2:N0} B (decoded)";
                        srcPath  = "(in-memory)";
                        break;
                    case ShellcodeSource.Url:
                        srcPath  = main.ShellcodeUrlTextBox.Text.Trim();
                        srcLabel = string.IsNullOrEmpty(srcPath) ? "(url not set)" : "remote-fetch";
                        srcSize  = "(runtime-resolved)";
                        break;
                    case ShellcodeSource.Generic:
                        srcLabel = main.GenericShellcodeCombo.SelectedItem?.ToString() ?? "(generic)";
                        srcPath  = "(bundled)";
                        srcSize  = "(catalog)";
                        break;
                }
            }
            snap.SignatureSpec.Add(new("kind",     srcKind));
            snap.SignatureSpec.Add(new("label",    srcLabel));
            snap.SignatureSpec.Add(new("location", srcPath));
            snap.SignatureSpec.Add(new("size",     srcSize));

            // ─── Sidebar: Encoding ──────────────────────────────────────
            string sgnPlacement = "off";
            int sgnCount = 0, sgnMax = 0;
            if (main?.IsShikataGaNaiEnabled == true)
            {
                sgnPlacement = main.IsShikataGaNaiPostPlacement ? "post-bin2shell" : "pre-bin2shell";
                sgnCount = main.ShikataGaNaiEncodeCount;
                sgnMax   = main.ShikataGaNaiMaxBytes;
            }
            string encName = main?.EncoderCombo?.SelectedItem?.ToString() ?? "(default)";
            string envName = main?.EnvelopeCombo?.SelectedItem?.ToString() ?? "(default)";
            snap.EncodingSpec.Add(new("sgn",       sgnPlacement));
            snap.EncodingSpec.Add(new("sgn-iters", sgnPlacement == "off" ? "—" : $"x{sgnCount}, max {sgnMax} B"));
            snap.EncodingSpec.Add(new("encoder",   encName));
            snap.EncodingSpec.Add(new("envelope",  envName));
            if (main?.SelectedEncoderIndex is int ei)  snap.EncodingSpec.Add(new("enc-id", ei.ToString()));
            if (main?.SelectedEnvelopeIndex is int vi) snap.EncodingSpec.Add(new("env-id", vi.ToString()));

            // ─── Sidebar: Execution ─────────────────────────────────────
            string execId = "(unset)";
            string execMem = "—";
            string execApis = "—";
            string execSgnSafe = "—";
            string? execSummary = null;
            var options = main?.Coordinator?.TemplateOptions;
            if (options != null)
            {
                foreach (var (k, v) in options.ComboValues)
                {
                    if (string.IsNullOrEmpty(v)) continue;
                    var nk = (k ?? "").Replace(" ", "").ToLowerInvariant();
                    if (nk.Contains("shellcodeexec") || nk.Contains("execution"))
                    {
                        execId = v;
                        break;
                    }
                }
            }
            if (execId != "(unset)" &&
                (snippets.TryGetSectionByTemplate("shellcodeexecution", out var section) ||
                 snippets.TryGetSectionByHeader("Shellcode Execution", out section)) &&
                section!.TryGetItem(execId, out var item))
            {
                var analysis = ExecutionSnippetAnalyzer.Analyze(execId, item!.Snippet);
                execMem      = analysis.FinalProtection.ToString();
                execApis     = analysis.DetectedApis.Count > 0 ? string.Join(", ", analysis.DetectedApis) : "(none detected)";
                execSgnSafe  = analysis.IsSgnCompatible ? "✓ RWX-compatible" : "✗ requires RWX";
                execSummary  = analysis.Summary;

                if (main?.IsShikataGaNaiEnabled == true && !main.IsShikataGaNaiPostPlacement && !analysis.IsSgnCompatible)
                {
                    snap.Warnings.Add(
                        $"SGN (pre) requires writable+executable memory, but '{execId}' yields {analysis.FinalProtection}. " +
                        "Either move SGN to post-placement, or pick an exec snippet that keeps the page RWX (e.g. RWXAlloc / DirectExec).");
                }
            }
            snap.ExecutionSpec.Add(new("snippet",  execId));
            snap.ExecutionSpec.Add(new("mem-prot", execMem));
            snap.ExecutionSpec.Add(new("apis",     execApis));
            snap.ExecutionSpec.Add(new("sgn-safe", execSgnSafe));
            if (execSummary != null) snap.ExecutionSpec.Add(new("note", execSummary));

            // ─── Sidebar: Carrier ───────────────────────────────────────
            snap.CarrierSpec.Add(new("template", main?.TemplateCombo?.SelectedItem?.ToString() ?? "(unset)"));
            snap.CarrierSpec.Add(new("templ-id", main?.SelectedTemplateId ?? "(unknown)"));
            int snippetSlots = options?.ComboValues?.Count(x => !string.IsNullOrEmpty(x.Value)) ?? 0;
            int listSlots    = options?.ListValues?.Count(x => x.Value != null && x.Value.Count > 0) ?? 0;
            snap.CarrierSpec.Add(new("snippets", $"{snippetSlots} bound"));
            if (listSlots > 0) snap.CarrierSpec.Add(new("list-opts", $"{listSlots} slot(s)"));

            // ─── Sidebar: Post-processing ───────────────────────────────
            snap.PostProcessingSpec.Add(new("backdoor", backdoor?.IsBackdooringEnabled == true ? "✓ enabled" : "✗ off"));
            if (backdoor?.IsBackdooringEnabled == true)
            {
                snap.PostProcessingSpec.Add(new("method",  InjectionLabel(backdoor.SelectedInjectionMethod)));
                snap.PostProcessingSpec.Add(new("carrier", CarrierLabel(backdoor.SelectedCarrierInvoke)));
                snap.PostProcessingSpec.Add(new("encrypt", backdoor.SelectedEncryption.ToString()));
                if (backdoor.TargetPeFilePath is { } tp)
                    snap.PostProcessingSpec.Add(new("target-pe", Path.GetFileName(tp)));
            }
            snap.PostProcessingSpec.Add(new("pack",     packing?.IsPackingEnabled == true ? $"UPX {packing.SelectedCompressionLevel}" : "✗ off"));
            snap.PostProcessingSpec.Add(new("finalize", finalize?.IsFinalizationEnabled == true ? "✓ enabled" : "✗ off"));

            // ─── Sidebar: Compile Target ────────────────────────────────
            snap.CompileSpec.Add(new("compiler", compile?.SelectedCompilerDisplay ?? "(not detected)"));
            snap.CompileSpec.Add(new("output",   string.IsNullOrWhiteSpace(compile?.OutputDirectory) ? "(prompt on build)" : compile!.OutputDirectory!));
            snap.CompileSpec.Add(new("debug",    compile?.GenerateDebugInfoEnabled == true ? "✓ PDB on" : "stripped"));
            snap.CompileSpec.Add(new("verbose",  compile?.VerboseBuildEnabled == true ? "✓" : "—"));
            snap.CompileSpec.Add(new("strip",    compile?.IsStripToBinChecked == true ? "✓ →.bin" : "—"));
            snap.OutputArchHint = "x64-PE";

            // ─── Sidebar: Artifacts ─────────────────────────────────────
            snap.ExpectedArtifacts.Add("loader.exe");
            if (compile?.IsStripToBinChecked == true || (main?.IsShikataGaNaiEnabled == true && main.IsShikataGaNaiPostPlacement))
                snap.ExpectedArtifacts.Add("loader.bin");
            if (main?.IsShikataGaNaiEnabled == true && main.IsShikataGaNaiPostPlacement)
            {
                snap.ExpectedArtifacts.Add("loader.sgn.bin");
                if (backdoor?.IsBackdooringEnabled != true)
                    snap.ExpectedArtifacts.Add("loader.sgn.exe (sgncarrier wrap)");
            }
            if (backdoor?.IsBackdooringEnabled == true && backdoor.TargetPeFilePath is { } targetPe)
                snap.ExpectedArtifacts.Add($"{Path.GetFileNameWithoutExtension(targetPe)}_backdoored{Path.GetExtension(targetPe)}");

            foreach (var a in snap.ExpectedArtifacts)
                snap.ArtifactSpec.Add(new(" •", a));

            // ─── Stage timeline ─────────────────────────────────────────
            snap.Stages.Add(new Stage(
                Title: "Shellcode source",
                Glyph: "\uE943",
                Subtitle: $"Origin: {srcKind} — {srcLabel}",
                Params: new List<(string, string)>
                {
                    ("location", srcPath),
                    ("size",     srcSize),
                },
                CommandLine: null,
                Enabled: true,
                Severity: Severity.Normal,
                StatusChip: "INPUT"));

            if (main?.IsShikataGaNaiEnabled == true && !main.IsShikataGaNaiPostPlacement)
            {
                snap.Stages.Add(new Stage(
                    Title: "SGN — pre-bin2shell",
                    Glyph: "\uE8C8",
                    Subtitle: "Polymorphic encoding baked into the loader's embedded shellcode.",
                    Params: new List<(string, string)>
                    {
                        ("arch",       "x86_64"),
                        ("iterations", $"{sgnCount}"),
                        ("max-decode", $"{sgnMax} B"),
                        ("rwx-needed", "yes (decoder is self-modifying)"),
                    },
                    CommandLine: $"sgn -a 64 -c {sgnCount} -M {sgnMax} -i <input.bin> -o <sgn.bin>",
                    Enabled: true,
                    Severity: snap.Warnings.Count > 0 ? Severity.Warning : Severity.Normal,
                    StatusChip: "POLY"));
            }

            snap.Stages.Add(new Stage(
                Title: "Bin2Shell encode/envelope",
                Glyph: "\uE809",
                Subtitle: "Lift bytes into inline C source with runtime decoder + envelope.",
                Params: new List<(string, string)>
                {
                    ("encoder",  encName),
                    ("envelope", envName),
                    ("emits",    "C source (code_blob + length)"),
                },
                CommandLine: BuildBin2ShellCli(main),
                Enabled: true,
                Severity: Severity.Normal,
                StatusChip: "ENCODE"));

            snap.Stages.Add(new Stage(
                Title: "Template compile",
                Glyph: "\uE74E",
                Subtitle: $"Carrier: {main?.TemplateCombo?.SelectedItem ?? "(unset)"}  •  Snippet slots: {snippetSlots}",
                Params: BuildTemplateParams(main, options, execId),
                CommandLine: $"cc <template>.c -o loader.exe   // execution: {execId}",
                Enabled: true,
                Severity: Severity.Normal,
                StatusChip: "BUILD"));

            bool needPostSgn = main?.IsShikataGaNaiEnabled == true && main.IsShikataGaNaiPostPlacement;
            bool stripChecked = compile?.IsStripToBinChecked == true;

            if (stripChecked || needPostSgn)
            {
                snap.Stages.Add(new Stage(
                    Title: "Strip loader → flat .bin",
                    Glyph: "\uE77F",
                    Subtitle: needPostSgn && !stripChecked
                        ? "Auto-enabled: post-placement SGN needs a position-independent input."
                        : "Position-independent shellcode extracted from the compiled loader.",
                    Params: new List<(string, string)>
                    {
                        ("mode",   "ep (entry-point relative)"),
                        ("output", "loader.bin"),
                    },
                    CommandLine: "washmachine-cli strip <loader.exe> -o <loader.bin> --mode ep",
                    Enabled: true,
                    Severity: Severity.Normal,
                    StatusChip: "STRIP"));
            }

            if (needPostSgn)
            {
                snap.Stages.Add(new Stage(
                    Title: "SGN — post-bin2shell",
                    Glyph: "\uE8C8",
                    Subtitle: "Wrap the stripped loader as a separate polymorphic artifact.",
                    Params: new List<(string, string)>
                    {
                        ("arch",       "x86_64"),
                        ("iterations", $"{sgnCount}"),
                        ("max-decode", $"{sgnMax} B"),
                        ("output",     "loader.sgn.bin"),
                    },
                    CommandLine: $"sgn -a 64 -c {sgnCount} -M {sgnMax} -i loader.bin -o loader.sgn.bin",
                    Enabled: true,
                    Severity: Severity.Normal,
                    StatusChip: "POLY"));

                if (backdoor?.IsBackdooringEnabled != true)
                {
                    snap.Stages.Add(new Stage(
                        Title: "SGN carrier wrap",
                        Glyph: "\uE7B8",
                        Subtitle: "Backdoor disabled — wrap the SGN .bin in the 'sgncarrier' template so it ships as a runnable .exe.",
                        Params: new List<(string, string)>
                        {
                            ("template", "sgncarrier"),
                            ("exec",     execId),
                            ("artifact", "loader.sgn.exe"),
                        },
                        CommandLine: "bin2shell loader.sgn.bin → sgncarrier.c → cc → loader.sgn.exe",
                        Enabled: true,
                        Severity: Severity.Normal,
                        StatusChip: "WRAP"));
                }
            }

            // Backdoor stage
            if (backdoor?.IsBackdooringEnabled == true)
            {
                var bp = new List<(string, string)>
                {
                    ("target",  backdoor.TargetPeFilePath is { } t ? Path.GetFileName(t) : "(not selected)"),
                    ("method",  InjectionLabel(backdoor.SelectedInjectionMethod)),
                    ("carrier", CarrierLabel(backdoor.SelectedCarrierInvoke)),
                    ("encrypt", backdoor.SelectedEncryption.ToString()),
                };
                if (backdoor.CustomSectionName is { } sn) bp.Add(("section",  sn));
                if (!backdoor.PreserveOriginalEntry)       bp.Add(("entry",    "no-preserve"));
                if (!backdoor.PatchIat)                    bp.Add(("iat",      "no-patch"));
                if (!backdoor.PatchExit)                   bp.Add(("exit",     "no-patch"));
                if (!backdoor.RemoveSignature)             bp.Add(("sig",      "keep"));
                if (backdoor.DryRun)                       bp.Add(("dry-run",  "✓"));

                snap.Stages.Add(new Stage(
                    Title: "Backdoor target PE",
                    Glyph: "\uE8AC",
                    Subtitle: $"Inject shellcode into {(backdoor.TargetPeFilePath is { } tn ? Path.GetFileName(tn) : "target")}.",
                    Params: bp,
                    CommandLine: "washmachine-cli backdoor <target.exe> -p <shellcode.bin> ...",
                    Enabled: true,
                    Severity: Severity.Normal,
                    StatusChip: "INJECT"));
            }
            else
            {
                snap.Stages.Add(new Stage(
                    Title: "Backdoor target PE",
                    Glyph: "\uE8AC",
                    Subtitle: "No PE will be modified (backdooring disabled).",
                    Params: Array.Empty<(string, string)>(),
                    CommandLine: null,
                    Enabled: false,
                    Severity: Severity.Skipped,
                    StatusChip: "OFF"));
            }

            // Pack stage
            if (packing?.IsPackingEnabled == true)
            {
                var args = packing.GetUpxArguments();
                snap.Stages.Add(new Stage(
                    Title: "Pack with UPX",
                    Glyph: "\uE7B8",
                    Subtitle: "Executable compression — reduces size; trivially un-packable.",
                    Params: new List<(string, string)>
                    {
                        ("level", packing.SelectedCompressionLevel),
                        ("args",  string.IsNullOrWhiteSpace(args) ? "(default)" : args),
                    },
                    CommandLine: string.IsNullOrWhiteSpace(args) ? "upx <input.exe>" : $"upx {args} <input.exe>",
                    Enabled: true,
                    Severity: Severity.Normal,
                    StatusChip: "PACK"));
            }
            else
            {
                snap.Stages.Add(new Stage(
                    Title: "Pack with UPX",
                    Glyph: "\uE7B8",
                    Subtitle: "Output not compressed.",
                    Params: Array.Empty<(string, string)>(),
                    CommandLine: null,
                    Enabled: false,
                    Severity: Severity.Skipped,
                    StatusChip: "OFF"));
            }

            // Finalize stage
            if (finalize?.IsFinalizationEnabled == true && (finalize.IsCloneEnabled || finalize.NopPaddingBytes > 0))
            {
                var fp = new List<(string, string)>();
                if (finalize.IsCloneEnabled && finalize.CloneSourceExePath is { } src2)
                {
                    fp.Add(("clone-src", Path.GetFileName(src2)));
                    var flags = new List<string>();
                    if (finalize.CloneResources) flags.Add("rsrc");
                    if (finalize.CloneIcon)      flags.Add("icon");
                    if (finalize.CloneMetadata)  flags.Add("meta");
                    if (flags.Count > 0) fp.Add(("clone-set", string.Join(", ", flags)));
                }
                if (finalize.NopPaddingBytes > 0) fp.Add(("nop-pad", $"{finalize.NopPaddingBytes:N0} B"));

                snap.Stages.Add(new Stage(
                    Title: "Finalize output",
                    Glyph: "\uE8C1",
                    Subtitle: "Clone version-info / icon / resources, optional NOP inflation.",
                    Params: fp,
                    CommandLine: "washmachine-cli finalize <out.exe> [--clone ...] [--pad-bytes N]",
                    Enabled: true,
                    Severity: Severity.Normal,
                    StatusChip: "POLISH"));
            }
            else
            {
                snap.Stages.Add(new Stage(
                    Title: "Finalize output",
                    Glyph: "\uE8C1",
                    Subtitle: "No cosmetic post-processing.",
                    Params: Array.Empty<(string, string)>(),
                    CommandLine: null,
                    Enabled: false,
                    Severity: Severity.Skipped,
                    StatusChip: "OFF"));
            }

            // Output stage
            string outDir = string.IsNullOrWhiteSpace(compile?.OutputDirectory) ? "(prompt on build)" : compile!.OutputDirectory!;
            snap.Stages.Add(new Stage(
                Title: "Output projection",
                Glyph: "\uE8B7",
                Subtitle: $"Drop directory: {outDir}",
                Params: snap.ExpectedArtifacts.Select(a => ("artifact", a)).ToList(),
                CommandLine: null,
                Enabled: true,
                Severity: Severity.Normal,
                StatusChip: "OUT"));

            // Recipe one-liner
            var oneLiner = new List<string>();
            oneLiner.Add($"src={srcKind.ToLowerInvariant()}");
            if (sgnPlacement != "off") oneLiner.Add($"sgn={sgnPlacement}");
            oneLiner.Add($"enc={encName}");
            oneLiner.Add($"env={envName}");
            oneLiner.Add($"tpl={main?.SelectedTemplateId ?? "?"}");
            oneLiner.Add($"exec={execId}");
            if (backdoor?.IsBackdooringEnabled == true) oneLiner.Add("backdoor");
            if (packing?.IsPackingEnabled == true)      oneLiner.Add("upx");
            if (finalize?.IsFinalizationEnabled == true) oneLiner.Add("finalize");
            snap.RecipeOneLiner = string.Join("  ·  ", oneLiner);

            return snap;
        }

        private static string? BuildBin2ShellCli(MainPage? main)
        {
            if (main == null) return null;
            var args = new List<string>();
            if (main.SelectedEncoderIndex is int e)  args.Add($"-e {e}");
            if (main.SelectedEnvelopeIndex is int v) args.Add($"-v {v}");
            return args.Count > 0
                ? $"bin2shell {string.Join(' ', args)} <input.bin>"
                : "bin2shell <input.bin>";
        }

        private static List<(string, string)> BuildTemplateParams(MainPage? main, TemplateOptionsState? options, string execId)
        {
            var list = new List<(string, string)>();
            list.Add(("template-id",  main?.SelectedTemplateId ?? "(unset)"));
            list.Add(("exec-snippet", execId));
            if (options != null)
            {
                foreach (var (k, v) in options.ComboValues)
                {
                    if (string.IsNullOrEmpty(v)) continue;
                    if (k != null && k.Replace(" ", "").ToLowerInvariant().Contains("execution")) continue;
                    list.Add((k ?? "?", v));
                    if (list.Count > 10) { list.Add(("…", "(truncated)")); break; }
                }
            }
            return list;
        }

        private static string InjectionLabel(InjectionMethod m) => m switch
        {
            InjectionMethod.CodeCave         => "code-cave",
            InjectionMethod.NewSection       => "new-section",
            InjectionMethod.SectionExtension => "section-ext",
            _                                => m.ToString(),
        };

        private static string CarrierLabel(CarrierInvoke c) => c switch
        {
            CarrierInvoke.EntryPointHijack      => "entry-point",
            CarrierInvoke.EntryFunctionBackdoor => "function-backdoor",
            CarrierInvoke.TlsCallback           => "tls-callback",
            CarrierInvoke.DllMain               => "dll-main",
            CarrierInvoke.DllExport             => "dll-export",
            _ => c.ToString()
        };
    }
}
