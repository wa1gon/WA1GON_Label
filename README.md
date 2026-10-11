# WA1GONLabel

Cross-printer Linux label printing, built with .NET 10 and Avalonia UI.

## Milestone 1

- Linux CUPS printer discovery (`lpstat`)
- Initial 3.5 × 1 inch label document model, expressed in millimeters
- Basic text preview
- Print text label through CUPS (`lp`) using a generated PDF
- Pluggable printer backend interface

**Status:** Source scaffold only. It has not been compiled or tested on a real DYMO LabelWriter 450. The first printing test must validate media size, orientation, margins, and CUPS filter support.

## Prerequisites (Linux Mint / Ubuntu)

Install .NET 10 SDK from Microsoft's supported installation instructions, and ensure CUPS is installed and your printer is configured.

```bash
sudo apt install cups cups-client
lpstat -p -d
```

If the DYMO queue isn't listed, configure the printer in your system's Printers settings first. The correct DYMO driver/filter may be required.

## Run

```bash
cd src/WA1GONLabel.App
dotnet restore
dotnet run
```

Avalonia package versions are pinned in the project file. If NuGet cannot resolve a pinned version, update both Avalonia packages to a mutually compatible stable release.

## Important limitations

- Initial PDF renderer supports basic ASCII text and Helvetica only.
- The preview is illustrative, not a pixel-perfect rendering.
- CUPS must be able to convert PDF for the chosen printer queue.
- The `Custom.88.9x25.4mm` media option may not be supported by every driver; inspect `lpoptions -p PRINTER -l` and adapt to the queue's actual media names.
- Label text, page dimensions and positions are represented in the printer-independent document model. A future milestone will add a true WYSIWYG canvas, template save/load UI, barcodes, and a more complete PDF/raster engine.
- For DYMO, **first test on a single inexpensive label**, and verify feed and orientation before batch printing.

## Architecture

- `Models/LabelDocument.cs`: printer-independent model and JSON serialization.
- `Printing/IPrinterBackend.cs`: abstraction for printer enumeration and printing.
- `Printing/CupsPrinterBackend.cs`: Linux implementation using `lpstat`, `lp`, and a small PDF generator.
- `MainWindow.axaml`: initial Avalonia editor/preview UI.

## Suggested next milestone

1. Validate the DYMO LabelWriter 450 CUPS queue and media options.
2. Add a true dimensioned label canvas and draggable text objects.
3. Add save/load templates and reliable font rendering.
4. Add per-printer capability profiles and raster output when required.
