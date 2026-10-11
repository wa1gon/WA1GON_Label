using System.Text.Json;

namespace OpenLabel.App.Models;

public sealed class LabelDocument
{
    public string Name { get; set; } = "Address Label";
    public double WidthMm { get; set; } = 88.9; // 3.5 inches
    public double HeightMm { get; set; } = 25.4; // 1 inch
    public List<LabelText> TextItems { get; set; } = [new()];

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static LabelDocument FromJson(string json) => JsonSerializer.Deserialize<LabelDocument>(json)
        ?? throw new InvalidDataException("Invalid label document.");
}

public sealed class LabelText
{
    public string Text { get; set; } = "Hello from OpenLabel";
    public double Xmm { get; set; } = 4;
    public double Ymm { get; set; } = 4;
    public double FontSizePt { get; set; } = 14;
}
