using Companion.Core;

var paths = AppPaths.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("COMPANION_ROOT"));
paths.EnsureCreated();
var clock = new SystemClock();
var log = new FileLog(paths.Logs, clock);
var secrets = new SecretStore(paths.SecretsFile, new DpapiProtector(), log);

if (args.Length == 2 && args[0] == "--save-key")
{
    secrets.SaveApiKey(args[1]);
    Console.WriteLine($"API key saved (encrypted) to {paths.SecretsFile}");
    return 0;
}

var temp = Path.Combine(Path.GetTempPath(), "companion-smoke-" + Guid.NewGuid().ToString("N"));
var settings = new SettingsStore(Path.Combine(temp, "settings.json"), clock, log);

IClaudeClient client;
if (args.Contains("--subscription"))
{
    var exe = ClaudeCodeLocator.Find(null) ?? throw new InvalidOperationException("Claude Code not found.");
    var server = MemoryServerLocator.Find(paths, AppContext.BaseDirectory);
    client = new ClaudeCodeClient(new ClaudeCodeOptions(exe, "claude-haiku-4-5", "claude-sonnet-5", server,
        Path.Combine(temp, "memory.json"), Path.Combine(temp, "workspace")));
}
else
{
    var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? secrets.LoadApiKey();
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("No API key. Run: dotnet run --project tools/Smoke -- --save-key sk-ant-...");
        return 1;
    }

    var anthropicClient = new AnthropicClaudeClient(key);
    await anthropicClient.ValidateKeyAsync(CancellationToken.None);
    Console.WriteLine("Key OK.");
    client = anthropicClient;
}

var memory = new MemoryStore(Path.Combine(temp, "memory.json"), clock, log);
var tools = new ToolRegistry();
tools.Register(new RememberTool(memory));
tools.Register(new ForgetTool(memory));
var spend = new SpendTracker(settings, clock);
var brain = new Brain(client, new Conversation(clock), () => SystemPrompt.Build("Pip", "Tester", memory.Facts),
    () => memory.Version, tools, spend, settings, null, log);

string[] questions =
[
    "Remember that my favourite colour is teal. Then tell me a one-line joke.",
    "What's my favourite colour?",
    "Search the web: what is the latest stable .NET version? One sentence.",
    // Search + a client tool in one turn: the tool round must echo every server block (code execution included).
    "Search the web for today's date in Tokyo, then remember that I asked about Tokyo.",
];
foreach (var question in questions)
{
    Console.WriteLine($"\n> {question}");
    var reply = await brain.AskAsync(question, new ConsoleListener());
    memory.Reload();
    var u = brain.LastUsage;
    Console.WriteLine($"\n[{reply.Outcome}] notice={reply.Notice ?? "-"} in={u.InputTokens} out={u.OutputTokens} cacheRead={u.CacheReadTokens} cacheWrite={u.CacheWriteTokens} searches={u.WebSearches}");
}

Console.WriteLine($"\nFacts: {string.Join(" | ", memory.Facts.Select(f => f.Text))}");
Console.WriteLine($"Estimated cost of this smoke run: ${spend.ThisMonth:0.0000}");
return 0;

sealed class ConsoleListener : IBrainListener
{
    public void OnThinking() => Console.Write("(thinking) ");
    public void OnFirstText() { }
    public void OnText(string delta) => Console.Write(delta);
    public void OnStatus(string status) => Console.Write($"({status}) ");
    public void OnActivity() { }
}
