using System.Diagnostics;
using System.Globalization;
using System.Text;
using SkiaSharp;
using WA1GONLabel.App.Models;

namespace WA1GONLabel.App.Printing;

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

// Proof-of-concept PDF. Supports basic ASCII and Helvetica variants with simple word-wrapping.
// Later milestones will replace this with a proper rendering pipeline.
internal static class MinimalPdfRenderer
{
    private record FontDef(string Name, string BaseFont);
    private record PlacedWord(string Text, LabelText Item, double X, double Width);
    private record PlacedLine(List<PlacedWord> Words, double MaxFontSize, double Top);
    private record TextToken(string Text, LabelText Item, bool IsNewLine);

    public static byte[] Render(LabelDocument label)
    {
        static string Num(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        static string FontName(bool bold, bool italic) => (bold, italic) switch
        {
            (true, true) => "F4",
            (true, false) => "F2",
            (false, true) => "F3",
            (false, false) => "F1"
        };
        static double Measure(TextToken t)
        {
            var family = SKTypeface.FromFamilyName("Liberation Sans")
                      ?? SKTypeface.FromFamilyName("DejaVu Sans")
                      ?? SKTypeface.FromFamilyName("Helvetica")
                      ?? SKTypeface.FromFamilyName(null);
            var weight = t.Item.IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = t.Item.IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var typeface = SKTypeface.FromFamilyName(family.FamilyName, weight, SKFontStyleWidth.Normal, slant) ?? family;
            using var paint = new SKPaint { Typeface = typeface, TextSize = (float)t.Item.FontSizePt, IsAntialias = true };
            return paint.MeasureText(t.Text);
        }

        var w = label.WidthMm * 72 / 25.4;
        var h = label.HeightMm * 72 / 25.4;

        var fonts = new[]
        {
            new FontDef("F1", "Helvetica"),
            new FontDef("F2", "Helvetica-Bold"),
            new FontDef("F3", "Helvetica-Oblique"),
            new FontDef("F4", "Helvetica-BoldOblique")
        };

        var resourceEntries = string.Join(" ", fonts.Select((f, i) => $"/{f.Name} {4 + i} 0 R"));
        var contentObjectNumber = 4 + fonts.Length;

        var xBase = label.TextItems.FirstOrDefault()?.Xmm * 72 / 25.4 ?? 0;
        var rightMargin = xBase; // mirror left margin
        var maxX = w - rightMargin;
        const double lineSpacing = 1.2;

        // The first line always starts at the very top of the label.
        // If centering is on, the whole laid-out block is shifted down to be centered.
        var yBase = 0.0;

        var tokens = new List<TextToken>();
        foreach (var item in label.TextItems)
        {
            var text = item.Text ?? "";
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    tokens.Add(new TextToken("\n", item, true));
                    i += 2;
                }
                else if (text[i] == '\n')
                {
                    tokens.Add(new TextToken("\n", item, true));
                    i++;
                }
                else if (char.IsWhiteSpace(text[i]))
                {
                    var start = i;
                    while (i < text.Length && char.IsWhiteSpace(text[i]) && text[i] != '\r' && text[i] != '\n')
                        i++;
                    tokens.Add(new TextToken(text[start..i], item, false));
                }
                else
                {
                    var start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]))
                        i++;
                    tokens.Add(new TextToken(text[start..i], item, false));
                }
            }
        }

        var lines = new List<PlacedLine>();
        var line = new List<PlacedWord>();
        var lineTop = yBase;
        var currentX = xBase;
        var currentLineMaxSize = 0.0;

        void FlushLine()
        {
            if (line.Count == 0) return;
            lines.Add(new PlacedLine(line, currentLineMaxSize, lineTop));
            line = new List<PlacedWord>();
            lineTop += currentLineMaxSize * lineSpacing;
            currentX = xBase;
            currentLineMaxSize = 0;
        }

        foreach (var token in tokens)
        {
            if (token.IsNewLine)
            {
                FlushLine();
                continue;
            }

            // skip whitespace at the start of a line
            if (currentX <= xBase && token.Text.Trim().Length == 0)
                continue;

            var wordWidth = Measure(token);
            if (currentX + wordWidth > maxX && currentX > xBase)
                FlushLine();

            line.Add(new PlacedWord(token.Text, token.Item, currentX, wordWidth));
            currentX += wordWidth;
            if (token.Item.FontSizePt > currentLineMaxSize)
                currentLineMaxSize = token.Item.FontSizePt;
        }
        FlushLine();

        var blockHeight = lineTop - yBase;
        var yOffset = label.CenterVertical ? (h - blockHeight) / 2 - yBase : 0;

        var stream = new StringBuilder();
        foreach (var placed in lines)
        {
            var baseline = placed.Top + placed.MaxFontSize + yOffset - (label.CenterVertical ? 0.0 : 0.1 * 72);
            var y = h - baseline;
            var xOffset = 0.0;
            if (label.CenterHorizontal && placed.Words.Count > 0)
            {
                var last = placed.Words[^1];
                var lineWidth = last.X + last.Width - xBase;
                xOffset = Math.Max(0, (maxX - xBase - lineWidth) / 2);
            }
            foreach (var word in placed.Words)
            {
                var safe = new string(word.Text.Where(c => c >= 32 && c <= 126).ToArray());
                stream.AppendLine($"BT /{FontName(word.Item.IsBold, word.Item.IsItalic)} {Num(word.Item.FontSizePt)} Tf 1 0 0 1 {Num(word.X + xOffset)} {Num(y)} Tm ({Escape(safe)}) Tj ET");
            }
        }

        var content = Encoding.ASCII.GetBytes(stream.ToString());

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(w)} {Num(h)}] /Resources << /Font << {resourceEntries} >> >> /Contents {contentObjectNumber} 0 R >>",
        };
        foreach (var font in fonts)
            objects.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{font.BaseFont} >>");
        objects.Add($"<< /Length {content.Length} >>\nstream\n{stream}endstream");

        using var buffer = new MemoryStream();
        void Write(string s) { var b = Encoding.ASCII.GetBytes(s); buffer.Write(b); }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(buffer.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = buffer.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return buffer.ToArray();
    }
}
