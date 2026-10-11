using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenLabel.App.Models;

namespace OpenLabel.App.Printing;

public sealed class CupsPrinterBackend : IPrinterBackend
{
    public async Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        var printers = await RunAsync("lpstat", ["-p"], cancellationToken);
        var defaultPrinter = await RunAsync("lpstat", ["-d"], cancellationToken);
        var defaultName = defaultPrinter.Stdout.StartsWith("system default destination: ", StringComparison.Ordinal)
            ? defaultPrinter.Stdout["system default destination: ".Length..].Trim() : null;
        if (printers.ExitCode != 0)
            throw new InvalidOperationException($"CUPS printer discovery failed: {printers.Stderr}");
        return printers.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("printer ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => new PrinterInfo(parts[1], parts[1] == defaultName))
            .ToArray();
    }

    public async Task PrintAsync(string printerName, LabelDocument label, int copies = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(printerName)) throw new ArgumentException("Select a printer.", nameof(printerName));
        if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies));
        var media = await GetDefaultPageSizeAsync(printerName, cancellationToken)
            ?? (IsLabelWriter(printerName) ? "1.25x3.5" : "Custom.88.9x25.4mm");
        var path = Path.Combine(Path.GetTempPath(), $"openlabel-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, MinimalPdfRenderer.Render(label), cancellationToken);
            // CUPS determines whether the installed printer queue can accept/convert PDF.
            var result = await RunAsync("lp", ["-d", printerName, "-n", copies.ToString(CultureInfo.InvariantCulture),
                "-o", $"media={media}", path], cancellationToken);
            if (result.ExitCode != 0) throw new InvalidOperationException($"CUPS print failed: {result.Stderr}");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static bool IsLabelWriter(string printerName) =>
        printerName.Contains("DYMO", StringComparison.OrdinalIgnoreCase) ||
        printerName.Contains("LabelWriter", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> GetDefaultPageSizeAsync(string printerName, CancellationToken ct)
    {
        var options = await RunAsync("lpoptions", ["-p", printerName, "-l"], ct);
        if (options.ExitCode != 0) return null;
        var pageSizeLine = options.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("PageSize", StringComparison.Ordinal));
        if (pageSizeLine is null) return null;
        var star = pageSizeLine.IndexOf('*');
        if (star < 0) return null;
        var start = star + 1;
        var end = pageSizeLine.IndexOf(' ', start);
        if (end < 0) end = pageSizeLine.IndexOf('\t', start);
        if (end < 0) end = pageSizeLine.Length;
        var value = pageSizeLine[start..end];
        return value == "Custom.WIDTHxHEIGHT" ? null : value;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(file) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException($"Could not launch {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdout, await stderr);
    }
}

// Dependency-free proof-of-concept PDF. Supports basic ASCII Helvetica text only.
// Later milestones will replace this with a proper rendering pipeline.
internal static class MinimalPdfRenderer
{
    public static byte[] Render(LabelDocument label)
    {
        static string Num(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var w = label.WidthMm * 72 / 25.4;
        var h = label.HeightMm * 72 / 25.4;
        var stream = new StringBuilder();
        foreach (var item in label.TextItems)
        {
            var x = item.Xmm * 72 / 25.4;
            var y = h - item.Ymm * 72 / 25.4 - item.FontSizePt;
            var safe = new string(item.Text.Where(c => c >= 32 && c <= 126).ToArray());
            stream.AppendLine($"BT /F1 {Num(item.FontSizePt)} Tf 1 0 0 1 {Num(x)} {Num(y)} Tm ({Escape(safe)}) Tj ET");
        }
        var content = Encoding.ASCII.GetBytes(stream.ToString());
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(w)} {Num(h)}] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{stream}endstream"
        };
        using var buffer = new MemoryStream();
        void Write(string s) { var b = Encoding.ASCII.GetBytes(s); buffer.Write(b); }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(buffer.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = buffer.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return buffer.ToArray();
    }
}
