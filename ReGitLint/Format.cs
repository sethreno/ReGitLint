using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using ManyConsole;

namespace ReCleanWrap {
    public class Format : ConsoleCommand {
        public enum FileMatch {
            Pattern,
            Staged,
            Modified,
            Commits
        }

        public Format() {
            IsCommand(
                "Format",
                "Formats code using cleanupcode and the current .editorconfig. "
                + "Must be run from the root of the git repo containing files "
                + "to format.");
            SkipsCommandSummaryBeforeRunning();
            HasRequiredOption(
                "s|solution-file=",
                "Path to .sln or .csproj file.",
                x => SolutionFile = x.Trim());
            HasOption(
                "f|files-to-format=",
                "Optional. Default is Pattern. Choices include:\n" +
                " Pattern      Format files that match pattern.\n" +
                " Staged       Format staged files.\n" +
                " Modified     Format modified files.\n" +
                " Commits      Format files modified\n" +
                "              between commit-a and commit-b.\n",
                x => FilesToFormat =
                    (FileMatch)Enum.Parse(typeof(FileMatch), x, true));
            HasOption(
                "p|pattern=",
                "Optional. Only files matching this pattern will be formatted. "
                + "Default is **/*",
                x => FilePattern = x);
            HasOption(
                "a|commit-a=",
                "Partial or full sha hash for commit A ",
                x => CommitA = x);
            HasOption(
                "b|commit-b=",
                "Partial or full sha hash for commit B ",
                x => CommitB = x);
            HasOption(
                "c|full-cleanup",
                "Run full cleanup in addition to formatting",
                x => FullCleanup = x != null);
            HasOption(
                "d|fail-on-diff",
                "Exit with non-zero return code if formatting produces a diff."
                + " Useful for pre-commit hooks or build server stuff.",
                x => FailOnDiff = x != null);
            HasOption(
                "skip-tool-check",
                "Skip the check to see if the jb dotnet tool exists.",
                x => SkipToolCheck = x != null);
        }

        public string SolutionFile { get; set; }
        public FileMatch FilesToFormat { get; set; } = FileMatch.Pattern;
        public string FilePattern { get; set; }
        public string CommitA { get; set; }
        public string CommitB { get; set; }
        public bool FullCleanup { get; set; }
        public bool FailOnDiff { get; set; }
        public bool SkipToolCheck { get; set; }

        public override int Run(string[] remainingArguments) {
            var files = GetFilesToFormat(
                FilePattern, FilesToFormat, CommitA, CommitB);

            if (!files.Any()) {
                Console.WriteLine("Nothing to format.");
                return 0;
            }

            var profile = FullCleanup ?
                "Built-in: Full Cleanup" :
                "Built-in: Reformat Code";

            // windows doesn't allow args > ~8100 so call cleanupcode in batches
            var remain = new HashSet<string>(files);
            while (remain.Any()) {
                var include = new StringBuilder();
                foreach (var file in remain.ToArray()) {
                    if (include.Length + file.Length > 7000) break;
                    include.Append($";{file}");
                    remain.Remove(file);
                }

                var returnCode = RunCleanupCode(
                    profile, include.ToString(), SolutionFile);

                if (returnCode != 0) return returnCode;
            }

            if (FailOnDiff) {
                var diffFiles =
                    GetFileListFromGit("git diff --name-only --diff-filter=ACM")
                        .ToList();

                if (diffFiles.Any()) {
                    if (FilesToFormat == FileMatch.Staged ||
                        FilesToFormat == FileMatch.Commits) {
                        // we only care about files we formatted
                        diffFiles = diffFiles.Intersect(files).ToList();
                    }

                    if (diffFiles.Any()) {
                        Console.WriteLine();
                        Console.WriteLine("!!!! Process Aborted !!!!");
                        Console.WriteLine(
                            "Code formatter changed the following files:");
                        diffFiles.ForEach(
                            x => { Console.WriteLine($" * {x}"); });
                        return 1;
                    }
                }
            }

            return 0;
        }

