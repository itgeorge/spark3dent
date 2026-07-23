using System.Text.Json;
using Database;
using Orders;
using Utilities;

namespace Cli;

internal static class SchedulingOrderOwnerCommands
{
    public static async Task<int> RunAsync(string subcommand, string[] args, string defaultDbPath)
    {
        var (opts, positional) = CliOptsParser.ParseWithPositional(args.ToList());
        if (positional.Count > 0)
        {
            Console.WriteLine("Unexpected positional argument(s): " + string.Join(" ", positional));
            PrintUsage(subcommand);
            return 2;
        }

        var dbPath = GetOption(opts, "db") ?? defaultDbPath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            Console.WriteLine("Error: database path is not configured. Pass --db <path>.");
            return 2;
        }

        try
        {
            if (subcommand.Equals("order-owner-report", StringComparison.OrdinalIgnoreCase))
                return await RunReportAsync(dbPath, opts);

            if (subcommand.Equals("order-owner-validate", StringComparison.OrdinalIgnoreCase))
                return await RunValidateAsync(dbPath, opts, forceCurrentMismatch: false);

            if (subcommand.Equals("order-owner-apply", StringComparison.OrdinalIgnoreCase))
                return await RunApplyAsync(dbPath, opts);

            PrintUsage(subcommand);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunReportAsync(string dbPath, Dictionary<string, string> opts)
    {
        var outPath = GetOption(opts, "out");
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.WriteLine("Error: --out <path> is required.");
            return 2;
        }

        var report = await SchedulingOrderOwnerMigration.GenerateReportAsync(dbPath);
        var json = SchedulingOrderOwnerMigration.SerializeReport(report);
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(outPath, json);
        Console.WriteLine($"Wrote {report.Orders.Count} order(s) to {outPath}");
        return 0;
    }

    private static async Task<int> RunValidateAsync(string dbPath, Dictionary<string, string> opts, bool forceCurrentMismatch)
    {
        var assignmentsPath = GetOption(opts, "assignments");
        if (string.IsNullOrWhiteSpace(assignmentsPath))
        {
            Console.WriteLine("Error: --assignments <path> is required.");
            return 2;
        }

        forceCurrentMismatch = forceCurrentMismatch || CliOptsParser.HasFlag(opts, "force-current-mismatch");
        var result = await SchedulingOrderOwnerMigration.ValidateAsync(dbPath, assignmentsPath, forceCurrentMismatch);
        SchedulingOrderOwnerMigration.PrintValidationSummary(Console.Out, result.Summary, result.Errors);
        return result.Errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> RunApplyAsync(string dbPath, Dictionary<string, string> opts)
    {
        var assignmentsPath = GetOption(opts, "assignments");
        var outPath = GetOption(opts, "out");
        if (string.IsNullOrWhiteSpace(assignmentsPath))
        {
            Console.WriteLine("Error: --assignments <path> is required.");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.WriteLine("Error: --out <path> is required.");
            return 2;
        }
        if (!CliOptsParser.HasFlag(opts, "backup-confirmed"))
        {
            Console.WriteLine("Error: apply requires --backup-confirmed after completing a database backup.");
            return 2;
        }

        var forceCurrentMismatch = CliOptsParser.HasFlag(opts, "force-current-mismatch");
        var result = await SchedulingOrderOwnerMigration.ApplyAsync(
            dbPath,
            assignmentsPath,
            Path.GetFileName(assignmentsPath),
            backupConfirmed: true,
            forceCurrentMismatch);

        SchedulingOrderOwnerMigration.PrintValidationSummary(Console.Out, result.Summary, result.Errors);
        if (result.Errors.Count > 0)
            return 1;

        var json = SchedulingOrderOwnerMigration.SerializeApplyResult(result);
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(outPath, json);
        Console.WriteLine($"Updated {result.UpdatedOrders.Count} order(s). Wrote apply result to {outPath}");
        return 0;
    }

    private static string? GetOption(Dictionary<string, string> opts, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (opts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static void PrintUsage(string subcommand)
    {
        if (subcommand.Equals("order-owner-report", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: scheduling order-owner-report --db <path> --out <path>");
            return;
        }

        if (subcommand.Equals("order-owner-validate", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: scheduling order-owner-validate --db <path> --assignments <path> [--force-current-mismatch]");
            return;
        }

        if (subcommand.Equals("order-owner-apply", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: scheduling order-owner-apply --db <path> --assignments <path> --backup-confirmed --out <path> [--force-current-mismatch]");
        }
    }
}
