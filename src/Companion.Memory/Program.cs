using System.Text;
using Companion.Core;

var index = Array.IndexOf(args, "--memory");
if (index < 0 || index + 1 >= args.Length)
{
    Console.Error.WriteLine("usage: Companion.Memory --memory <memory.json>");
    return 2;
}

var memory = new MemoryStore(args[index + 1], new SystemClock(), NullLog.Instance);
var tools = new ToolRegistry();
tools.Register(new RememberTool(memory));
tools.Register(new ForgetTool(memory));

using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
await new McpStdioServer(tools).RunAsync(input, output);
return 0;
