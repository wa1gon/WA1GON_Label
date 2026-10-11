using Avalonia.Controls;
using Avalonia.Interactivity;
using WA1GONLabel.App.Models;
using WA1GONLabel.App.Printing;

namespace WA1GONLabel.App;

public partial class MainWindow : Window
{
    private readonly IPrinterBackend _printers = new CupsPrinterBackend();
    private readonly LabelDocument _label = new();

    public MainWindow()
    {
        InitializeComponent();
        if (_label.TextItems.Count > 0)
        {
            var item = _label.TextItems[0];
            if (PreviewText is not null)
                PreviewText.Text = item.Text;
            ApplyPreviewFormatting(item);
            if (FontSizeInput is not null)
                FontSizeInput.Value = (decimal)item.FontSizePt;
            if (BoldCheck is not null)
                BoldCheck.IsChecked = item.IsBold;
            if (ItalicCheck is not null)
                ItalicCheck.IsChecked = item.IsItalic;
        }
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

    private void TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (PreviewText is null || _label.TextItems.Count == 0) return;
        _label.TextItems[0].Text = PreviewText.Text ?? "";
    }

    private void FormatChanged(object? sender, RoutedEventArgs e)
    {
        if (_label.TextItems.Count == 0) return;
        ApplyFormatToDocument();
    }

    private void FormatChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_label.TextItems.Count == 0) return;
        ApplyFormatToDocument();
    }

    private void ApplyFormatToDocument()
    {
        var item = _label.TextItems[0];
        item.FontSizePt = (double)(FontSizeInput?.Value ?? 14);
        item.IsBold = BoldCheck?.IsChecked ?? false;
        item.IsItalic = ItalicCheck?.IsChecked ?? false;
        ApplyPreviewFormatting(item);
    }

    private void ApplyPreviewFormatting(LabelText item)
    {
        if (PreviewText is null) return;
        PreviewText.FontSize = item.FontSizePt;
        PreviewText.FontWeight = item.IsBold ? global::Avalonia.Media.FontWeight.Bold : global::Avalonia.Media.FontWeight.Normal;
        PreviewText.FontStyle = item.IsItalic ? global::Avalonia.Media.FontStyle.Italic : global::Avalonia.Media.FontStyle.Normal;
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
