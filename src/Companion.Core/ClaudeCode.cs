using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Companion.Core;

/// <summary>Finds claude.exe itself: starting claude.cmd would route arguments through cmd.exe, which mangles them.</summary>
public static class ClaudeCodeLocator
{
    private const string PackageExe = @"node_modules\@anthropic-ai\claude-code\bin\claude.exe";

    public static string? Find(string? overridePath) => Find(
        overridePath,
        File.Exists,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static string? Find(string? overridePath, Func<string, bool> exists, string pathVariable, string userProfile, string appData)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && exists(overridePath)) return overridePath;
        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exe = Path.Combine(dir, "claude.exe");
            if (exists(exe)) return exe;
            if (exists(Path.Combine(dir, "claude.cmd")))
            {
                var packaged = Path.Combine(dir, PackageExe);
                if (exists(packaged)) return packaged;
            }
        }
        foreach (var candidate in new[] { Path.Combine(userProfile, ".local", "bin", "claude.exe"), Path.Combine(appData, "npm", PackageExe) })
        {
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>Finds Companion.Memory.exe: next to the app when published, or in its dev build folder.</summary>
public static class MemoryServerLocator
{
    public const string ExeName = "Companion.Memory.exe";

    public static string? Find(AppPaths paths, string baseDirectory) => Find(paths, baseDirectory, File.Exists);

    internal static string? Find(AppPaths paths, string baseDirectory, Func<string, bool> exists)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, ExeName),
            Path.Combine(paths.Root, "src", "Companion.Memory", "bin", "Debug", "net10.0-windows", ExeName),
            Path.Combine(paths.Root, "src", "Companion.Memory", "bin", "Release", "net10.0-windows", ExeName),
        };
        return candidates.FirstOrDefault(exists);
    }
}

/// <summary>Reads Claude Code's <c>--output-format stream-json</c> lines for one turn.</summary>
public sealed class ClaudeCodeStreamParser(ITurnHandler handler)
{
    private readonly StringBuilder _text = new();
    private bool _firstTextThisMessage = true;

    public string Text => _text.ToString();
    public bool Finished { get; private set; }
    public bool IsError { get; private set; }
    public string? ResultText { get; private set; }
    public TurnStop Stop { get; private set; } = TurnStop.EndTurn;
    public string? LastApiError { get; private set; }
    public int? LastApiErrorStatus { get; private set; }
    public bool LimitRejected { get; private set; }
    public DateTimeOffset? LimitResetsAt { get; private set; }

