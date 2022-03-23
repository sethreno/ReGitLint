using System.ComponentModel;
using System.Text;
using ManyConsole;

namespace ReGitLint;

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
            + "Default is ** / *",
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
            "jb=",
            "Arg passsed through to jb cleanupcode. " +
            "Allows multiple. e.g. --jb -d --jb --toolset=12.0 " +
            "see https://www.jetbrains.com/help/resharper/CleanupCode.html#command-line-parameters " +
            "for a full list of options.",
            x => JbArgs.Add(x));
        HasOption(
            "jb-profile=",
            "Passed to jb cleanupcode as --profile \"VALUE\"" +
            " negates --format-only",
            x => JbProfile = x);
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
        HasOption(
            "disable-jb-path-hack",
            "Don't prefix file paths sent to jb cleanupcode with '**/'. " +
            "May reduce false positive matches.",
            x => DisableJbPathHack = x != null);
        HasOption(
            "jenkins",
            "Format files changed between recent commits and fail on diff",
            x => Jenkins = x != null);
        HasOption(
            "assume-head",
            "If the commit specified doesnt exist HEAD is used instead."
            + " This was added to work around a bug when building pull"
            + " requests via jenkins.",
            x => AssumeHead = x != null);
        HasOption(
            "g|use-global",
            "Use the global version of Resharper instead.",
            x => UseGlobalResharper = x != null);
        HasOption(
            "print-diff",
            "Prints the full diff on fail-on-diff",
            x => PrintDiff = x != null);
        HasOption(
            "print-fix",
            "Prints the regitlint command to run to fix formatting",
            x => PrintFix = x != null);
        HasOption(
            "print-command",
            "Prints the jb command before running it",
            x => PrintCommand = x != null);
        HasOption(
            "prettier",
            "Exclude file types supported by prettier",
            x => UsePrettier = x != null);
    }

    public string SolutionFile { get; set; }
    public FileMatch FilesToFormat { get; set; } = FileMatch.Pattern;
    public string FilePattern { get; set; }
    public string CommitA { get; set; }
    public string CommitB { get; set; }
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

    public override int Run(string[] remainingArguments) {
        if (Jenkins) SetJenkinsOptions();

        if (AssumeHead && !string.IsNullOrEmpty(CommitA)
            && !DoesCommitExist(CommitA)) {
            CommitA = "HEAD";
            Console.WriteLine($"commit {CommitA} not found, using HEAD");
        }

        if (AssumeHead && !string.IsNullOrEmpty(CommitB)
            && !DoesCommitExist(CommitB)) {
            CommitB = "HEAD";
            Console.WriteLine($"commit {CommitB} not found, using HEAD");
        }

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

        var solutionDir = Path.GetDirectoryName(SolutionFile);
        if (solutionDir.StartsWith(".\\") || solutionDir.StartsWith("./"))
            solutionDir = solutionDir.Substring(2);

        // windows doesn't allow args > ~8100 so call cleanupcode in batches
        var remain = new HashSet<string>(files);
        while (remain.Any()) {
            var include = new StringBuilder();
            foreach (var file in remain.ToArray()) {
                if (include.Length + file.Length > 7000) break;

                // jb codecleanup requires file paths relative to the sln
                var jbFilePath = file;
                if (file.StartsWith(solutionDir))
                    jbFilePath = file.Substring(solutionDir.Length);

                // hack - I haven't been able to figure out how to specify
                // relative paths when the .sln file is contained in a peer
                // directory to the project directores.
                // using **/ which may match too much, but I guess this is
                // better than mathching nothing for our use case
                var prefix = DisableJbPathHack ? "" : "**/";
                include.Append($";{prefix}{jbFilePath}");
                remain.Remove(file);
            }

            var returnCode = RunCleanupCode(
                include.ToString(), SolutionFile);

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
                        "The following files do not match .editorconfig:");
                    diffFiles.ForEach(x => { Console.WriteLine($" * {x}"); });

                    if (PrintDiff) CmdUtil.Run("git", "diff");

                    if (PrintFix) PrintFixCommand();

                    return 1;
                }
            }
        }

        return 0;
    }

    private void PrintFixCommand() {
        var args = string.Join(
            " ", Environment.GetCommandLineArgs().Skip(1));

        if (args.Contains("--jenkins")) {
            var a = Environment.GetEnvironmentVariable(
                "GIT_PREVIOUS_SUCCESSFUL_COMMIT");

            var b = Environment.GetEnvironmentVariable(
                "GIT_COMMIT");

            args = args.Replace("--jenkins", $"-f commits -a {a} -b {b}");
        }

        var cmd = $"dotnet regitlint {args}";

        Console.WriteLine();
        Console.WriteLine("Run the following command to fix formatting:");
        Console.WriteLine();
        Console.WriteLine($"    {cmd}");
        Console.WriteLine();
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

    private void SetJenkinsOptions() {
        FailOnDiff = true;
        PrintDiff = true;
        PrintFix = true;
        AssumeHead = true;
        FilesToFormat = FileMatch.Commits;
        CommitA = Environment.GetEnvironmentVariable(
            "GIT_PREVIOUS_SUCCESSFUL_COMMIT");
        CommitB = Environment.GetEnvironmentVariable("GIT_COMMIT");
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
                gitArgs = "diff --name-only --cached";
                break;

            case FileMatch.Modified:
                gitArgs = "ls-files --modified --others --exclude-standard";
                break;

            case FileMatch.Commits:
                if (string.IsNullOrEmpty(commitA)) commitA = commitB;
                if (string.IsNullOrEmpty(commitB)) commitB = commitA;
                if (commitA == commitB) commitA = $"{commitA}^";

                gitArgs = $"diff --name-only {commitA} {commitB}";
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        GetFileListFromGit(gitArgs)
            .ToList()
            .ForEach(x => files.Add(x));

        return files;
    }

    private static bool DoesCommitExist(string sha) {
        var args = $"cat-file -t {sha}";
        var exists = false;

        void OutputCallback(string data) {
            if (data.StartsWith("commit")) exists = true;
            Console.WriteLine(data);
        }

        var returnCode = CmdUtil.Run("git", args,
            OutputCallback
        );

        return exists;
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

    private static bool DoesJbToolExist(bool global) {
        int exitCode;
        if (!global) {
            exitCode = CmdUtil.Run("dotnet", "tool run jb cleanupcode -v");
        } else {
            try {
                exitCode = CmdUtil.Run("jb", "cleanupcode -v");
            } catch (Win32Exception ex) {
                // throws if jb isn't installed globally
                Console.WriteLine(ex.Message);
                exitCode = -1;
            }
        }

        return exitCode == 0;
    }

    private int RunCleanupCode(
        string include,
        string slnFile
    ) {
        if (!SkipToolCheck && !DoesJbToolExist(UseGlobalResharper)) {
            var installCommand =
                "dotnet tool install JetBrains.ReSharper.GlobalTools";
            if (UseGlobalResharper) installCommand += " --global";

            Console.WriteLine($@"
looks like jb dotnet tool isn't installed...
you can install it by running the following command:

    {
        installCommand
    }
    ");
            return 1;
        }

        var jbArgs = new HashSet<string>(JbArgs);

        if (!string.IsNullOrEmpty(JbProfile))
            jbArgs.Add($@"--profile ""{JbProfile}""");

        if (!jbArgs.Any(x => x.StartsWith("--profile"))) {
            if (FormatOnly) {
                jbArgs.Add(@"--profile=""Built-in: Reformat Code""");
            } else {
                jbArgs.Add(@"--profile=""Built-in: Full Cleanup""");
            }
        }

        if (!jbArgs.Contains("-dsl")
            && !jbArgs.Contains("--disable-settings-layers")) {
            // ignore settings that might conflict with .editorconfig
            jbArgs.Add("-dsl=GlobalAll");
            jbArgs.Add("-dsl=GlobalPerProduct");
            jbArgs.Add("-dsl=SolutionPersonal");
            jbArgs.Add("-dsl=ProjectPersonal");
        }

        var exclude = "";
        if (UsePrettier) {
            var extensions = new[] {
                "js", "jsx", "json", "html", "ts", "tsx", "css", "less", "scss",
                "md", "yaml"
            };
            exclude = @"--exclude=""";

            foreach (var extension in extensions) {
                exclude += $"**/*.{extension};";
            }

            exclude += @"""";
        }

        var command = "dotnet";
        var baseArgs = "tool run jb cleanupcode";
        if (UseGlobalResharper) {
            command = "jb";
            baseArgs = "cleanupcode";
        }

        var args = baseArgs
            + $@" ""{slnFile}"" "
            + $@"{exclude} --include=""{include}"" "
            + string.Join(" ", jbArgs);

        if (PrintCommand) Console.WriteLine($"{command} {args}");

        // jb returns non zero when there's nothing to format
        // capture that so we can return zero
        var nothingToFormat = false;

        void ErrorCallback(string data) {
            if (data.Contains("No items were found to cleanup"))
                nothingToFormat = true;
            Console.WriteLine($"error: {data}");
        }

        var returnCode = CmdUtil.Run(command, args,
            errorCallback: ErrorCallback,

            // this can take a really long time on large code bases
            cmdTimeout: TimeSpan.FromHours(24),

            // but don't wait longer than 10 minutes for a single file
            // to get formatted
            outputTimeout: TimeSpan.FromMinutes(10)
        );

        if (nothingToFormat) return 0;
        return returnCode;
    }
}
