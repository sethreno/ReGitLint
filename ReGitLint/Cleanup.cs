using System.ComponentModel;
using System.Text;
using ManyConsole;

namespace ReGitLint;

public class Cleanup : ConsoleCommand
{
    [Flags]
    public enum FileMatch
    {
        Pattern = 1,
        Staged = 2,
        Modified = 4,
        Commits = 8
    }

    private const int BatchSize = 7000;

    public Cleanup()
    {
        IsCommand("Cleanup", "Runs cleanupcode on the current project.");
        SkipsCommandSummaryBeforeRunning();
        HasOption(
            "s|solution-file=",
            "Optional. Path to .sln file.\n"
                + "By default ReGitLint will use the first sln file it finds.",
            x => SolutionFile = x.Trim()
        );
        HasOption(
            "f|files-to-format=",
            "Optional. Default is Pattern. Choices include:\n"
                + " Pattern      Format files that match pattern.\n"
                + " Staged       Format staged files.\n"
                + " Modified     Format modified files.\n"
                + " Commits      Format files modified\n"
                + "              between commit-a and commit-b.\n",
            x =>
            {
                var flags = (FileMatch)Enum.Parse(typeof(FileMatch), x, true);

                if (
                    flags.HasFlag(FileMatch.Pattern)
                    && flags != FileMatch.Pattern
                )
                {
                    Console.WriteLine(
                        "WARNING: Pattern can't be combined with other sources, assuming pattern only."
                    );
                    FilesToFormat = FileMatch.Pattern;
                }
                else
                {
                    FilesToFormat = flags;
                }
            }
        );
        HasOption(
            "p|pattern=",
            "Optional. Only files matching this pattern will be formatted. "
                + "Default is **/*",
            x => FilePattern = x
        );
        HasOption(
            "a|commit-a=",
            "Partial or full sha hash for commit A ",
            x => CommitA = x
        );
        HasOption(
            "b|commit-b=",
            "Partial or full sha hash for commit B ",
            x => CommitB = x
        );
        HasOption(
            "m|max-runs=",
            "Max number of cleanupcode runs. If more are needed, runs one full cleanup instead. Default is -1 (disabled).",
            x => MaxRuns = int.Parse(x)
        );
        HasOption(
            "jb=",
            "Arg passed through to jb cleanupcode. "
                + "Allows multiple. e.g. --jb -d --jb --toolset=12.0 "
                + "see https://www.jetbrains.com/help/resharper/CleanupCode.html#command-line-parameters "
                + "for a full list of options.",
            x => JbArgs.Add(x)
        );
        HasOption(
            "jb-profile=",
            "Passed to jb cleanupcode as --profile=\"VALUE\""
                + " negates --format-only",
            x => JbProfile = x
        );
        HasOption(
            "format-only",
            "Only format files instead of running full cleanup.",
            x => FormatOnly = x != null
        );
        HasOption(
            "fail-on-diff",
            "Exit with non-zero return code if formatting produces a diff."
                + " Useful for pre-commit hooks or build server stuff.",
            x => FailOnDiff = x != null
        );
        HasOption(
            "skip-tool-check",
            "Skip the check to see if the jb dotnet tool exists.",
            x => SkipToolCheck = x != null
        );
        HasOption(
            "disable-jb-path-hack",
            "This setting is no longer used.",
            x => DisableJbPathHack = x != null
        );
        HasOption(
            "long-form",
            "Call jb command using 'dotnet tool run jb' instead of 'dotnet jb'",
            x => LongForm = x != null
        );
        HasOption(
            "jenkins",
            "Format files changed between recent commits and fail on diff",
            x => Jenkins = x != null
        );
        HasOption(
            "assume-head",
            "If the commit specified doesn't exist HEAD is used instead."
                + " This was added to work around a bug when building pull"
                + " requests via jenkins.",
            x => AssumeHead = x != null
        );
        HasOption(
            "g|use-global",
            "Use the global version of Resharper instead.",
            x => UseGlobalResharper = x != null
        );
        HasOption(
            "print-diff",
            "Prints the full diff on fail-on-diff",
            x => PrintDiff = x != null
        );
        HasOption(
            "print-fix",
            "Prints the regitlint command to run to fix formatting",
            x => PrintFix = x != null
        );
        HasOption(
            "print-command",
            "Prints the jb command before running it",
            x => PrintCommand = x != null
        );
        HasOption(
            "prettier",
            "Exclude file types supported by prettier",
            x => UsePrettier = x != null
        );
    }