        private static HashSet<string> GetFilesToFormat(
            string pattern,
            FileMatch filesToFormat,
            string commitA,
            string commitB
        ) {
            var files = new HashSet<string>();
            var gitArgs = "";
            switch (filesToFormat) {
                case FileMatch.Pattern:
                    files.Add(string.IsNullOrEmpty(pattern) ? "**/*" : pattern);
                    return files;

                case FileMatch.Staged:
                    gitArgs = "diff --name-only --diff-filter=ACM --cached";
                    break;

                case FileMatch.Modified:
                    gitArgs = "diff --name-only --diff-filter=ACM";
                    break;

                case FileMatch.Commits:
                    gitArgs = "diff --name-only --diff-filter=ACM"
                        + $" {commitA} {commitB}";
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            GetFileListFromGit(gitArgs)
                .ToList()
                .ForEach(x => files.Add(x));

            return files;
        }

        private static List<string> GetFileListFromGit(string gitArgs) {
            // todo use cross platform git lib or cross platform powershell
            // or maybe just call git directly?
            var files = new HashSet<string>();

            using (var process = new Process()) {
                process.StartInfo.FileName = "git";
                process.StartInfo.Arguments = gitArgs;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                using (var outputWaitHandle = new AutoResetEvent(false))
                using (var errorWaitHandle = new AutoResetEvent(false)) {
                    process.OutputDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            outputWaitHandle.Set();
                        } else {
                            files.Add(e.Data.Trim());
                        }
                    };
                    process.ErrorDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            errorWaitHandle.Set();
                        } else {
                            Console.WriteLine(e.Data);
                        }
                    };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var overallTimeout =
                        (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
                    var outputTimeout =
                        (int)TimeSpan.FromMinutes(1).TotalMilliseconds;

                    if (process.WaitForExit(overallTimeout) &&
                        outputWaitHandle.WaitOne(outputTimeout) &&
                        errorWaitHandle.WaitOne(outputTimeout)) {
                        return files.ToList();
                    }
                }

                throw new Exception($"git {gitArgs} timed out");
            }
        }

        private static bool DoesJbToolExist() {
            using (var process = new Process()) {
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments = "tool run jb cleanupcode -v";
                process.Start();
                process.WaitForExit();
                return (process.ExitCode == 0);
            }
        }

        private int RunCleanupCode(
            string profile,
            string include,
            string slnFile
        ) {
            if (!SkipToolCheck && !DoesJbToolExist()) {
                Console.WriteLine(@"
looks like jb dotnet tool isn't installed...
you can install it by running the following command:

dotnet tool install JetBrains.ReSharper.GlobalTools");
                return 1;
            }

            const string flags =
                "-dsl=GlobalAll -dsl=SolutionPersonal -dsl=ProjectPersonal";

            var args = $@"tool run jb cleanupcode ""{slnFile}"" {flags}"
                + $@" --profile=""{profile}"" --include=""{include}""";

            return RunCommand("dotnet", args,

                // this can take a really long time on large code bases
                cmdTimeout: TimeSpan.FromHours(24),

                // but don't wait longer than 10 minutes for a single file
                // to get formatted
                outputTimeout: TimeSpan.FromMinutes(10)
            );
        }

        private int RunCommand(
            string cmd,
            string args,
            Action<string> outputCallback = null,
            Action<string> errorCallback = null,
            TimeSpan? cmdTimeout = null,
            TimeSpan? outputTimeout = null
        ) {
            void WriteToConsole(string data) {
                Console.WriteLine(data);
            }

            outputCallback = outputCallback ?? WriteToConsole;
            errorCallback = errorCallback ?? WriteToConsole;
            cmdTimeout = cmdTimeout ?? TimeSpan.FromMinutes(10);
            outputTimeout = outputTimeout ?? TimeSpan.FromMinutes(1);

            using (var process = new Process()) {
                process.StartInfo.FileName = cmd;
                process.StartInfo.Arguments = args;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                using (var outputWaitHandle = new AutoResetEvent(false))
                using (var errorWaitHandle = new AutoResetEvent(false)) {
                    process.OutputDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            outputWaitHandle.Set();
                        } else {
                            outputCallback(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            errorWaitHandle.Set();
                        } else {
                            errorCallback(e.Data);
                        }
                    };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var cmdTimeoutMs = cmdTimeout.Value.TotalMilliseconds;
                    var outTimeoutMs = outputTimeout.Value.TotalMilliseconds;

                    if (process.WaitForExit((int)cmdTimeoutMs) &&
                        outputWaitHandle.WaitOne((int)outTimeoutMs) &&
                        errorWaitHandle.WaitOne((int)outTimeoutMs)) {
                        return process.ExitCode;
                    }

                    Console.WriteLine($"{cmd} timed out");
                    return 1;
                }
            }
        }
    }
}
