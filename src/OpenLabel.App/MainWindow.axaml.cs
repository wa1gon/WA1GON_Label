using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenLabel.App.Models;
using OpenLabel.App.Printing;

namespace OpenLabel.App;

public partial class MainWindow : Window
{
    private readonly IPrinterBackend _printers = new CupsPrinterBackend();
    private readonly LabelDocument _label = new();

    public MainWindow()
    {
        InitializeComponent();
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
        if (LabelTextInput is null || PreviewText is null) return;
        PreviewText.Text = LabelTextInput.Text ?? "";
        _label.TextItems[0].Text = LabelTextInput.Text ?? "";
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