    public string SolutionFile { get; set; }
    public FileMatch FilesToFormat { get; set; } = FileMatch.Pattern;
    public string FilePattern { get; set; }
    public string CommitA { get; set; }
    public string CommitB { get; set; }
    public int MaxRuns { get; set; } = -1;
    public string JbProfile { get; set; }
    public List<string> JbArgs { get; set; } = new();
    public bool FormatOnly { get; set; }
    public bool FailOnDiff { get; set; }
    public bool PrintDiff { get; set; }
    public bool PrintFix { get; set; }
    public bool SkipToolCheck { get; set; }
    public bool Jenkins { get; set; }
    public bool PrintCommand { get; set; }
    public bool UsePrettier { get; set; }
    public bool AssumeHead { get; set; }
    public bool UseGlobalResharper { get; set; }
    public bool DisableJbPathHack { get; set; }
    public bool LongForm { get; set; }

    public override int Run(string[] remainingArguments)
    {
        if (Jenkins)
            SetJenkinsOptions();

        if (
            AssumeHead
            && !string.IsNullOrEmpty(CommitA)
            && !DoesCommitExist(CommitA)
        )
        {
            CommitA = "HEAD";
            Console.WriteLine($"commit {CommitA} not found, using HEAD");
        }

        if (
            AssumeHead
            && !string.IsNullOrEmpty(CommitB)
            && !DoesCommitExist(CommitB)
        )
        {
            CommitB = "HEAD";
            Console.WriteLine($"commit {CommitB} not found, using HEAD");
        }

        if (string.IsNullOrEmpty(SolutionFile))
        {
            Console.WriteLine("No sln file specified. Searching for one...");
            SolutionFile = FindSlnFile(".");
            Console.WriteLine($"Found {SolutionFile}. Using that.");
        }
        else
        {
            if (!File.Exists(SolutionFile))
            {
                throw new FileNotFoundException(
                    "Specified sln file does not exist.",
                    SolutionFile
                );
            }
        }

        SolutionFile = Path.GetRelativePath(
            Environment.CurrentDirectory,
            SolutionFile
        );

        // paths are relative to the git root directory
        var filePaths = new HashSet<string>();

        if (FilesToFormat == FileMatch.Pattern)
        {
            var pattern = string.IsNullOrEmpty(FilePattern)
                ? "**/*"
                : FilePattern;
            var returnCode = RunCleanupCode(pattern, SolutionFile);
            if (returnCode != 0)
                return returnCode;
        }
        else
        {
            filePaths = GetFilesToFormat(FilesToFormat, CommitA, CommitB);
            if (!filePaths.Any())
            {
                Console.WriteLine("Nothing to format.");
                return 0;
            }

            var runCount = (int)
                Math.Ceiling(filePaths.Count / (double)BatchSize);

            if (MaxRuns != -1 && runCount > MaxRuns)
            {
                var returnCode = RunCleanupCode("**/*", SolutionFile);
                if (returnCode != 0)
                    return returnCode;
            }
            else
            {
                // absolute paths
                var gitDirAbs = GetGitDirectory();
                var slnDirAbs = Path.GetDirectoryName(
                    Path.GetFullPath(SolutionFile)
                );

                // windows doesn't allow args > ~8100 so call cleanupcode in batches
                var remainingFilePaths = new HashSet<string>(filePaths);
                while (remainingFilePaths.Any())
                {
                    var include = new StringBuilder();
                    foreach (var filePath in remainingFilePaths.ToArray())
                    {
                        if (include.Length + filePath.Length > BatchSize)
                            break;

                        var filePathAbs = Path.Combine(gitDirAbs, filePath);

                        // jb codecleanup requires file paths relative to the sln
                        var jbFilePath = Path.GetRelativePath(
                            slnDirAbs,
                            filePathAbs
                        );

                        if (jbFilePath.StartsWith(".."))
                        {
                            // Workaround for https://youtrack.jetbrains.com/issue/RSRP-475755:
                            // The Ant-style wildcards do not allow to go above the
                            // .sln directory using "../", but by using **/ it
                            // works. This may match too much, but this is better
                            // than matching nothing for our use case.
                            jbFilePath = "**/" + filePath;
                        }

                        if (include.Length > 0)
                        {
                            include.Append(';');
                        }

                        include.Append(jbFilePath);
                        remainingFilePaths.Remove(filePath);
                    }

                    var returnCode = RunCleanupCode(
                        include.ToString(),
                        SolutionFile
                    );

                    if (returnCode != 0)
                        return returnCode;
                }
            }
        }

        if (FailOnDiff)
        {
            var diffFiles = GetFileListFromGit(
                    "diff --name-only --diff-filter=ACM"
                )
                .ToList();

            if (filePaths.Any())
            {
                // we only care about files we formatted
                diffFiles = diffFiles.Intersect(filePaths).ToList();
            }

            if (diffFiles.Any())
            {
                Console.WriteLine();
                Console.WriteLine("!!!! Process Aborted !!!!");
                Console.WriteLine(
                    "The following files do not match .editorconfig:"
                );
                diffFiles.ForEach(x =>
                {
                    Console.WriteLine($" * {x}");
                });

                if (PrintDiff)
                    CmdUtil.Run("git", "diff");

                if (PrintFix)
                    PrintFixCommand();

                return 1;
            }
        }

        return 0;
    }

