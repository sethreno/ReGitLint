using ManyConsole;

namespace ReGitLint;

internal class Program
{
    private static int Main(string[] args)
    {
        // locate any commands in the assembly
        var commands = GetCommands();

        // run the command for the console input
        return ConsoleCommandDispatcher.DispatchCommand(
            commands,
            args,
            Console.Out
        );
    }

    private static IEnumerable<ConsoleCommand> GetCommands()
    {
        return ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(
            typeof(Program)
        );
    }
}
