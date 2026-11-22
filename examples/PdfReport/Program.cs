// A branded kundli PDF, rendered on this machine (Chrome, Chromium or Microsoft Edge must be installed):
//
//   export LIFEASTRO_API_KEY=dv_live_...        # your key; never commit it
//   dotnet run --project examples/PdfReport
//   dotnet run --project examples/PdfReport -- saved-kundli-lite.json    # a saved report: no API call, no key
//
// Writes kundli.html and kundli.pdf to the working directory.
using System.Text.Json;
using LifeAstroApi;
using LifeAstroApi.Params;
using LifeAstroApi.Pdf;

try
{
    JsonElement report;
    if (args.Length > 0)
    {
        using JsonDocument saved = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
        report = saved.RootElement.Clone();
    }
    else
    {
        using var client = new LifeAstroClient();
        var arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
        report = await client.Reports.KundliLiteAsync(
            new ReportsKundliLiteParams(arjun) { Name = "Arjun Mehta", Place = "Mumbai", Lang = Lang.Hi });
    }

    var branding = new PdfBranding { CompanyName = "Acme Astro", PrimaryColor = "#6b21a8" };
    await File.WriteAllTextAsync("kundli.html", PdfReports.RenderKundliLiteHtml(report, branding));

    await using var engine = new ChromePdfEngine(); // keep one engine for many PDFs
    byte[] pdf = await PdfReports.KundliLitePdfAsync(report, branding, engine: engine);
    await File.WriteAllBytesAsync("kundli.pdf", pdf);
    Console.WriteLine($"kundli.pdf: {PdfOutline.PageCount(pdf)} pages");
}
catch (LifeAstroException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message}");
    Environment.ExitCode = 1;
}
