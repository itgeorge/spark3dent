using System.Text;

namespace Utilities;

/// <summary>
/// Shared CLI option parsing utilities. Handles --key value and -s value style arguments.
/// </summary>
public static class CliOptsParser
{
    /// <summary>
    /// Splits a line into arguments, respecting double and single-quoted strings.
    /// E.g. <c>prompt -m "hello world" -f 'C:\My Path\file.pdf'</c> yields correct tokens.
    /// </summary>
    public static List<string> ParseLineWithQuotes(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        char? inQuote = null;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuote.HasValue)
            {
                if (c == inQuote.Value)
                {
                    inQuote = null;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    /// <summary>
    /// Parses command-line arguments into key-value options (supports --key and -s shortcuts)
    /// and returns remaining positional arguments.
    /// </summary>
    /// <param name="args">The argument list (e.g. from a subcommand).</param>
    /// <param name="shortToLong">Optional map of short keys (e.g. "n") to long keys (e.g. "number").</param>
    /// <returns>Dictionary of options (case-insensitive keys) and list of positional args.</returns>
    public static (Dictionary<string, string> Opts, List<string> Positional) ParseWithPositional(
        List<string> args,
        Dictionary<string, string>? shortToLong = null)
    {
        shortToLong ??= new Dictionary<string, string>();
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--"))
            {
                var key = a[2..].ToLowerInvariant();
                if (i + 1 < args.Count && !args[i + 1].StartsWith("-"))
                {
                    opts[key] = args[i + 1];
                    i++;
                }
                else
                {
                    opts[key] = "true";
                }
            }
            else if (a.Length == 2 && a[0] == '-')
            {
                var shortKey = char.ToLowerInvariant(a[1]).ToString();
                if (shortToLong.TryGetValue(shortKey, out var longKey) && i + 1 < args.Count)
                {
                    opts[longKey] = args[i + 1];
                    i++;
                }
            }
            else
            {
                positional.Add(a);
            }
        }

        return (opts, positional);
    }

    /// <summary>
    /// Parses options only, discarding positional args.
    /// </summary>
    public static Dictionary<string, string> Parse(List<string> args, Dictionary<string, string>? shortToLong = null)
    {
        var (opts, _) = ParseWithPositional(args, shortToLong);
        return opts;
    }

    /// <summary>
    /// Returns true if the option is present and has a non-empty value (including "true" for flags).
    /// </summary>
    public static bool HasFlag(Dictionary<string, string> opts, string key)
    {
        return opts.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
    }
}
