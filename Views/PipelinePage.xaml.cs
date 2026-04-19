using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Washmachine.Models;
using Washmachine.Services;
using Windows.UI;
using WinRT.Interop;
using Path = System.IO.Path;

namespace Washmachine.Views;

public sealed partial class PipelinePage : Page
{
    private readonly AppPaths _paths = new();
    private readonly ICodeSnippetCatalogService _snippets;
    private readonly string _buildId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
    private DispatcherTimer? _clockTimer;

    public PipelinePage()
    {
        InitializeComponent();
        _snippets = new YamlCodeSnippetCatalogService(_paths);

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        SizeChanged += (_, _) => DrawScanLines();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Render();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        DrawScanLines();
        StartClock();
        Render();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
    }

    private void StartClock()
    {
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => HeaderTimestamp.Text = $"UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        _clockTimer.Start();
        HeaderTimestamp.Text = $"UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
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
            FlashTelemetry("◦ recipe exported");
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
            FlashTelemetry("◦ recipe imported — re-syncing");
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

    private void FlashTelemetry(string text)
    {
        TelemetryLine.Text = text;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scan-line decorative overlay
    // ═══════════════════════════════════════════════════════════════════════
    private void DrawScanLines()
    {
        if (GridOverlay == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        GridOverlay.Children.Clear();

        var lineBrush = (Brush)Resources["HudGridLineBrush"];
        const double spacing = 3.0;

        for (double y = 0; y < ActualHeight; y += spacing)
        {
            var line = new Line
            {
                X1 = 0, X2 = ActualWidth,
                Y1 = y, Y2 = y,
                Stroke = lineBrush,
                StrokeThickness = 0.5,
                Opacity = 0.18,
            };
            GridOverlay.Children.Add(line);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Render orchestrator
    // ═══════════════════════════════════════════════════════════════════════
    private void Render()
    {
        if (StepsList == null) return;

        StepsList.Items.Clear();
        SpecPanelHost.Children.Clear();
        AlertBanner.Visibility = Visibility.Collapsed;

        var snap = Snapshot.Capture(_snippets);

        // Header chips & status indicators
        HeaderBuildId.Text   = $"BUILD-ID {_buildId}";
        HeaderTarget.Text    = $"TARGET {snap.OutputArchHint}";
        ChipStepsValue.Text  = snap.Stages.Count.ToString();
        ChipArtifactsValue.Text = snap.ExpectedArtifacts.Count.ToString();
        TitleSubline.Text    = $"// {snap.RecipeOneLiner}";

        if (snap.Warnings.Count > 0)
        {
            ChipWarningBorder.Visibility = Visibility.Visible;
            ChipWarningValue.Text        = snap.Warnings.Count.ToString();
            StatusIndicatorDot.Fill      = (Brush)Resources["HudAmberBrush"];
            StatusIndicatorText.Text     = "◤ PIPELINE — REVIEW REQUIRED";
            StatusIndicatorText.Foreground = (Brush)Resources["HudAmberBrush"];
            AlertBanner.Visibility       = Visibility.Visible;
            AlertBannerText.Text         = string.Join("\n", snap.Warnings);
        }
        else
        {
            ChipWarningBorder.Visibility = Visibility.Collapsed;
            StatusIndicatorDot.Fill      = (Brush)Resources["HudMintBrush"];
            StatusIndicatorText.Text     = "◤ PIPELINE NOMINAL";
            StatusIndicatorText.Foreground = (Brush)Resources["HudMintBrush"];
        }

        RailEtaText.Text = $"// {snap.Stages.Count(s => s.Enabled)} active / {snap.Stages.Count} declared";

        // Stage cards
        for (int i = 0; i < snap.Stages.Count; i++)
        {
            var stage = snap.Stages[i];
            StepsList.Items.Add(BuildStageCard(i + 1, stage, isLast: i == snap.Stages.Count - 1));
        }

        // Spec sidebar
        BuildSpecSidebar(snap);

        // Telemetry line
        TelemetryLine.Text = snap.TelemetryLine;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stage card builder
    // ═══════════════════════════════════════════════════════════════════════
    private FrameworkElement BuildStageCard(int index, Stage stage, bool isLast)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical };

        var accent = stage.Severity switch
        {
            Severity.Warning => (Brush)Resources["HudAmberBrush"],
            Severity.Skipped => (Brush)Resources["HudInkFaintBrush"],
            _                => (Brush)Resources["HudCyanBrush"],
        };
        var accentSoft = stage.Severity switch
        {
            Severity.Warning => new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xB3, 0x47)),
            Severity.Skipped => new SolidColorBrush(Color.FromArgb(0x33, 0x42, 0x58, 0x7A)),
            _                => (Brush)Resources["HudCyanSoftBrush"],
        };

        var card = new Border
        {
            Background = (Brush)Resources["HudPanelBrush"],
            BorderBrush = accentSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(0),
            Opacity = stage.Enabled ? 1.0 : 0.55,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });   // accent rail
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // index
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Vertical accent bar
        var rail = new Border
        {
            Background = accent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0.85,
        };
        Grid.SetColumn(rail, 0);
        grid.Children.Add(rail);

        // Index pill
        var indexBlock = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 14, 4, 12),
            Spacing = 2,
        };
        indexBlock.Children.Add(new TextBlock
        {
            Text = $"{index:00}",
            FontFamily = MonoFont,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = accent,
        });
        indexBlock.Children.Add(new TextBlock
        {
            Text = "STAGE",
            FontFamily = MonoFont,
            FontSize = 9,
            CharacterSpacing = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Resources["HudInkFaintBrush"],
        });
        Grid.SetColumn(indexBlock, 1);
        grid.Children.Add(indexBlock);

