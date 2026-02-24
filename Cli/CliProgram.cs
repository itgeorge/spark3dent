using System.Diagnostics;
using System.Globalization;
using Accounting;
using AppSetup;
using Invoices;
using Utilities;

namespace Cli;

class CliProgram
{
    private const int LogBufferSize = 20;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var config = await AppBootstrap.LoadAndResolveConfigAsync();

        var logDir = config.Desktop.LogDirectory;
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "spark3dent.log");
        using var fileLogger = new FileLogger(logPath);
        using var logger = new BufferedLogger(fileLogger, LogBufferSize);
        logger.LogInfo("Spark3Dent started");

        var setup = await AppBootstrap.SetupDependenciesAsync(config, logger);
        if (setup == null)
        {
            logger.LogError("Failed to initialize dependencies. Check config: SellerAddress and SellerBankTransferInfo are required.", new InvalidOperationException("Missing config"));
            Console.WriteLine("Error: SellerAddress and SellerBankTransferInfo must be configured in appsettings.json.");
            return;
        }

        await RunCommandLoopAsync(args, setup.InvoiceManagement, setup.ClientRepo, setup.PdfExporter, setup.ImageExporter, logger);
    }

    static async Task RunCommandLoopAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IClientRepo clientRepo,
        IInvoiceExporter pdfExporter,
        IInvoiceExporter? imageExporter,
        ILogger logger)
    {
        if (args.Length > 0)
        {
            await ExecuteCommandAsync(args, invoiceManagement, clientRepo, pdfExporter, imageExporter, logger);
            return;
        }

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0].Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInfo("Exiting");
                break;
            }

            await ExecuteCommandAsync(parts, invoiceManagement, clientRepo, pdfExporter, imageExporter, logger);
        }
    }

    static async Task ExecuteCommandAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IClientRepo clientRepo,
        IInvoiceExporter pdfExporter,
        IInvoiceExporter? imageExporter,
        ILogger logger)
    {
        try
        {
            var cmd = args[0].ToLowerInvariant();

            if (cmd == "help")
            {
                PrintHelp();
                return;
            }

            if (cmd == "exit" && args.Length == 1)
            {
                return;
            }

            if (cmd == "clients")
            {
                await RunClientsCommandAsync(args.Skip(1).ToArray(), clientRepo);
                return;
            }

            if (cmd == "invoices")
            {
                await RunInvoicesCommandAsync(args.Skip(1).ToArray(), invoiceManagement, pdfExporter, imageExporter);
                return;
            }

            Console.WriteLine($"Unknown command: {cmd}. Type 'help' for available commands.");
        }
        catch (Exception ex)
        {
            logger.LogError("Command execution failed", ex);
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  help                    - Show this help");
        Console.WriteLine("  exit                    - Exit (interactive mode only)");
        Console.WriteLine("  clients add             - Add a new client (prompts for fields)");
        Console.WriteLine("  clients edit            - Edit an existing client");
        Console.WriteLine("  clients list            - List clients (alphabetically by nickname)");
        Console.WriteLine("  invoices issue <nickname> <amount> [date]");
        Console.WriteLine("                          - Issue invoice. Amount: 123.45 or 123,45 (€). Date: dd-MM-yyyy (default: today)");
        Console.WriteLine("  invoices preview <nickname> <amount> [date]");
        Console.WriteLine("                          - Preview future invoice in browser (no invoice created)");
        Console.WriteLine("  invoices correct <number> <amount> [date]");
        Console.WriteLine("                          - Correct an existing invoice");
        Console.WriteLine("  invoices issue/correct  - Add --exportPng to also export PNG image (in addition to PDF)");
        Console.WriteLine("  invoices list           - List recent invoices (newest first)");
    }

    static async Task RunClientsCommandAsync(string[] args, IClientRepo clientRepo)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: clients add | clients edit | clients list");
            return;
        }

        var sub = args[0].ToLowerInvariant();

        if (sub == "add")
        {
            await ClientsAddAsync(clientRepo);
            return;
        }

        if (sub == "edit")
        {
            await ClientsEditAsync(args.Skip(1).ToArray(), clientRepo);
            return;
        }

        if (sub == "list")
        {
            await ClientsListAsync(clientRepo);
            return;
        }

        Console.WriteLine($"Unknown subcommand: {sub}. Use: clients add | clients edit | clients list");
    }

    static async Task ClientsAddAsync(IClientRepo clientRepo)
    {
        var nickname = ReadRequired("Nickname");
        if (nickname == null) return;

        var name = ReadRequired("Company name");
        if (name == null) return;

        var representativeName = ReadRequired("Representative name");
        if (representativeName == null) return;

        var companyIdentifier = ReadRequired("Company identifier (EIK/Bulstat)");
        if (companyIdentifier == null) return;

        var vatIdentifier = ReadOptional("VAT identifier (optional)");
        var address = ReadRequired("Address");
        if (address == null) return;

        var city = ReadRequired("City");
        if (city == null) return;

        var postalCode = ReadRequired("Postal code");
        if (postalCode == null) return;

        var country = ReadOptional("Country (optional, default: България)") ?? "България";

        var billingAddress = new BillingAddress(name, representativeName, companyIdentifier, vatIdentifier, address, city, postalCode, country);
        var client = new Client(nickname, billingAddress);

        await clientRepo.AddAsync(client);
        Console.WriteLine($"Client '{nickname}' added successfully.");
    }

    static async Task ClientsEditAsync(string[] args, IClientRepo clientRepo)
    {
        string? nickname = args.Length > 0 ? args[0] : ReadRequired("Nickname of client to edit");
        if (string.IsNullOrEmpty(nickname)) return;

        Client client;
        try
        {
            client = await clientRepo.GetAsync(nickname!);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return;
        }

        var addr = client.Address;
        Console.WriteLine($"Current values for '{nickname}':");
        Console.WriteLine($"  Company: {addr.Name}");
        Console.WriteLine($"  Representative: {addr.RepresentativeName}");
        Console.WriteLine($"  EIK: {addr.CompanyIdentifier}");
        Console.WriteLine($"  VAT: {addr.VatIdentifier ?? "(none)"}");
        Console.WriteLine($"  Address: {addr.Address}, {addr.City} {addr.PostalCode}, {addr.Country}");
        Console.WriteLine("Enter new values (empty = keep current):");

        var newNickname = ReadOptional($"Nickname [{nickname}]") ?? nickname;
        var newName = ReadOptional($"Company name [{addr.Name}]") ?? addr.Name;
        var newRep = ReadOptional($"Representative name [{addr.RepresentativeName}]") ?? addr.RepresentativeName;
        var newEik = ReadOptional($"Company identifier [{addr.CompanyIdentifier}]") ?? addr.CompanyIdentifier;
        var newVat = ReadOptional($"VAT identifier [{addr.VatIdentifier ?? ""}]");
        if (string.IsNullOrWhiteSpace(newVat)) newVat = addr.VatIdentifier;
        var newAddr = ReadOptional($"Address [{addr.Address}]") ?? addr.Address;
        var newCity = ReadOptional($"City [{addr.City}]") ?? addr.City;
        var newPostal = ReadOptional($"Postal code [{addr.PostalCode}]") ?? addr.PostalCode;
        var newCountry = ReadOptional($"Country [{addr.Country}]") ?? addr.Country;

        var newBilling = new BillingAddress(newName, newRep, newEik, newVat, newAddr, newCity, newPostal, newCountry);
        var update = new IClientRepo.ClientUpdate(newNickname, newBilling);

        await clientRepo.UpdateAsync(nickname!, update);
        Console.WriteLine($"Client updated successfully.");
    }

    static async Task ClientsListAsync(IClientRepo clientRepo)
    {
        var result = await clientRepo.ListAsync(limit: 20);
        if (result.Items.Count == 0)
        {
            Console.WriteLine("No clients.");
            return;
        }

        Console.WriteLine($"{"Nickname",-20} {"Company",-30} {"Representative",-25}");
        Console.WriteLine(new string('-', 75));
        foreach (var c in result.Items)
            Console.WriteLine($"{c.Nickname,-20} {Truncate(c.Address.Name, 28),-30} {Truncate(c.Address.RepresentativeName, 23),-25}");
    }

    static async Task RunInvoicesCommandAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IInvoiceExporter pdfExporter,
        IInvoiceExporter? imageExporter)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: invoices issue | invoices correct | invoices preview | invoices list");
            return;
        }

        var sub = args[0].ToLowerInvariant();

        if (sub == "issue")
        {
            await InvoicesIssueAsync(args.Skip(1).ToArray(), invoiceManagement, pdfExporter, imageExporter);
            return;
        }

        if (sub == "correct")
        {
            await InvoicesCorrectAsync(args.Skip(1).ToArray(), invoiceManagement, pdfExporter, imageExporter);
            return;
        }

        if (sub == "preview")
        {
            await InvoicesPreviewAsync(args.Skip(1).ToArray(), invoiceManagement, imageExporter);
            return;
        }

        if (sub == "list")
        {
            await InvoicesListAsync(invoiceManagement);
            return;
        }

        Console.WriteLine($"Unknown subcommand: {sub}. Use: invoices issue | invoices correct | invoices preview | invoices list");
    }

    static async Task InvoicesIssueAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IInvoiceExporter pdfExporter,
        IInvoiceExporter? imageExporter)
    {
        var (opts, positional) = CliOptsParser.ParseWithPositional(args.ToList());
        var exportPng = CliOptsParser.HasFlag(opts, "exportPng");

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: invoices issue <client nickname> <amount> [date] [--exportPng]");
            Console.WriteLine("  amount: e.g. 123.45 or 123,45 (euros)");
            Console.WriteLine("  date: dd-MM-yyyy (default: today)");
            return;
        }

        var nickname = positional[0];
        var amountStr = positional[1].Replace(',', '.');
        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amountDec) || amountDec < 0)
        {
            Console.WriteLine("Invalid amount. Use e.g. 123.45 or 123,45");
            return;
        }
        var amountCents = (int)Math.Round(amountDec * 100);

        DateTime? date = null;
        if (positional.Count >= 3)
        {
            if (!DateTime.TryParseExact(positional[2], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                Console.WriteLine("Invalid date. Use dd-MM-yyyy");
                return;
            }
            date = parsedDate;
        }

        try
        {
            var result = await invoiceManagement.IssueInvoiceAsync(nickname, amountCents, date, pdfExporter);
            Console.WriteLine($"Invoice {result.Invoice.Number} created.");
            if (result.ExportResult.Success && result.ExportResult.DataOrUri != null)
                Console.WriteLine($"PDF saved to: {result.ExportResult.DataOrUri}");
            else
                Console.WriteLine("Warning: PDF export failed.");

            if (exportPng && imageExporter != null)
            {
                var pngResult = await invoiceManagement.ReExportInvoiceAsync(result.Invoice.Number, imageExporter);
                if (pngResult.ExportResult.Success && pngResult.ExportResult.DataOrUri != null)
                    Console.WriteLine($"PNG saved to: {pngResult.ExportResult.DataOrUri}");
                else
                    Console.WriteLine("Warning: PNG export failed.");
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task InvoicesPreviewAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IInvoiceExporter? imageExporter)
    {
        if (imageExporter == null)
        {
            Console.WriteLine("Error: Image exporter not available. Preview requires Chromium.");
            return;
        }

        var (_, positional) = CliOptsParser.ParseWithPositional(args.ToList());

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: invoices preview <client nickname> <amount> [date]");
            Console.WriteLine("  amount: e.g. 123.45 or 123,45 (euros)");
            Console.WriteLine("  date: dd-MM-yyyy (default: today)");
            return;
        }

        var nickname = positional[0];
        var amountStr = positional[1].Replace(',', '.');
        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amountDec) || amountDec < 0)
        {
            Console.WriteLine("Invalid amount. Use e.g. 123.45 or 123,45");
            return;
        }
        var amountCents = (int)Math.Round(amountDec * 100);

        DateTime? date = null;
        if (positional.Count >= 3)
        {
            if (!DateTime.TryParseExact(positional[2], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                Console.WriteLine("Invalid date. Use dd-MM-yyyy");
                return;
            }
            date = parsedDate;
        }

        try
        {
            var result = await invoiceManagement.PreviewInvoiceAsync(nickname, amountCents, date, imageExporter);
            if (!result.Success || result.DataOrUri == null)
            {
                Console.WriteLine("Preview export failed.");
                return;
            }

            var htmlPath = Path.Combine(Path.GetTempPath(), $"spark3dent-preview-{Guid.NewGuid():N}.html");
            var html = $"""
                <!DOCTYPE html>
                <html><head><meta charset="utf-8"><title>Invoice Preview</title></head>
                <body style="margin:0"><img src="{result.DataOrUri}" alt="Invoice preview" style="max-width:100%"/></body>
                </html>
                """;
            await File.WriteAllTextAsync(htmlPath, html);
            Console.WriteLine(htmlPath);

            try
            {
                Process.Start(new ProcessStartInfo { FileName = htmlPath, UseShellExecute = true });
                Console.WriteLine("Preview opened in browser.");
            }
            catch (Exception)
            {
                Console.WriteLine("Could not open browser automatically. Open the file above manually.");
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task InvoicesCorrectAsync(
        string[] args,
        InvoiceManagement invoiceManagement,
        IInvoiceExporter pdfExporter,
        IInvoiceExporter? imageExporter)
    {
        var (opts, positional) = CliOptsParser.ParseWithPositional(args.ToList());
        var exportPng = CliOptsParser.HasFlag(opts, "exportPng");

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: invoices correct <invoice number> <amount> [date] [--exportPng]");
            return;
        }

        var invoiceNumber = positional[0];
        var amountStr = positional[1].Replace(',', '.');
        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amountDec) || amountDec < 0)
        {
            Console.WriteLine("Invalid amount. Use e.g. 123.45 or 123,45");
            return;
        }
        var amountCents = (int)Math.Round(amountDec * 100);

        DateTime? date = null;
        if (positional.Count >= 3)
        {
            if (!DateTime.TryParseExact(positional[2], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                Console.WriteLine("Invalid date. Use dd-MM-yyyy");
                return;
            }
            date = parsedDate;
        }

        try
        {
            var result = await invoiceManagement.CorrectInvoiceAsync(invoiceNumber, amountCents, date, pdfExporter);
            Console.WriteLine($"Invoice {result.Invoice.Number} corrected successfully.");
            Console.WriteLine($"Total: {result.Invoice.TotalAmount.Cents / 100}.{result.Invoice.TotalAmount.Cents % 100:D2} €");
            if (!result.ExportResult.Success)
                Console.WriteLine("Warning: PDF export failed.");

            if (exportPng && imageExporter != null)
            {
                var pngResult = await invoiceManagement.ReExportInvoiceAsync(result.Invoice.Number, imageExporter);
                if (pngResult.ExportResult.Success && pngResult.ExportResult.DataOrUri != null)
                    Console.WriteLine($"PNG saved to: {pngResult.ExportResult.DataOrUri}");
                else
                    Console.WriteLine("Warning: PNG export failed.");
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task InvoicesListAsync(InvoiceManagement invoiceManagement)
    {
        var result = await invoiceManagement.ListInvoicesAsync(limit: 20);
        if (result.Items.Count == 0)
        {
            Console.WriteLine("No invoices.");
            return;
        }

        Console.WriteLine($"{"Number",-10} {"Date",-12} {"Buyer",-30} {"Total (€)",-12}");
        Console.WriteLine(new string('-', 64));
        foreach (var inv in result.Items)
        {
            var totalStr = $"{inv.TotalAmount.Cents / 100}.{inv.TotalAmount.Cents % 100:D2}";
            var buyerName = Truncate(inv.Content.BuyerAddress.Name, 28);
            Console.WriteLine($"{inv.Number,-10} {inv.Content.Date,-12:dd-MM-yyyy} {buyerName,-30} {totalStr,-12}");
        }
    }

    static string? ReadRequired(string prompt)
    {
        Console.Write($"{prompt}: ");
        var value = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            Console.WriteLine("This field is required.");
            return null;
        }
        return value;
    }

    static string? ReadOptional(string prompt)
    {
        Console.Write($"{prompt}: ");
        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";
}
