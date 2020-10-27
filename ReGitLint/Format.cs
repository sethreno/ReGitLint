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
            PatternOnly,
            Staged,
            Modified,
            Commits
        }

        public Format() {
            IsCommand(
                "Format",
                "Formats code using cleanupcode.exe and the current " +
                ".editorconfig. Must be run from the root of the git repo " +
                "containing files to format.");
            SkipsCommandSummaryBeforeRunning();
            HasRequiredOption(
                "s|solution-file=",
                "Path to .sln or .csproj file.",
                x => SolutionFile = x.Trim());
            HasOption(
                "f|files-to-format=",
                "Optional. Default is PatternOnly. Choices include:\n" +
                " PatternOnly  Format files that match\n" +
                "              file-pattern.\n" +
                " Staged       Format staged files that\n" +
                "              match file-pattern.\n" +
                " Modified     Format modified files that\n" +
                "              match file-pattern.\n" +
                " Commits      Format files modified\n" +
                "              between commit-a and commit-b\n" +
                "              that match file-pattern.",
                x => FilesToFormat =
                    (FileMatch)Enum.Parse(typeof(FileMatch), x, true));
            HasOption(
                "p|file-pattern=",
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
        }

        public string SolutionFile { get; set; }
        public FileMatch FilesToFormat { get; set; } = FileMatch.PatternOnly;
        public string FilePattern { get; set; }
        public string CommitA { get; set; }
        public string CommitB { get; set; }
        public bool FullCleanup { get; set; }
        public bool FailOnDiff { get; set; }

        public override int Run(string[] remainingArguments) {
            var files = GetFilesToFormat(FilePattern, FilesToFormat, CommitA,
                CommitB);

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
            var gitCommand = "";
            switch (filesToFormat) {
                case FileMatch.PatternOnly:
                    files.Add(string.IsNullOrEmpty(pattern) ? "**/*" : pattern);
                    break;
                case FileMatch.Staged:
                    gitCommand =
                        "git diff --name-only --diff-filter=ACM --cached";
                    break;
                case FileMatch.Modified:
                    gitCommand = "git diff --name-only --diff-filter=ACM";
                    break;
                case FileMatch.Commits:
                    gitCommand = "git diff --name-only --diff-filter=ACM"
                        + $" {commitA} {commitB}";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            /* 			GetFileListFromGit(gitCommand)
                            .Where(x =>
                                string.IsNullOrEmpty(pattern) ||
                                Operators.LikeString(x, pattern, CompareMethod.Text))
                            .ToList()
                            .ForEach(x => files.Add(x)); */

            return files;
        }

        private static List<string> GetFileListFromGit(string gitCommand) {
            // todo use cross platform git lib or cross platform powershell
            /* 			using (var ps = PowerShell.Create()) {
                            ps.AddScript(gitCommand);
                            return ps.Invoke<string>().ToList();
                        } */
            return new List<string>();
        }

        private static int RunCleanupCode(string profile, string include,
            string slnFile) {
            const string flags =
                "-dsl=GlobalAll -dsl=SolutionPersonal -dsl=ProjectPersonal";

            using (var process = new Process()) {
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments = "tool run jb cleanupcode -v";
                process.Start();
                process.WaitForExit();
                if (process.ExitCode == 1) {
                    // todo add option to install automatically
                    Console.WriteLine(@"
looks like jb dotnet tool isn't installed...
you can install it by running the following command:

	dotnet tool install JetBrains.ReSharper.GlobalTools");
                    return 1;
                }
            }

            using (var process = new Process()) {
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments =
                    $@"tool run jb cleanupcode ""{slnFile}"" {flags}"
                    + $@" --profile=""{profile}"" --include=""{include}""";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                using (var outputWaitHandle = new AutoResetEvent(false))
                using (var errorWaitHandle = new AutoResetEvent(false)) {
                    process.OutputDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            outputWaitHandle.Set();
                        } else {
                            Console.WriteLine(e.Data);
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

                    // this can take a really long time on large code bases
                    var overallTimeout =
                        (int)TimeSpan.FromHours(24).TotalMilliseconds;

                    // but don't wait longer than 10 minutes for a single file
                    // to get formatted
                    var outputTimeout =
                        (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

                    if (process.WaitForExit(overallTimeout) &&
                        outputWaitHandle.WaitOne(outputTimeout) &&
                        errorWaitHandle.WaitOne(outputTimeout)) return 0;

                    Console.WriteLine("cleanupcode.exe timed out");
                    return 1;
                }
            }
        }
    }
}
