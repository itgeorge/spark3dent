using System.Text;
using Invoices;

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
}