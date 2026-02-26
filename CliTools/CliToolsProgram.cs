using System.Globalization;
using System.Text;
using Invoices;
using PuppeteerSharp;
using UglyToad.PdfPig;
using Utilities;

namespace CliTools;

internal static class CliToolsProgram
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var argsList = args.Length > 0 ? args.ToList() : null;

        if (argsList != null)
        {
            await RunArgsAsync(argsList);
        }
        else
        {
            await RunInteractiveAsync();
        }
    }

    static async Task RunArgsAsync(List<string> args)
    {
        await ExecuteAsync(args);
    }

    static async Task RunInteractiveAsync()
    {
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;

            var parts = CliOptsParser.ParseLineWithQuotes(line);
            if (parts.Count == 0) continue;

            if (parts[0].Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            await ExecuteAsync(parts);
        }
    }

    static async Task ExecuteAsync(List<string> args)
    {
        var cmd = args[0].ToLowerInvariant();

        if (cmd == "help")
        {
            PrintHelp();
            return;
        }

        if (cmd == "transcribe")
        {
            RunBgAmountTranscriber(args.Skip(1).ToList());
            return;
        }

        if (cmd == "template")
        {
            RunTemplate(args.Skip(1).ToList());
            return;
        }

        if (cmd == "invoice")
        {
            RunInvoice(args.Skip(1).ToList());
            return;
        }

        if (cmd == "prompt")
        {
            RunPrompt(args.Skip(1).ToList());
            return;
        }

        if (cmd == "extract-invoice")
        {
            await RunExtractInvoiceAsync(args.Skip(1).ToList());
            return;
        }

        Console.WriteLine($"Unknown command: {cmd}. Type 'help' for available commands.");
    }

    static void PrintHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  help                    - Show this help");
        Console.WriteLine("  exit                    - Exit (interactive mode only)");
        Console.WriteLine("  transcribe <cents>           - Transcribe amount (cents) using BgAmountTranscriber");
        Console.WriteLine("  transcribe <from> <to>       - Transcribe all amounts in range [from, to] (cents)");
        Console.WriteLine("  transcribe <from> <to> euros - Same, but only whole euro amounts (step by 100)");
        Console.WriteLine("  template [--key value...]     - Render invoice template (dev tool)");
        Console.WriteLine("      --number, -n    Invoice number (default: TPL-001)");
        Console.WriteLine("      --date, -d      Date yyyy-MM-dd (default: today)");
        Console.WriteLine("      --total, -t     Total amount e.g. 213.56 (default: 213.56)");
        Console.WriteLine("      --out, -o       Output file (default: invoice-preview.html)");
        Console.WriteLine("      --seller-name, --seller-mol, --seller-eik, --seller-vat, --seller-addr, --seller-city");
        Console.WriteLine("      --buyer-name, --buyer-mol, --buyer-eik, --buyer-vat, --buyer-addr, --buyer-city");
        Console.WriteLine("      --iban, --bank-name, --bic   Bank transfer info for pay-grid");
        Console.WriteLine("  invoice [--key value...]        - Render invoice to PDF and save to file (dev tool)");
        Console.WriteLine("      Same options as template, --out defaults to invoice.pdf");
        Console.WriteLine("      --png        Also render a PNG image alongside the PDF");
        Console.WriteLine("      --line-items  Comma-separated description:cents (e.g. \"Item1:12000,Item2:8000\")");
        Console.WriteLine("  prompt -m <message> -k <openaikey> [-f <filepath>] - Send a prompt to OpenAI (optionally with file attachment)");
        Console.WriteLine("  extract-invoice -f <filepath> [-k <openaikey>] - Extract BillingAddress from legacy PDF using GPT (key from -k or OPENAI_API_KEY)");
    }

    static async Task RunExtractInvoiceAsync(List<string> args)
    {
        var opts = CliOptsParser.Parse(args, new Dictionary<string, string>
        {
            ["f"] = "file",
            ["k"] = "openaikey",
        });

        if (!opts.TryGetValue("file", out var filePath) || string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("Usage: extract-invoice -f <filepath> [-k <openaikey>]");
            Console.WriteLine("  -f, --file      Path to legacy invoice PDF (required)");
            Console.WriteLine("  -k, --openaikey OpenAI API key (optional; uses OPENAI_API_KEY env var if not provided)");
            return;
        }

        var apiKey = opts.TryGetValue("openaikey", out var k) && !string.IsNullOrWhiteSpace(k)
            ? k
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("Error: OpenAI API key required. Provide -k <key> or set OPENAI_API_KEY environment variable.");
            return;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Error: File not found: {filePath}");
            return;
        }

        try
        {
            var result = await GptLegacyPdfParser.TryParseAsync(filePath, apiKey);
            if (result == null)
            {
                Console.WriteLine("Failed to parse invoice.");
                return;
            }
            Console.WriteLine($"Number: {result.Number}");
            Console.WriteLine($"Date: {result.Date:yyyy-MM-dd}");
            Console.WriteLine($"Total: {result.TotalCents / 100.0:F2} {result.Currency}");
            Console.WriteLine($"Recipient: {result.Recipient.Name}");
            Console.WriteLine($"  MOL: {result.Recipient.RepresentativeName}");
            Console.WriteLine($"  EIK: {result.Recipient.CompanyIdentifier}");
            Console.WriteLine($"  Address: {result.Recipient.Address}");
            Console.WriteLine($"  City: {result.Recipient.City}");
            Console.WriteLine($"  Country: {result.Recipient.Country}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void RunPrompt(List<string> args)
    {
        var opts = CliOptsParser.Parse(args, new Dictionary<string, string>
        {
            ["m"] = "message",
            ["f"] = "file",
            ["k"] = "openaikey",
        });

        if (!opts.TryGetValue("message", out var message) || string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine("Usage: prompt -m <message> -k <openaikey> [-f <filepath>]");
            Console.WriteLine("  -m, --message   The prompt message (required)");
            Console.WriteLine("  -k, --openaikey OpenAI API key (required)");
            Console.WriteLine("  -f, --file      Optional file path to attach (e.g. PDF for extraction)");
            return;
        }

        if (!opts.TryGetValue("openaikey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("Error: -k (openaikey) is required.");
            return;
        }

        try
        {
            var facade = new OpenAiFacade(apiKey);
            string response;

            if (opts.TryGetValue("file", out var filePath) && !string.IsNullOrWhiteSpace(filePath))
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Error: File not found: {filePath}");
                    return;
                }
                var fileBytes = File.ReadAllBytes(filePath);
                var filename = Path.GetFileName(filePath);
                response = facade.PromptAndFile(message, fileBytes, filename).GetAwaiter().GetResult();
            }
            else
            {
                response = facade.Prompt(message).GetAwaiter().GetResult();
            }

            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void RunBgAmountTranscriber(List<string> args)
    {
        if (args.Count == 0)
        {
            Console.WriteLine("Usage: transcribe <cents> or transcribe <from> <to> [euros]");
            return;
        }

        if (!int.TryParse(args[0], out var from))
        {
            Console.WriteLine($"Invalid number: {args[0]}");
            return;
        }

        var to = from;
        if (args.Count >= 2)
        {
            if (!int.TryParse(args[1], out to))
            {
                Console.WriteLine($"Invalid number: {args[1]}");
                return;
            }
            (from, to) = (Math.Min(from, to), Math.Max(from, to));
        }

        var wholeEurosOnly = args.Count >= 3 && args[2].Equals("euros", StringComparison.OrdinalIgnoreCase);
        if (wholeEurosOnly)
        {
            if (from == to)
                from = to = (from + 50) / 100 * 100;
            else
            {
                from = (from + 99) / 100 * 100;
                to = to / 100 * 100;
            }
        }

        var step = wholeEurosOnly ? 100 : 1;
        var transcriber = new BgAmountTranscriber();

        for (var cents = from; cents <= to; cents += step)
        {
            try
            {
                var result = transcriber.Transcribe(new Amount(cents, Currency.Eur));
                Console.WriteLine($"{cents / 100}.{cents % 100:D2} -> {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{cents} -> Error: {ex.Message}");
            }
        }
    }

    static void RunTemplate(List<string> args)
    {
        var opts = CliOptsParser.Parse(args, new Dictionary<string, string>
        {
            ["n"] = "number",
            ["d"] = "date",
            ["t"] = "total",
            ["o"] = "out",
        });

        string GetOrDefault(string key, string d) => opts.TryGetValue(key, out var v) ? v : d;

        var number = GetOrDefault("number", "TPL-001");
        var dateStr = GetOrDefault("date", DateTime.Today.ToString("yyyy-MM-dd"));
        var totalStr = GetOrDefault("total", "213.56");

        if (!DateTime.TryParseExact(dateStr, new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Console.WriteLine($"Invalid date: {dateStr}. Use yyyy-MM-dd");
            return;
        }

        var totalNorm = totalStr.Replace(',', '.');
        if (!decimal.TryParse(totalNorm, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalDec) || totalDec < 0)
        {
            Console.WriteLine($"Invalid total: {totalStr}. Use e.g. 213.56 or 213,56");
            return;
        }
        var totalCents = (int)Math.Round(totalDec * 100);

        var seller = new BillingAddress(
            Name: GetOrDefault("seller-name", "Dev Seller EOOD"),
            RepresentativeName: GetOrDefault("seller-mol", "Иван Проба"),
            CompanyIdentifier: GetOrDefault("seller-eik", "111222333"),
            VatIdentifier: opts.TryGetValue("seller-vat", out var sv) ? sv : null,
            Address: GetOrDefault("seller-addr", "ул. Тестова 1, ет.1"),
            City: GetOrDefault("seller-city", "София"),
            PostalCode: "1000",
            Country: "BG");

        var buyer = new BillingAddress(
            Name: GetOrDefault("buyer-name", "Dev Buyer EOOD"),
            RepresentativeName: GetOrDefault("buyer-mol", "Мария Проба"),
            CompanyIdentifier: GetOrDefault("buyer-eik", "444555666"),
            VatIdentifier: opts.TryGetValue("buyer-vat", out var bv) ? bv : null,
            Address: GetOrDefault("buyer-addr", "ул. Проба 42, ап.5"),
            City: GetOrDefault("buyer-city", "Пловдив"),
            PostalCode: "4000",
            Country: "BG");

        var bankTransferInfo = new BankTransferInfo(
            Iban: GetOrDefault("iban", "BG03FINV91501017534825"),
            BankName: GetOrDefault("bank-name", "FIRST INVESTMENT BANK"),
            Bic: GetOrDefault("bic", "FINVBGSF"));

        var invoice = new Invoice(number, new Invoice.InvoiceContent(
            Date: date,
            SellerAddress: seller,
            BuyerAddress: buyer,
            LineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(totalCents, Currency.Eur)) },
            BankTransferInfo: bankTransferInfo));

        try
        {
            var template = InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber()).GetAwaiter().GetResult();
            var html = template.Render(invoice);

            var outPath = GetOrDefault("out", "invoice-preview.html");
            File.WriteAllText(outPath, html);
            Console.WriteLine(Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void RunInvoice(List<string> args)
    {
        var opts = CliOptsParser.Parse(args, new Dictionary<string, string>
        {
            ["n"] = "number",
            ["d"] = "date",
            ["t"] = "total",
            ["o"] = "out",
            ["l"] = "line-items",
        });

        string GetOrDefault(string key, string d) => opts.TryGetValue(key, out var v) ? v : d;
        bool HasFlag(string key) => CliOptsParser.HasFlag(opts, key);

        var number = GetOrDefault("number", "TPL-001");
        var dateStr = GetOrDefault("date", DateTime.Today.ToString("yyyy-MM-dd"));

        Invoice.LineItem[] lineItems;
        if (opts.TryGetValue("line-items", out var lineItemsStr))
        {
            lineItems = ParseLineItems(lineItemsStr);
            if (lineItems.Length == 0)
            {
                Console.WriteLine("Invalid --line-items. Use format \"desc1:cents1,desc2:cents2\"");
                return;
            }
        }
        else
        {
            var totalStr = GetOrDefault("total", "213.56");
            var totalNorm = totalStr.Replace(',', '.');
            if (!decimal.TryParse(totalNorm, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalDec) || totalDec < 0)
            {
                Console.WriteLine($"Invalid total: {totalStr}. Use e.g. 213.56 or 213,56");
                return;
            }
            var totalCents = (int)Math.Round(totalDec * 100);
            lineItems = new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(totalCents, Currency.Eur)) };
        }

        if (!DateTime.TryParseExact(dateStr, new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Console.WriteLine($"Invalid date: {dateStr}. Use yyyy-MM-dd");
            return;
        }

        var seller = new BillingAddress(
            Name: GetOrDefault("seller-name", "Dev Seller EOOD"),
            RepresentativeName: GetOrDefault("seller-mol", "Иван Проба"),
            CompanyIdentifier: GetOrDefault("seller-eik", "111222333"),
            VatIdentifier: opts.TryGetValue("seller-vat", out var sv) ? sv : null,
            Address: GetOrDefault("seller-addr", "ул. Тестова 1, ет.1"),
            City: GetOrDefault("seller-city", "София"),
            PostalCode: "1000",
            Country: "BG");

        var buyer = new BillingAddress(
            Name: GetOrDefault("buyer-name", "Dev Buyer EOOD"),
            RepresentativeName: GetOrDefault("buyer-mol", "Мария Проба"),
            CompanyIdentifier: GetOrDefault("buyer-eik", "444555666"),
            VatIdentifier: opts.TryGetValue("buyer-vat", out var bv) ? bv : null,
            Address: GetOrDefault("buyer-addr", "ул. Проба 42, ап.5"),
            City: GetOrDefault("buyer-city", "Пловдив"),
            PostalCode: "4000",
            Country: "BG");

        var bankTransferInfo = new BankTransferInfo(
            Iban: GetOrDefault("iban", "BG03FINV91501017534825"),
            BankName: GetOrDefault("bank-name", "FIRST INVESTMENT BANK"),
            Bic: GetOrDefault("bic", "FINVBGSF"));

        var invoice = new Invoice(number, new Invoice.InvoiceContent(
            Date: date,
            SellerAddress: seller,
            BuyerAddress: buyer,
            LineItems: lineItems,
            BankTransferInfo: bankTransferInfo));

        try
        {
            var fetcher = new BrowserFetcher();
            fetcher.DownloadAsync().GetAwaiter().GetResult();
            var template = InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber()).GetAwaiter().GetResult();
            var outPath = GetOrDefault("out", "invoice.pdf");

            var pdfExporter = new InvoicePdfExporter();
            using (var pdfStream = pdfExporter.Export(template, invoice).GetAwaiter().GetResult())
            using (var fileStream = File.Create(outPath))
            {
                pdfStream.CopyTo(fileStream);
            }
            Console.WriteLine(Path.GetFullPath(outPath));

            if (HasFlag("png"))
            {
                var pngPath = Path.ChangeExtension(outPath, ".png");
                var imageExporter = new InvoiceImageExporter();
                using (var pngStream = imageExporter.Export(template, invoice).GetAwaiter().GetResult())
                using (var fileStream = File.Create(pngPath))
                {
                    pngStream.CopyTo(fileStream);
                }
                Console.WriteLine(Path.GetFullPath(pngPath));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static Invoice.LineItem[] ParseLineItems(string value)
    {
        var result = new List<Invoice.LineItem>();
        foreach (var part in value.Split(','))
        {
            var colon = part.IndexOf(':');
            if (colon < 0) continue;
            var desc = part[..colon].Trim();
            if (desc.Length == 0) continue;
            if (!int.TryParse(part[(colon + 1)..].Trim(), out var cents) || cents < 0) continue;
            result.Add(new Invoice.LineItem(desc, new Amount(cents, Currency.Eur)));
        }
        return result.ToArray();
    }

}