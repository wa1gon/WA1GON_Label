using System.Reflection;
using OpenLabel.App.Printing;

var backend = new CupsPrinterBackend();
var printers = await backend.GetPrintersAsync();
Console.WriteLine($"Printers: {printers.Count}");
foreach (var p in printers) Console.WriteLine($"  {p.Name}{(p.IsDefault ? " (default)" : "")}");

var dymo = printers.FirstOrDefault(p => p.Name.Contains("DYMO", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("LabelWriter", StringComparison.OrdinalIgnoreCase));
if (dymo is null)
{
    Console.WriteLine("DYMO/LabelWriter not found");
    return 1;
}

var method = typeof(CupsPrinterBackend).GetMethod("GetDefaultPageSizeAsync", BindingFlags.NonPublic | BindingFlags.Static);
var media = method is null ? null : await (Task<string?>)method.Invoke(null, [dymo.Name, default(CancellationToken)])!;
Console.WriteLine($"Resolved media for {dymo.Name}: {media ?? "(none)"}");
return media == "1.25x3.5" ? 0 : 1;
