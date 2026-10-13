using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WA1GONLabel.App.Models;
using WA1GONLabel.App.Printing;

namespace WA1GONLabel.App;

public partial class MainWindow : Window
{
    private readonly IPrinterBackend _printers = new CupsPrinterBackend();
    private LabelDocument _label = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyDocumentToUI();
        Opened += async (_, _) => await RefreshPrintersAsync();
    }

    private async Task RefreshPrintersAsync()
    {
        try
        {
            StatusText.Text = "Searching CUPS printers...";
            var printers = await _printers.GetPrintersAsync();
            PrinterSelector.ItemsSource = printers;
            var preferred = printers.FirstOrDefault(p =>
                p.Name.Contains("DYMO", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("LabelWriter", StringComparison.OrdinalIgnoreCase));
            PrinterSelector.SelectedItem = preferred ?? printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
            StatusText.Text = printers.Count == 0 ? "No CUPS printers found. Check lpstat -p." : $"Found {printers.Count} printer(s).";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshPrintersAsync();

    private void TextChanged(object? sender, TextChangedEventArgs e) => UpdateDocumentFromPreview();

    private void FontSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        var size = (double)(FontSizeInput?.Value ?? 14);
        if (PreviewText is not null)
            PreviewText.FontSize = size;
        UpdateDocumentFromPreview();
    }

    private void UpdateDocumentFromPreview()
    {
        if (PreviewText is null) return;
        var size = (double)(FontSizeInput?.Value ?? 14);
        var x = _label.TextItems.FirstOrDefault()?.Xmm ?? 8;
        var y = _label.TextItems.FirstOrDefault()?.Ymm ?? 8;
        _label.CenterVertical = CenterVerticalCheck?.IsChecked ?? false;
        _label.CenterHorizontal = CenterHorizontalCheck?.IsChecked ?? false;
        _label.TextItems = ParseMarkup(PreviewText.Text ?? "", size, x, y);
        UpdateFormattedPreview();
    }

    private void VerticalAlignmentChanged(object? sender, RoutedEventArgs e)
    {
        _label.CenterVertical = CenterVerticalCheck?.IsChecked ?? false;
        ApplyVerticalAlignment();
    }

    private void HorizontalAlignmentChanged(object? sender, RoutedEventArgs e)
    {
        _label.CenterHorizontal = CenterHorizontalCheck?.IsChecked ?? false;
        ApplyHorizontalAlignment();
    }

    private void ApplyDocumentToUI()
    {
        var first = _label.TextItems.FirstOrDefault();
        if (PreviewText is not null)
            PreviewText.Text = first?.Text ?? "Hello from WA1GONLabel";
        if (FontSizeInput is not null)
            FontSizeInput.Value = (decimal)(first?.FontSizePt ?? 14);
        if (PreviewText is not null)
            PreviewText.FontSize = first?.FontSizePt ?? 14;
        if (CenterVerticalCheck is not null)
            CenterVerticalCheck.IsChecked = _label.CenterVertical;
        if (CenterHorizontalCheck is not null)
            CenterHorizontalCheck.IsChecked = _label.CenterHorizontal;
        ApplyVerticalAlignment();
        ApplyHorizontalAlignment();
        UpdateDocumentFromPreview();
    }

    private void ApplyVerticalAlignment()
    {
        var center = _label.CenterVertical;
        if (FormattedPreview is not null)
        {
            FormattedPreview.VerticalAlignment = center ? global::Avalonia.Layout.VerticalAlignment.Center : global::Avalonia.Layout.VerticalAlignment.Top;
            // Align the first line to the top of the preview without clipping.
            FormattedPreview.Margin = center ? new global::Avalonia.Thickness(0) : new global::Avalonia.Thickness(0, -11 - 0.1 * 96, 0, 0);
        }
    }

    private void ApplyHorizontalAlignment()
    {
        var center = _label.CenterHorizontal;
        if (FormattedPreview is not null)
            FormattedPreview.TextAlignment = center ? global::Avalonia.Media.TextAlignment.Center : global::Avalonia.Media.TextAlignment.Left;
    }

    private void UpdateFormattedPreview()
    {
        if (FormattedPreview is null) return;
        FormattedPreview!.Inlines.Clear();
        foreach (var run in _label.TextItems)
        {
            var inline = new Run { Text = run.Text };
            if (run.IsBold)
                inline.FontWeight = global::Avalonia.Media.FontWeight.Bold;
            if (run.IsItalic)
                inline.FontStyle = global::Avalonia.Media.FontStyle.Italic;
            inline.FontSize = run.FontSizePt;
            FormattedPreview!.Inlines.Add(inline);
        }
    }

    private static List<LabelText> ParseMarkup(string text, double fontSize, double x, double y)
    {
        var runs = new List<LabelText>();
        var current = new StringBuilder();
        var bold = false;
        var italic = false;
        var i = 0;

        void Flush()
        {
            if (current.Length <= 0) return;
            runs.Add(new LabelText
            {
                Text = current.ToString(),
                Xmm = x,
                Ymm = y,
                FontSizePt = fontSize,
                IsBold = bold,
                IsItalic = italic
            });
            current.Clear();
        }

        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                var end = text.IndexOf('>', i);
                if (end < 0)
                {
                    current.Append(text[i]);
                    i++;
                    continue;
                }
                var tag = text[(i + 1)..end].Trim();
                Flush();
                if (tag.StartsWith('/'))
                {
                    var name = tag[1..].ToLowerInvariant();
                    if (name == "b") bold = false;
                    else if (name == "i") italic = false;
                }
                else
                {
                    var name = tag.ToLowerInvariant();
                    if (name == "b") bold = true;
                    else if (name == "i") italic = true;
                }
                i = end + 1;
            }
            else
            {
                current.Append(text[i]);
                i++;
            }
        }
        Flush();
        return runs;
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Label",
                SuggestedFileName = "label.json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("Label document") { Patterns = new[] { "*.json" } } }
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(_label.ToJson());
            StatusText.Text = "Label saved.";
        }
        catch (Exception ex) { StatusText.Text = $"Save failed: {ex.Message}"; }
    }

    private async void LoadClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Label",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Label document") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            _label = LabelDocument.FromJson(json);
            ApplyDocumentToUI();
            StatusText.Text = "Label loaded.";
        }
        catch (Exception ex) { StatusText.Text = $"Load failed: {ex.Message}"; }
    }

    private void ExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void PreviewText_KeyDown(object? sender, KeyEventArgs e)
    {
        if (PreviewText is null) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.B)
        {
            SurroundSelection("<b>", "</b>");
            e.Handled = true;
        }
        else if (e.Key == Key.I)
        {
            SurroundSelection("<i>", "</i>");
            e.Handled = true;
        }
    }

    private void SurroundSelection(string openTag, string closeTag)
    {
        if (PreviewText is null) return;
        var text = PreviewText.Text ?? "";
        var start = Math.Min(PreviewText.SelectionStart, PreviewText.SelectionEnd);
        var end = Math.Max(PreviewText.SelectionStart, PreviewText.SelectionEnd);
        if (start == end)
        {
            PreviewText.Text = text[..start] + openTag + closeTag + text[end..];
            PreviewText.CaretIndex = start + openTag.Length;
        }
        else
        {
            var before = text[..start];
            var selected = text[start..end];
            var after = text[end..];
            PreviewText.Text = before + openTag + selected + closeTag + after;
            PreviewText.SelectionStart = start + openTag.Length;
            PreviewText.SelectionEnd = start + openTag.Length + selected.Length;
        }
    }

    private async void PrintClicked(object? sender, RoutedEventArgs e)
    {
        if (PrinterSelector.SelectedItem is not PrinterInfo printer)
        {
            StatusText.Text = "Select a printer first.";
            return;
        }
        PrintButton.IsEnabled = false;
        try
        {
            await _printers.PrintAsync(printer.Name, _label, (int)(CopiesInput.Value ?? 1));
            StatusText.Text = $"Submitted label to CUPS queue '{printer.Name}'. Check printer output.";
        }
        catch (Exception ex) { StatusText.Text = $"Printing failed: {ex.Message}"; }
        finally { PrintButton.IsEnabled = true; }
    }
}
