using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QaHarness;

internal static class QaHarnessProgram
{
    private static readonly string StateFilePath =
        Path.Combine(Path.GetTempPath(), "spark3dent-qa-state.txt");

    private static readonly string SolutionRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            "stage" => await StageAsync(),
            "run" => await RunAsync(rest),
            "check-file" => CheckFile(rest),
            "invoice-images" => InvoiceImages(),
            "read-log" => ReadLog(),
            "list-files" => ListFiles(rest),
            "cleanup" => Cleanup(),
            "staging-dir" => StagingDir(),
            "agenterrorreport" => AgentErrorReport(rest),
            _ => Error($"Unknown command: {command}. Run without arguments for usage.")
        };
    }

    private static readonly string TestDataDir =
        Path.Combine(SolutionRoot, "QaHarness", "testdata");

    static void PrintUsage()
    {
        Console.WriteLine("QaHarness -- Spark3Dent CLI QA test harness");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project QaHarness -- <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  stage                                   Publish Cli, create isolated staging dir, configure appsettings");
        Console.WriteLine("  run [--stdin-file <name>] <args...>     Run Cli.dll with arguments (and optional piped stdin from testdata/)");
        Console.WriteLine("  run [--stdin \"line1\\nline2\"] <args...>  Run Cli.dll with arguments (and optional piped stdin)");
        Console.WriteLine("  check-file <path>                       Show file info (exists, size, modified, header bytes)");
        Console.WriteLine("  invoice-images                          List PNG invoice images in staging blobs dir");
        Console.WriteLine("  read-log                                Print the staging log file");
        Console.WriteLine("  list-files [subdir]                     Recursive file listing of staging dir");
        Console.WriteLine("  cleanup                                 Delete staging dir and state file");
        Console.WriteLine("  staging-dir                             Print current staging dir path");
        Console.WriteLine("  agenterrorreport [staging-dir]          Create failures-...md file for debug agent (uses state if dir omitted)");
    }

    // ---------------------------------------------------------------
    // stage
    // ---------------------------------------------------------------
    static async Task<int> StageAsync()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var stagingDir = Path.Combine(Path.GetTempPath(), $"spark3dent-qa-{timestamp}");
        var appDir = Path.Combine(stagingDir, "app");

        Console.WriteLine($"Staging directory: {stagingDir}");
        Console.WriteLine($"Solution root:     {SolutionRoot}");
        Console.WriteLine();

        Console.WriteLine("Publishing Cli...");
        var cliProject = Path.Combine(SolutionRoot, "Cli", "Cli.csproj");
        var (exitCode, output) = await RunProcessAsync("dotnet", $"publish \"{cliProject}\" -c Release -o \"{appDir}\"", SolutionRoot);
        Console.WriteLine(output);
        if (exitCode != 0)
            return Error($"dotnet publish failed with exit code {exitCode}");

        var requiredFiles = new[] { "Cli.dll", "appsettings.json" };
        foreach (var file in requiredFiles)
        {
            if (!File.Exists(Path.Combine(appDir, file)))
                return Error($"Published output missing: {file}");
        }

        var chromiumDir = Path.Combine(appDir, "chromium");
        if (!Directory.Exists(chromiumDir))
            Console.WriteLine("Warning: chromium/ directory not found -- PDF/PNG export may download Chromium at runtime.");

        Console.WriteLine("Configuring appsettings.json with staging paths...");
        var configPath = Path.Combine(appDir, "appsettings.json");
        var configResult = PatchAppSettings(configPath, stagingDir);
        if (configResult != 0) return configResult;

        await File.WriteAllTextAsync(StateFilePath, stagingDir, Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine($"Staging ready: {stagingDir}");
        Console.WriteLine($"State saved to: {StateFilePath}");
        return 0;
    }

    static int PatchAppSettings(string configPath, string stagingDir)
    {
        string json;
        try
        {
            json = File.ReadAllText(configPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return Error($"Failed to read {configPath}: {ex.Message}");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            return Error($"Failed to parse {configPath}: {ex.Message}");
        }

        if (root == null)
            return Error($"appsettings.json parsed to null");

        var desktop = root["Desktop"]?.AsObject();
        if (desktop == null)
        {
            desktop = new JsonObject();
            root["Desktop"] = desktop;
        }

        desktop["DatabasePath"] = Path.Combine(stagingDir, "data", "spark3dent.db");
        desktop["BlobStoragePath"] = Path.Combine(stagingDir, "blobs");
        desktop["LogDirectory"] = Path.Combine(stagingDir, "logs");

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var updatedJson = root.ToJsonString(writeOptions);
        File.WriteAllText(configPath, updatedJson, new UTF8Encoding(false));
        Console.WriteLine("appsettings.json patched successfully.");
        return 0;
    }

    // ---------------------------------------------------------------
    // run
    // ---------------------------------------------------------------
    static async Task<int> RunAsync(string[] args)
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null) return 1;

        string? stdinText = null;
        var cliArgs = new List<string>();

        var i = 0;
        while (i < args.Length)
        {
            if (args[i] == "--stdin-file" && i + 1 < args.Length)
            {
                var filePath = Path.Combine(TestDataDir, args[i + 1]);
                if (!File.Exists(filePath))
                    return Error($"Test data file not found: {filePath}");
                stdinText = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                i += 2;
            }
            else if (args[i] == "--stdin" && i + 1 < args.Length)
            {
                stdinText = args[i + 1].Replace("\\n", "\n");
                i += 2;
            }
            else
            {
                cliArgs.Add(args[i]);
                i++;
            }
        }

        if (cliArgs.Count == 0)
            return Error("No CLI arguments provided. Usage: run [--stdin \"...\"] <cli-args...>");

        var cliDll = Path.Combine(stagingDir, "app", "Cli.dll");
        if (!File.Exists(cliDll))
            return Error($"Cli.dll not found at {cliDll}. Did you run 'stage' first?");

        var arguments = $"\"{cliDll}\" {string.Join(" ", cliArgs.Select(QuoteIfNeeded))}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinText != null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = stagingDir
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdinText != null)
        {
            await process.StandardInput.WriteAsync(stdinText);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();

        var stdoutStr = stdout.ToString();
        var stderrStr = stderr.ToString();

        if (stdoutStr.Length > 0)
            Console.Write(stdoutStr);

        if (stderrStr.Length > 0)
        {
            Console.Write("STDERR: ");
            Console.Write(stderrStr);
        }

        Console.WriteLine($"EXIT_CODE: {process.ExitCode}");
        return 0;
    }

    // ---------------------------------------------------------------
    // check-file
    // ---------------------------------------------------------------
    static int CheckFile(string[] args)
    {
        if (args.Length == 0)
            return Error("Usage: check-file <path>");

        var path = args[0];
        if (!Path.IsPathRooted(path))
        {
            var stagingDir = ReadStagingDir();
            if (stagingDir == null) return 1;
            path = Path.Combine(stagingDir, path);
        }

        if (!File.Exists(path))
        {
            Console.WriteLine($"EXISTS: false");
            Console.WriteLine($"PATH: {path}");
            return 0;
        }

        var info = new FileInfo(path);
        Console.WriteLine($"EXISTS: true");
        Console.WriteLine($"PATH: {info.FullName}");
        Console.WriteLine($"SIZE: {info.Length} bytes");
        Console.WriteLine($"MODIFIED: {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");

        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[Math.Min(4, info.Length)];
            var read = fs.Read(header, 0, header.Length);
            var ascii = Encoding.ASCII.GetString(header, 0, read);
            var hex = BitConverter.ToString(header, 0, read);
            Console.WriteLine($"HEADER_ASCII: {ascii}");
            Console.WriteLine($"HEADER_HEX: {hex}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HEADER_ERROR: {ex.Message}");
        }

        return 0;
    }

    // ---------------------------------------------------------------
    // invoice-images
    // ---------------------------------------------------------------
    static int InvoiceImages()
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null) return 1;

        var invoicesDir = Path.Combine(stagingDir, "blobs", "invoices");
        if (!Directory.Exists(invoicesDir))
        {
            Console.WriteLine("No invoices directory found. Issue some invoices first.");
            return 0;
        }

        var pngFiles = Directory.GetFiles(invoicesDir, "*.png")
            .OrderBy(f => f)
            .ToArray();

        if (pngFiles.Length == 0)
        {
            Console.WriteLine("No PNG invoice images found. Did you pass --exportPng when issuing invoices?");
            return 0;
        }

        Console.WriteLine($"Found {pngFiles.Length} invoice image(s):");
        Console.WriteLine();
        foreach (var file in pngFiles)
        {
            var info = new FileInfo(file);
            Console.WriteLine($"{info.FullName} ({info.Length} bytes)");
        }
        return 0;
    }

    // ---------------------------------------------------------------
    // read-log
    // ---------------------------------------------------------------
    static int ReadLog()
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null) return 1;

        var logPath = Path.Combine(stagingDir, "logs", "spark3dent.log");
        if (!File.Exists(logPath))
        {
            Console.WriteLine($"Log file not found: {logPath}");
            Console.WriteLine("Run some CLI commands first to generate log entries.");
            return 0;
        }

        var content = File.ReadAllText(logPath, Encoding.UTF8);
        Console.Write(content);
        return 0;
    }

    // ---------------------------------------------------------------
    // list-files
    // ---------------------------------------------------------------
    static int ListFiles(string[] args)
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null) return 1;

        var targetDir = stagingDir;
        if (args.Length > 0)
        {
            var sub = args[0];
            targetDir = Path.IsPathRooted(sub) ? sub : Path.Combine(stagingDir, sub);
        }

        if (!Directory.Exists(targetDir))
        {
            Console.WriteLine($"Directory not found: {targetDir}");
            return 1;
        }

        Console.WriteLine($"Listing: {targetDir}");
        Console.WriteLine();

        foreach (var file in Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories).OrderBy(f => f))
        {
            var info = new FileInfo(file);
            var relative = Path.GetRelativePath(targetDir, file);
            Console.WriteLine($"  {relative,-60} {info.Length,12:N0} bytes");
        }
        return 0;
    }

    // ---------------------------------------------------------------
    // cleanup
    // ---------------------------------------------------------------
    static int Cleanup()
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null)
        {
            Console.WriteLine("No staging directory to clean up.");
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
            return 0;
        }

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
            Console.WriteLine($"Deleted: {stagingDir}");
        }
        else
        {
            Console.WriteLine($"Staging directory already gone: {stagingDir}");
        }

        File.Delete(StateFilePath);
        Console.WriteLine($"Deleted state file: {StateFilePath}");
        return 0;
    }

    // ---------------------------------------------------------------
    // staging-dir
    // ---------------------------------------------------------------
    static int StagingDir()
    {
        var stagingDir = ReadStagingDir();
        if (stagingDir == null) return 1;
        Console.WriteLine(stagingDir);
        return 0;
    }

    // ---------------------------------------------------------------
    // agenterrorreport
    // ---------------------------------------------------------------
    static int AgentErrorReport(string[] args)
    {
        var stagingDir = args.Length > 0 ? args[0].Trim() : ReadStagingDir();
        if (string.IsNullOrEmpty(stagingDir))
            return Error("No staging directory. Usage: agenterrorreport [staging-dir]");
        if (!Directory.Exists(stagingDir))
            return Error($"Staging directory not found: {stagingDir}");

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var fileName = $"{timestamp}.md";
        var failuresDir = Path.Combine(SolutionRoot, "agent-qa", "failures");
        var filePath = Path.Combine(failuresDir, fileName);

        Directory.CreateDirectory(failuresDir);

        var content = $@"# Agent Testing Failure Report

**Date:** {DateTime.Now:yyyy-MM-dd}
**Timestamp:** {timestamp}
**Source:** `agent-qa/agent-testing.md` playbook execution
**Verdict:** RED

---

## Staging Directory

{stagingDir}

The staging directory contains the failed deployment: published CLI (`app/`), database (`data/`), blob storage (`blobs/`), and logs (`logs/`). Inspect these artifacts to reproduce and debug the issues. Do not run `cleanup` — the staging directory is preserved for investigation.

---

";
        File.WriteAllText(filePath, content, Encoding.UTF8);

        Console.WriteLine(Path.GetFullPath(filePath));
        return 0;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
    static string? ReadStagingDir()
    {
        if (!File.Exists(StateFilePath))
        {
            Console.Error.WriteLine($"Error: No staging directory configured.");
            Console.Error.WriteLine($"Run 'stage' first: dotnet run --project QaHarness -- stage");
            return null;
        }

        var dir = File.ReadAllText(StateFilePath, Encoding.UTF8).Trim();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(Path.Combine(dir, "app")))
        {
            Console.Error.WriteLine($"Error: Staging directory invalid or missing: {dir}");
            Console.Error.WriteLine($"Run 'stage' again to create a fresh deployment.");
            return null;
        }
        return dir;
    }

    static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return (process.ExitCode, output.ToString());
    }

    static string QuoteIfNeeded(string arg) =>
        arg.Contains(' ') ? $"\"{arg}\"" : arg;

    static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
