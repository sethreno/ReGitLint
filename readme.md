# ReGitLint

Integrates the free
[CleanupCode](https://www.jetbrains.com/help/resharper/CleanupCode.html)
command line tool from ReSharper Command Line Tools with git to provide
low friction .net linting for teams without requiring everyone to install
[ReSharper](https://www.jetbrains.com/resharper/).

[CleanupCode](https://www.jetbrains.com/help/resharper/CleanupCode.html)
supports formatting c# as well as vb.net, c++, html, asp.net, razor,
javascript, typescript, css, xml, & xaml.

ReGitLint puts and end to style wars by making it easy to add git hooks
and CI checks to ensure code is formatted consistenlty. Your team will
be holding hands and singing kum ba yah in no time!
![Kum ba yah](https://media2.giphy.com/media/3oz8xClhwv2EnhZeXS/giphy.gif)

Formatting options are specified in
[.editorconfig](https://editorconfig.org/) so everyone can use their
favorite editor. There are many formatting options supported... Here's
[a reference](https://www.jetbrains.com/help/resharper/EditorConfig_Generalized.html)


# Why CleanupCode instead of the built in dotnet format?
`dotnet format` is cool, but currently doesn't support .editorconfig
max_line_length. For curmudgeons like me this is a deal breaker. There's an
[open issue](https://github.com/dotnet/format/issues/246) for it but so far
no fix.


## Quick Start

If you don't already have a tool manifest in your project

    dotnet new tool-manifest

Install jb & regitlint

    dotnet tool install JetBrains.ReSharper.GlobalTools
    dotnet tool install ReGitLint

Now to clean up the whole solution run

    dotnet regitlint

To keep everything formatted you can add a pre-commit hook and build step.
Don't panic!!! ReGitLint has options to format only what's changed so it's fast!

Add the following to .git/hooks/pre-commit

    #!/bin/sh
    dotnet regitlint -f staged --fail-on-diff

ReGitLint will run
[CleanupCode](https://www.jetbrains.com/help/resharper/CleanupCode.html) to
format all staged files. If they don't match
[.editorconfig](https://editorconfig.org/) the commit will fail and you'll see a
message like:

    !!!! Process Aborted !!!!
    Code formatter changed the following files:
     * Directory/SomeCode.cs


To enforce code formatting on the build server add this to your build script

    dotnet tool restore
    dotnet regitlint -f commits -a $env.GIT_PREVIOUS_SUCCESSFUL_COMMIT -b $env.GIT_COMMIT --fail-on-diff --print-diff

Or if you use jenkins you can just add this

    dotnet tool restore
    dotnet regitlint --jenkins

This will only format the files changed between the commit that triggered the
build and the commit that triggered the last successful build. This saves a
lot of time when compared to formatting all files on a large project.


## More Examples:

* Run cleanup on entire solution

    `dotnet regitlint`

* Format only, don't run a full code cleanup

    `dotnet regitlint --format-only`

* Clean up all staged files

    `dotnet regitlint -f staged`

* Clean up all modified files

    `dotnet regitlint -f modified`

* Clean up only c# files

    `dotnet regitlint -p "**/*.cs"`

* Clean up only js files

    `dotnet regitlint -p "**/*.js"`

* Clean up all files modified by commit 3796556

	`dotnet regitlint -f commits -a 3796556`

* Clean up all files modified between commit 6708090 and 3796556

	`dotnet regitlint -f commits -a 6708090 -b 3796556`

* Clean up all files modified by the last four commits

    `dotnet regitlint -f commits -a head^^^^ -b head`

* Clean up all files modified by the last four commits, including staged and unstaged changes

    `dotnet regitlint -f staged,modified,commits -a head^^^^ -b head`

* Clean up staged files, return 1 if files change. Handy for git hooks.

    `dotnet regitlint -f staged --fail-on-diff`

* Clean up files between commits and return 1 if files change. Handy for
  enforcing code formatting on build server.

    `dotnet regitlint -f commits -a 6708090 -b 3796556 --fail-on-diff`

* Enforce code formatting on jenkins

    `dotnet regitlint --jenkins`

* Enforce code formatting on other build servers

    `dotnet regitlint -f commits -a $env.GIT_PREVIOUS_SUCCESSFUL_COMMIT -b $env.GIT_COMMIT --fail-on-diff --print-diff`

* Pass options through to jb cleanupcode

    `dotnet regitlint --jb --toolset=16.0 --jb --exclude="**/*.html"`

----

If you've found ReGitLint helpful you can
[buy me a coffee](https://www.buymeacoffee.com/sethreno) to say thanks.
Happy linting!
