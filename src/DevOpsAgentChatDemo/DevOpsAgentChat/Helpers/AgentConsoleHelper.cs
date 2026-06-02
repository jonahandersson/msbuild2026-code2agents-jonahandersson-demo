using Spectre.Console;

namespace DevOpsAgentChat.Helpers
{
    public static class AgentConsoleHelper
    {
        public static void WriteSuccess(string message)
        {
            AnsiConsole.MarkupLine($"[green]{message}[/]");
        }

        public static void WriteWarning(string message)
        {
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
        }

        public static void WriteError(string message)
        {
            AnsiConsole.MarkupLine($"[red]{message}[/]");
        }

        public static void WriteInfo(string message)
        {
            AnsiConsole.MarkupLine($"[blue]{message}[/]");
        }
    }
}
