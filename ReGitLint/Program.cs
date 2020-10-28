using System;
using System.Collections.Generic;
using ManyConsole;

namespace ReGitLint {
    internal class Program {
        private static int Main(string[] args) {
            // locate any commands in the assembly (or use an IoC container, or whatever source)
            var commands = GetCommands();

            // run the command for the console input
            return ConsoleCommandDispatcher.DispatchCommand(commands, args,
                Console.Out);
        }

        private static IEnumerable<ConsoleCommand> GetCommands() {
            return ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(
                typeof(Program));
        }
    }
}