    private void PrintFixCommand()
    {
        var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

        if (args.Contains("--jenkins"))
        {
            var a = Environment.GetEnvironmentVariable(
                "GIT_PREVIOUS_SUCCESSFUL_COMMIT"
            );

            var b = Environment.GetEnvironmentVariable("GIT_COMMIT");

            args = args.Replace("--jenkins", $"-f commits -a {a} -b {b}");
        }

        var cmd = $"dotnet regitlint {args}";

        Console.WriteLine();
        Console.WriteLine("Run the following command to fix formatting:");
        Console.WriteLine();
        Console.WriteLine($"    {cmd}");
        Console.WriteLine();
    }

    private static string FindSlnFile(string dir)
    {
        var firstSolutionFile = Directory
            .EnumerateFiles(dir, "*.sln", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (firstSolutionFile != null)
        {
            return firstSolutionFile;
        }

        var parentDir = Directory.GetParent(dir);
        if (parentDir == null)
            throw new Exception("could not find sln file");
        return FindSlnFile(parentDir.FullName);
    }

    private static string GetGitDirectory()
    {
        var directoryInfo = new DirectoryInfo(Environment.CurrentDirectory);
        while (directoryInfo != null)
        {
            if (
                directoryInfo
                    .GetDirectories(".git", SearchOption.TopDirectoryOnly)
                    .Any()
            )
            {
                return directoryInfo.FullName;
            }

            directoryInfo = directoryInfo.Parent;
        }

        throw new InvalidOperationException(
            "This tool should be run from within a git repository."
        );
    }

    private void SetJenkinsOptions()
    {
        FailOnDiff = true;
        PrintDiff = true;
        PrintFix = true;
        AssumeHead = true;
        FilesToFormat = FileMatch.Commits;
        CommitA = Environment.GetEnvironmentVariable(
            "GIT_PREVIOUS_SUCCESSFUL_COMMIT"
        );
        CommitB = Environment.GetEnvironmentVariable("GIT_COMMIT");
    }

    private static HashSet<string> GetFilesToFormat(
        FileMatch filesToFormat,
        string commitA,
        string commitB
    )
    {
        var files = new HashSet<string>();

        if (filesToFormat.HasFlag(FileMatch.Modified))
        {
            var newFiles = GetFileListFromGit(
                "ls-files --modified --others --exclude-standard"
            );
            files.AddRange(newFiles);
        }

        if (filesToFormat.HasFlag(FileMatch.Staged))
        {
            var newFiles = GetFileListFromGit("diff --name-only --cached");
            files.AddRange(newFiles);
        }

        if (filesToFormat.HasFlag(FileMatch.Commits))
        {
            if (string.IsNullOrEmpty(commitA))
                commitA = commitB;
            if (string.IsNullOrEmpty(commitB))
                commitB = commitA;
            if (commitA == commitB)
                commitA = $"{commitA}^";

            var gitArgs = $"diff --name-only {commitA} {commitB}";
            var newFiles = GetFileListFromGit(gitArgs);
            files.AddRange(newFiles);
        }

        return files;
    }

    private static bool DoesCommitExist(string sha)
    {
        var args = $"cat-file -t {sha}";
        var exists = false;

        void OutputCallback(string data)
        {
            if (data.StartsWith("commit"))
                exists = true;
            Console.WriteLine(data);
        }

        var returnCode = CmdUtil.Run("git", args, OutputCallback);

        return exists;
    }

    private static List<string> GetFileListFromGit(string gitArgs)
    {
        var files = new HashSet<string>();
        var exitCode = CmdUtil.Run(
            "git",
            gitArgs,
            data => files.Add(data.Trim())
        );

        if (exitCode != 0)
        {
            throw new Exception($"Failed to run git command {gitArgs}");
        }

        return files.ToList();
    }

    private static string GetJbCommand(bool longForm)
    {
        if (longForm) return "tool run jb";
        return "jb";
    }

    private static bool DoesJbToolExist(bool global, bool longForm)
    {
        int exitCode;
        if (!global)
        {
            var jb = GetJbCommand(longForm);
            exitCode = CmdUtil.Run("dotnet", $"{jb} cleanupcode -v");
        }
        else
        {
            try
            {
                exitCode = CmdUtil.Run("jb", "cleanupcode -v");
            }
            catch (Win32Exception ex)
            {
                // throws if jb isn't installed globally
                Console.WriteLine(ex.Message);
                exitCode = -1;
            }
        }

        return exitCode == 0;
    }

    private int RunCleanupCode(string include, string slnFile)
    {
        if (!SkipToolCheck && !DoesJbToolExist(UseGlobalResharper, LongForm))
        {
            var installCommand =
                "dotnet tool install JetBrains.ReSharper.GlobalTools";
            if (UseGlobalResharper)
                installCommand += " --global";

            Console.WriteLine(
                $@"
looks like jb dotnet tool isn't installed...
you can install it by running the following command:

    {
        installCommand
    }
    "
            );
            return 1;
        }

        var jbArgs = new HashSet<string>(JbArgs);

        if (!string.IsNullOrEmpty(JbProfile))
            jbArgs.Add($@"--profile=""{JbProfile}""");

        if (!jbArgs.Any(x => x.StartsWith("--profile")))
        {
            if (FormatOnly)
            {
                jbArgs.Add(@"--profile=""Built-in: Reformat Code""");
            }
            else
            {
                jbArgs.Add(@"--profile=""Built-in: Full Cleanup""");
            }
        }

        if (
            !jbArgs.Contains("-dsl")
            && !jbArgs.Contains("--disable-settings-layers")
        )
        {
            // ignore settings that might conflict with .editorconfig
            jbArgs.Add("-dsl=GlobalAll");
            jbArgs.Add("-dsl=GlobalPerProduct");
            jbArgs.Add("-dsl=SolutionPersonal");
            jbArgs.Add("-dsl=ProjectPersonal");
        }

        var exclude = "";
        if (UsePrettier)
        {
            var extensions = new[]
            {
                "js",
                "jsx",
                "json",
                "html",
                "ts",
                "tsx",
                "css",
                "less",
                "scss",
                "md",
                "yaml"
            };
            exclude = @"--exclude=""";

            foreach (var extension in extensions)
            {
                exclude += $"**/*.{extension};";
            }

            exclude += @"""";
        }

        var command = "dotnet";
        var jb = GetJbCommand(LongForm);
        var baseArgs = $"{jb} cleanupcode";
        if (UseGlobalResharper)
        {
            command = "jb";
            baseArgs = "cleanupcode";
        }

        var args =
            baseArgs
            + $@" ""{slnFile}"" "
            + $@"{exclude} --include=""{include}"" "
            + string.Join(" ", jbArgs);

        if (PrintCommand)
            Console.WriteLine($"{command} {args}");

        // jb returns non zero when there's nothing to format
        // capture that so we can return zero
        var nothingToFormat = false;

        void ErrorCallback(string data)
        {
            if (data.Contains("No items were found to cleanup"))
                nothingToFormat = true;
            Console.WriteLine($"error: {data}");
        }

        var returnCode = CmdUtil.Run(
            command,
            args,
            errorCallback: ErrorCallback,
            // this can take a really long time on large code bases
            cmdTimeout: TimeSpan.FromHours(24),
            // but don't wait longer than 10 minutes for a single file
            // to get formatted
            outputTimeout: TimeSpan.FromMinutes(10)
        );

        if (nothingToFormat)
            return 0;
        return returnCode;
    }
}
