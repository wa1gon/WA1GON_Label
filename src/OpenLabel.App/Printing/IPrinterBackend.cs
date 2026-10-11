using OpenLabel.App.Models;

namespace OpenLabel.App.Printing;

public sealed record PrinterInfo(string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}

public interface IPrinterBackend
{
    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default);
    Task PrintAsync(string printerName, LabelDocument label, int copies = 1, CancellationToken cancellationToken = default);
}