    public void Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            handler.OnActivity();
            switch (Str(root, "type"))
            {
                case "stream_event" when root.TryGetProperty("event", out var ev):
                    OnStreamEvent(ev);
                    break;
                case "system" when Str(root, "subtype") == "api_retry":
                    LastApiError = Str(root, "error");
                    LastApiErrorStatus = root.TryGetProperty("error_status", out var status) && status.ValueKind == JsonValueKind.Number ? status.GetInt32() : null;
                    break;
                case "rate_limit_event" when root.TryGetProperty("rate_limit_info", out var info):
                    if (Str(info, "status") == "rejected")
                    {
                        LimitRejected = true;
                        if (info.TryGetProperty("resetsAt", out var resets) && resets.ValueKind == JsonValueKind.Number)
                            LimitResetsAt = DateTimeOffset.FromUnixTimeSeconds(resets.GetInt64());
                    }
                    break;
                case "result":
                    Finished = true;
                    IsError = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;
                    ResultText = Str(root, "result");
                    Stop = Str(root, "stop_reason") switch
                    {
                        "max_tokens" => TurnStop.MaxTokens,
                        "refusal" => TurnStop.Refusal,
                        _ => TurnStop.EndTurn,
                    };
                    break;
            }
        }
    }

    /// <summary>Turns a failed or unfinished run into a ClaudeException with a user-facing message.</summary>
    public ClaudeException ToFailure(string stderr)
    {
        var detail = string.Join(" | ", new[] { ResultText, LastApiError, stderr.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (detail.Length == 0) detail = "Claude Code exited without an answer.";
        if (LimitRejected || LastApiErrorStatus == 429 || Has(detail, "usage limit") || Has(detail, "rate limit"))
        {
            var when = LimitResetsAt is { } at ? $" It resets at {at.ToLocalTime():HH:mm}." : "";
            return new ClaudeException(ClaudeErrorKind.PlanLimit, detail, userMessage: $"You've reached your Claude plan's usage limit.{when}");
        }
        if (Has(detail, "/login") || Has(detail, "not logged in") || Has(detail, "authentication") || (!Finished && LastApiErrorStatus == 401))
            return new ClaudeException(ClaudeErrorKind.NotLoggedIn, detail);
        if (Has(detail, "connection error") || Has(detail, "ENOTFOUND") || Has(detail, "ECONNREFUSED") || Has(detail, "network"))
            return new ClaudeException(ClaudeErrorKind.Network, detail);
        if (LastApiErrorStatus >= 500 || Has(detail, "overloaded") || Has(detail, " 5"))
            return new ClaudeException(ClaudeErrorKind.Server, detail);
        return new ClaudeException(ClaudeErrorKind.Unknown, detail);
    }

    private void OnStreamEvent(JsonElement ev)
    {
        switch (Str(ev, "type"))
        {
            case "message_start":
                _firstTextThisMessage = true;
                break;
            case "content_block_start" when ev.TryGetProperty("content_block", out var block):
                if (Str(block, "type") == "tool_use" && Str(block, "name") == "WebSearch") handler.OnWebSearch();
                break;
            case "content_block_delta" when ev.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta":
                var piece = Str(delta, "text") ?? "";
                if (piece.Length == 0) return;
                if (_firstTextThisMessage && _text.Length > 0 && _text[^1] != '\n') piece = "\n\n" + piece;
                _firstTextThisMessage = false;
                _text.Append(piece);
                handler.OnTextDelta(piece);
                break;
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
}

public sealed record ClaudeCodeOptions(
    string ExePath,
    string EverydayModel,
    string ThinkHarderModel,
    string? MemoryServerExe,
    string MemoryFile,
    string WorkingDirectory);

/// <summary>Answers through the user's installed Claude Code (their subscription): one claude.exe process per turn.</summary>
public sealed class ClaudeCodeClient(ClaudeCodeOptions options) : IClaudeClient
{
    /// <summary>Nothing to validate: Claude Code uses its own login.</summary>
    public Task ValidateKeyAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct)
    {
        Directory.CreateDirectory(options.WorkingDirectory);
        var start = new ProcessStartInfo(options.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = options.WorkingDirectory,
        };
        foreach (var argument in BuildArguments(request, options)) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ClaudeException(ClaudeErrorKind.NotInstalled, ex.Message, ex);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var parser = new ClaudeCodeStreamParser(handler);
        using var registration = ct.Register(() => Kill(process));
        try
        {
            await process.StandardInput.WriteAsync(BuildPrompt(request.Messages).AsMemory(), ct);
            process.StandardInput.Close();
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line) parser.Feed(line);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new ClaudeException(ClaudeErrorKind.Unknown, ex.Message, ex);
        }

        var stderr = await stderrTask;
        if (!parser.Finished || parser.IsError) throw parser.ToFailure(stderr);
        return new TurnResult(parser.Text, parser.Stop, new UsageTotals());
    }

    internal static IReadOnlyList<string> BuildArguments(TurnRequest request, ClaudeCodeOptions options)
    {
        var model = request.Effort == EffortLevel.High ? options.ThinkHarderModel : options.EverydayModel;
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--model", model,
            "--effort", request.Effort.ToString().ToLowerInvariant(),
            "--system-prompt", request.SystemPrompt,
            "--tools", request.WebSearch ? "WebSearch" : "",
            "--permission-mode", "dontAsk",
            "--settings", """{"disableAllHooks":true}""",
            "--setting-sources", "",
            "--no-session-persistence",
            "--strict-mcp-config",
        };
        var allowed = new List<string>();
        if (request.WebSearch) allowed.Add("WebSearch");
        if (options.MemoryServerExe is { } server)
        {
            args.Add("--mcp-config");
            args.Add(JsonSerializer.Serialize(new
            {
                mcpServers = new
                {
                    companion = new { type = "stdio", command = server, args = new[] { "--memory", options.MemoryFile } },
                },
            }));
            allowed.Add("mcp__companion__remember");
            allowed.Add("mcp__companion__forget");
        }
        if (allowed.Count > 0)
        {
            args.Add("--allowedTools");   // variadic: keep it last
            args.AddRange(allowed);
        }
        return args;
    }

    internal static string BuildPrompt(IReadOnlyList<HistoryMessage> messages)
    {
        if (messages.Count == 1) return messages[0].Text;
        var sb = new StringBuilder("Our conversation so far (you are the assistant):\n\n");
        for (var i = 0; i < messages.Count - 1; i++)
        {
            sb.Append(messages[i].FromUser ? "User: " : "You: ").Append(messages[i].Text).Append("\n\n");
        }
        sb.Append("---\nThe user's new message:\n").Append(messages[^1].Text);
        return sb.ToString();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