        // Body
        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(8, 12, 14, 14) };

        // Title row
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new FontIcon
        {
            Glyph = stage.Glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = accent,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = stage.Title.ToUpperInvariant(),
            FontFamily = MonoFont,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 100,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["HudInkBrush"],
        });
        if (!string.IsNullOrEmpty(stage.StatusChip))
        {
            var chip = new Border
            {
                Background = stage.Severity == Severity.Warning
                    ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xB3, 0x47))
                    : new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xE0, 0xFF)),
                BorderBrush = accentSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = stage.StatusChip,
                    FontFamily = MonoFont,
                    FontSize = 9,
                    CharacterSpacing = 200,
                    Foreground = accent,
                },
            };
            titleRow.Children.Add(chip);
        }
        if (!stage.Enabled)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = "// SKIPPED",
                FontFamily = MonoFont,
                FontSize = 10,
                Foreground = (Brush)Resources["HudInkFaintBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        body.Children.Add(titleRow);

        // Subtitle
        if (!string.IsNullOrWhiteSpace(stage.Subtitle))
        {
            body.Children.Add(new TextBlock
            {
                Text = stage.Subtitle,
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = (Brush)Resources["HudInkDimBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Param table (key/value)
        if (stage.Params.Count > 0)
        {
            var paramGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < stage.Params.Count; r++)
            {
                paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (k, v) = stage.Params[r];

                var keyText = new TextBlock
                {
                    Text = $"› {k}",
                    FontFamily = MonoFont,
                    FontSize = 10,
                    CharacterSpacing = 100,
                    Foreground = (Brush)Resources["HudInkFaintBrush"],
                    Margin = new Thickness(0, 1, 8, 1),
                };
                Grid.SetRow(keyText, r);
                Grid.SetColumn(keyText, 0);
                paramGrid.Children.Add(keyText);

                var valText = new TextBlock
                {
                    Text = v,
                    FontFamily = MonoFont,
                    FontSize = 11,
                    Foreground = (Brush)Resources["HudInkBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                };
                Grid.SetRow(valText, r);
                Grid.SetColumn(valText, 1);
                paramGrid.Children.Add(valText);
            }
            body.Children.Add(paramGrid);
        }

        // Code line
        if (!string.IsNullOrWhiteSpace(stage.CommandLine))
        {
            var codeBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x05, 0x0B, 0x18)),
                BorderBrush = accentSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = $"$ {stage.CommandLine}",
                    FontFamily = MonoFont,
                    FontSize = 11,
                    Foreground = accent,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            body.Children.Add(codeBox);
        }

        Grid.SetColumn(body, 2);
        grid.Children.Add(body);

        card.Child = grid;
        container.Children.Add(card);

        // Connector
        if (!isLast)
        {
            var conn = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 0, 0, 0) };
            for (int i = 0; i < 3; i++)
            {
                conn.Children.Add(new Ellipse
                {
                    Width = 3, Height = 3,
                    Fill = (Brush)Resources["HudCyanSoftBrush"],
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            container.Children.Add(conn);
        }

        return container;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Spec sidebar
    // ═══════════════════════════════════════════════════════════════════════
    private void BuildSpecSidebar(Snapshot snap)
    {
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ TARGET SIGNATURE", snap.SignatureSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ ENCODING STACK",   snap.EncodingSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ EXECUTION VECTOR", snap.ExecutionSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ CARRIER PROFILE",  snap.CarrierSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ POST-PROCESSING",  snap.PostProcessingSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ COMPILE TARGET",   snap.CompileSpec));
        SpecPanelHost.Children.Add(BuildSpecPanel("◢ ARTIFACT PROJECTION", snap.ArtifactSpec));
    }

    private Border BuildSpecPanel(string heading, IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var panel = new Border
        {
            Background = (Brush)Resources["HudPanelBrush"],
            BorderBrush = (Brush)Resources["HudCyanSoftBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(0),
        };

        var stack = new StackPanel();
        var head = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x15, 0x25)),
            Padding = new Thickness(10, 5, 10, 5),
            Child = new TextBlock
            {
                Text = heading,
                FontFamily = MonoFont,
                FontSize = 10,
                CharacterSpacing = 200,
                Foreground = (Brush)Resources["HudCyanBrush"],
            }
        };
        stack.Children.Add(head);

        var body = new StackPanel { Margin = new Thickness(10, 8, 10, 10), Spacing = 4 };

        if (rows.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "// no data",
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = (Brush)Resources["HudInkFaintBrush"],
            });
        }
        else
        {
            foreach (var (k, v) in rows)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                row.Children.Add(new TextBlock
                {
                    Text = k,
                    FontFamily = MonoFont,
                    FontSize = 10,
                    CharacterSpacing = 100,
                    Foreground = (Brush)Resources["HudInkFaintBrush"],
                    VerticalAlignment = VerticalAlignment.Top,
                });

                var valText = new TextBlock
                {
                    Text = v,
                    FontFamily = MonoFont,
                    FontSize = 11,
                    Foreground = (Brush)Resources["HudInkBrush"],
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(valText, 1);
                row.Children.Add(valText);
                body.Children.Add(row);
            }
        }

        stack.Children.Add(body);
        panel.Child = stack;
        return panel;
    }

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    // ═══════════════════════════════════════════════════════════════════════
    // Snapshot — extracts a structured view of the entire user configuration
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
        public List<KeyValuePair<string, string>> SignatureSpec  { get; } = new();
        public List<KeyValuePair<string, string>> EncodingSpec   { get; } = new();
        public List<KeyValuePair<string, string>> ExecutionSpec  { get; } = new();
        public List<KeyValuePair<string, string>> CarrierSpec    { get; } = new();
        public List<KeyValuePair<string, string>> PostProcessingSpec { get; } = new();
        public List<KeyValuePair<string, string>> CompileSpec    { get; } = new();
        public List<KeyValuePair<string, string>> ArtifactSpec   { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> ExpectedArtifacts { get; } = new();
        public string OutputArchHint { get; set; } = "x64-PE";
        public string RecipeOneLiner { get; set; } = "";
        public string TelemetryLine  { get; set; } = "";

        public static Snapshot Capture(ICodeSnippetCatalogService snippets)
        {
            var snap = new Snapshot();
            var main     = MainPage.Instance;
            var backdoor = BackdooringPage.Instance;
            var packing  = PackingPage.Instance;
            var finalize = FinalizePage.Instance;
            var compile  = CompilePage.Instance;

            // ─── Sidebar: TARGET SIGNATURE ───────────────────────────────
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
            snap.SignatureSpec.Add(new("KIND",     srcKind));
            snap.SignatureSpec.Add(new("LABEL",    srcLabel));
            snap.SignatureSpec.Add(new("LOCATION", srcPath));
            snap.SignatureSpec.Add(new("SIZE",     srcSize));

            // ─── Sidebar: ENCODING STACK ─────────────────────────────────
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
            snap.EncodingSpec.Add(new("SGN MODE",   sgnPlacement));
            snap.EncodingSpec.Add(new("SGN ITER",   sgnPlacement == "off" ? "—" : $"x{sgnCount}, max={sgnMax}B"));
            snap.EncodingSpec.Add(new("ENCODER",    encName));
            snap.EncodingSpec.Add(new("ENVELOPE",   envName));
            if (main?.SelectedEncoderIndex is int ei)  snap.EncodingSpec.Add(new("ENC INDEX", ei.ToString()));
            if (main?.SelectedEnvelopeIndex is int vi) snap.EncodingSpec.Add(new("ENV INDEX", vi.ToString()));

            // ─── Sidebar: EXECUTION VECTOR ───────────────────────────────
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
                execMem  = analysis.FinalProtection.ToString();
                execApis = analysis.DetectedApis.Count > 0 ? string.Join(", ", analysis.DetectedApis) : "(none detected)";
                execSgnSafe = analysis.IsSgnCompatible ? "✓ RWX-compatible" : "✗ requires RWX";
                execSummary = analysis.Summary;

                if (main?.IsShikataGaNaiEnabled == true && !main.IsShikataGaNaiPostPlacement && !analysis.IsSgnCompatible)
                {
                    snap.Warnings.Add(
                        $"SGN (pre) requires writable+executable memory, but '{execId}' yields {analysis.FinalProtection}. " +
                        "Either move SGN to post-placement, or pick an exec snippet that keeps the page RWX (e.g. RWXAlloc / DirectExec).");
                }
            }
            snap.ExecutionSpec.Add(new("SNIPPET ID", execId));
            snap.ExecutionSpec.Add(new("MEM PROT",   execMem));
            snap.ExecutionSpec.Add(new("APIs",       execApis));
            snap.ExecutionSpec.Add(new("SGN SAFE",   execSgnSafe));
            if (execSummary != null) snap.ExecutionSpec.Add(new("NOTE", execSummary));

            // ─── Sidebar: CARRIER PROFILE ────────────────────────────────
            snap.CarrierSpec.Add(new("TEMPLATE",   main?.TemplateCombo?.SelectedItem?.ToString() ?? "(unset)"));
            snap.CarrierSpec.Add(new("TEMPL ID",   main?.SelectedTemplateId ?? "(unknown)"));
            int snippetSlots = options?.ComboValues?.Count(x => !string.IsNullOrEmpty(x.Value)) ?? 0;
            int listSlots    = options?.ListValues?.Count(x => x.Value != null && x.Value.Count > 0) ?? 0;
            snap.CarrierSpec.Add(new("SNIPPETS",   $"{snippetSlots} bound"));
            if (listSlots > 0) snap.CarrierSpec.Add(new("LIST OPTS", $"{listSlots} slot(s)"));

            // ─── Sidebar: POST-PROCESSING ────────────────────────────────
            snap.PostProcessingSpec.Add(new("BACKDOOR", backdoor?.IsBackdooringEnabled == true ? "✓ enabled" : "✗ off"));
            if (backdoor?.IsBackdooringEnabled == true)
            {
                snap.PostProcessingSpec.Add(new("METHOD",   InjectionLabel(backdoor.SelectedInjectionMethod)));
                snap.PostProcessingSpec.Add(new("CARRIER",  CarrierLabel(backdoor.SelectedCarrierInvoke)));
                snap.PostProcessingSpec.Add(new("ENCRYPT",  backdoor.SelectedEncryption.ToString()));
                if (backdoor.TargetPeFilePath is { } tp) snap.PostProcessingSpec.Add(new("TARGET PE", Path.GetFileName(tp)));
            }
            snap.PostProcessingSpec.Add(new("PACK",     packing?.IsPackingEnabled == true ? $"UPX {packing.SelectedCompressionLevel}" : "✗ off"));
            snap.PostProcessingSpec.Add(new("FINALIZE", finalize?.IsFinalizationEnabled == true ? "✓ enabled" : "✗ off"));

            // ─── Sidebar: COMPILE TARGET ─────────────────────────────────
            snap.CompileSpec.Add(new("COMPILER", compile?.SelectedCompilerDisplay ?? "(not detected)"));
            snap.CompileSpec.Add(new("OUTPUT",   string.IsNullOrWhiteSpace(compile?.OutputDirectory) ? "(prompt on build)" : compile!.OutputDirectory!));
            snap.CompileSpec.Add(new("DEBUG",    compile?.GenerateDebugInfoEnabled == true ? "✓ PDB on" : "stripped"));
            snap.CompileSpec.Add(new("VERBOSE",  compile?.VerboseBuildEnabled == true ? "✓" : "—"));
            snap.CompileSpec.Add(new("STRIP",    compile?.IsStripToBinChecked == true ? "✓ →.bin" : "—"));
            snap.OutputArchHint = "x64-PE";

            // ─── Sidebar: ARTIFACT PROJECTION ────────────────────────────
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

            // ─── Stage timeline (mirrors CompilerService order) ──────────
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
                Subtitle: $"Carrier: {main?.TemplateCombo?.SelectedItem ?? "(unset)"}  •  Snippet slots bound: {snippetSlots}",
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
                        ("mode",       "ep (entry-point relative)"),
                        ("output",     "loader.bin"),
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
                        Subtitle: "Backdoor disabled → wrap the SGN .bin in the 'sgncarrier' template so it ships as a runnable .exe.",
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
                    ("target",    backdoor.TargetPeFilePath is { } t ? Path.GetFileName(t) : "(not selected)"),
                    ("method",    InjectionLabel(backdoor.SelectedInjectionMethod)),
                    ("carrier",   CarrierLabel(backdoor.SelectedCarrierInvoke)),
                    ("encrypt",   backdoor.SelectedEncryption.ToString()),
                };
                if (backdoor.CustomSectionName is { } sn) bp.Add(("section", sn));
                if (!backdoor.PreserveOriginalEntry)      bp.Add(("entry",   "no-preserve"));
                if (!backdoor.PatchIat)                    bp.Add(("iat",     "no-patch"));
                if (!backdoor.PatchExit)                   bp.Add(("exit",    "no-patch"));
                if (!backdoor.RemoveSignature)             bp.Add(("sig",     "keep"));
                if (backdoor.DryRun)                        bp.Add(("dry-run", "✓"));

                snap.Stages.Add(new Stage(
                    Title: "Backdoor target PE",
                    Glyph: "\uE8AC",
                    Subtitle: $"Inject ORIGINAL shellcode into {(backdoor.TargetPeFilePath is { } tn ? Path.GetFileName(tn) : "target")}.",
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
                    if (flags.Count > 0) fp.Add(("clone-set", string.Join(",", flags)));
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

            // Recipe one-liner / telemetry
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
            snap.RecipeOneLiner = string.Join(" • ", oneLiner);

            snap.TelemetryLine =
                $"◦ stages={snap.Stages.Count} ◦ active={snap.Stages.Count(s => s.Enabled)} ◦ artifacts={snap.ExpectedArtifacts.Count} ◦ alerts={snap.Warnings.Count} ◦ exec={execId} ◦ mem={execMem}";

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
            list.Add(("template-id", main?.SelectedTemplateId ?? "(unset)"));
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
            CarrierInvoke.EntryPointHijack       => "entry-point",
            CarrierInvoke.EntryFunctionBackdoor  => "function-backdoor",
            CarrierInvoke.TlsCallback            => "tls-callback",
            _                                    => c.ToString(),
        };
    }
}
