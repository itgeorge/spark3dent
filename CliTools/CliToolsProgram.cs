using System.Globalization;
using System.Text;
using Invoices;
using PuppeteerSharp;

namespace CliTools;

internal static class CliToolsProgram
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var argsList = args.Length > 0 ? args.ToList() : null;

        if (argsList != null)
        {
            RunArgs(argsList);
        }
        else
        {
            RunInteractive();
        }
    }

    static void RunArgs(List<string> args)
    {
        Execute(args);
    }

    static void RunInteractive()
    {
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0) continue;

            if (parts[0].Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            Execute(parts);
        }
    }

    static void Execute(List<string> args)
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
        Console.WriteLine("      --line-items  Comma-separated description:cents (e.g. \"Item1:12000,Item2:8000\")");
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
        var opts = ParseOpts(args, new Dictionary<string, string>
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
        var opts = ParseOpts(args, new Dictionary<string, string>
        {
            ["n"] = "number",
            ["d"] = "date",
            ["t"] = "total",
            ["o"] = "out",
            ["l"] = "line-items",
        });

        string GetOrDefault(string key, string d) => opts.TryGetValue(key, out var v) ? v : d;

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
            var exporter = new InvoicePdfExporter();
            using var pdfStream = exporter.Export(template, invoice).GetAwaiter().GetResult();
            var outPath = GetOrDefault("out", "invoice.pdf");
            using (var fileStream = File.Create(outPath))
            {
                pdfStream.CopyTo(fileStream);
            }
            Console.WriteLine(Path.GetFullPath(outPath));
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

    static Dictionary<string, string> ParseOpts(List<string> args, Dictionary<string, string> shortToLong)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--"))
            {
                var key = a[2..].ToLowerInvariant();
                if (i + 1 < args.Count)
                {
                    result[key] = args[i + 1];
                    i++;
                }
            }
            else if (a.Length == 2 && a[0] == '-')
            {
                var shortKey = char.ToLowerInvariant(a[1]).ToString();
                if (shortToLong.TryGetValue(shortKey, out var longKey) && i + 1 < args.Count)
                {
                    result[longKey] = args[i + 1];
                    i++;
                }
            }
        }
        return result;
    }
}