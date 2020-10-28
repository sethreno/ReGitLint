using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ManyConsole;

namespace ReGitLint {
    public class Cleanup : ConsoleCommand {
        public enum FileMatch {
            Pattern,
            Staged,
            Modified,
            Commits
        }

        public Cleanup() {
            IsCommand(
                "Cleanup",
                "Runs cleanupcode on the current project."
            );
            SkipsCommandSummaryBeforeRunning();
            HasOption(
                "s|solution-file=",
                "Optional. Path to .sln file.\n" +
                "By default ReGitLint will use the first sln file it finds.",
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
                "format-only",
                "Only format files instead of running full cleanup.",
                x => FormatOnly = x != null);
            HasOption(
                "fail-on-diff",
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
        public bool FormatOnly { get; set; }
        public bool FailOnDiff { get; set; }
        public bool SkipToolCheck { get; set; }

        public override int Run(string[] remainingArguments) {
            var files = GetFilesToFormat(
                FilePattern, FilesToFormat, CommitA, CommitB);

            if (!files.Any()) {
                Console.WriteLine("Nothing to format.");
                return 0;
            }

            if (string.IsNullOrEmpty(SolutionFile)) {
                Console.WriteLine(
                    "No sln file specified. Searching for one...");
                SolutionFile = FindSlnFile(".");
                Console.WriteLine($"Found {SolutionFile}. Using that.");
            }

            var profile = FormatOnly ?
                "Built-in: Reformat Code" :
                "Built-in: Full Cleanup";

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
                    GetFileListFromGit("diff --name-only --diff-filter=ACM")
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

        private static string FindSlnFile(string dir) {
            var files =
                Directory.GetFiles(dir, "*.sln", SearchOption.AllDirectories);
            if (files.Any()) return files.First();
            var parentDir = Directory.GetParent(dir);
            if (parentDir == null)
                throw new Exception("could not find sln file");
            return FindSlnFile(parentDir.FullName);
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
            var files = new HashSet<string>();
            var exitCode = CmdUtil.Run("git", gitArgs,
                data => files.Add(data.Trim()));

            if (exitCode != 0) {
                throw new Exception($"Failed to run git command {gitArgs}");
            }

            return files.ToList();
        }

        private static bool DoesJbToolExist() {
            var exitCode = CmdUtil.Run("dotnet", "tool run jb cleanupcode -v");
            return exitCode == 0;
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

            return CmdUtil.Run("dotnet", args,

                // this can take a really long time on large code bases
                cmdTimeout: TimeSpan.FromHours(24),

                // but don't wait longer than 10 minutes for a single file
                // to get formatted
                outputTimeout: TimeSpan.FromMinutes(10)
            );
        }
    }
}
