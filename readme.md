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


## Quick Start

Add the following to .git/hooks/pre-commit

    #!/bin/sh
    nuget install regitlint -Version 1.5.0 -OutputDirectory packages
    packages/ReGitLint.1.5.0/tools/ReGitLint.exe -s Example.sln -f Staged -d

ReGitLint will run
[CleanupCode](https://www.jetbrains.com/help/resharper/CleanupCode.html) to
format all staged files. If they don't match
[.editorconfig](https://editorconfig.org/) the commit will fail and you'll see a
message like:

    !!!! Process Aborted !!!!
    Code formatter changed the following files:
     * Directory/SomeCode.cs


## More Examples:

* Format everything

    `ReGitLint.exe format -s .\Example.sln`

* Format all staged files

    `ReGitLint.exe format -s .\Example.sln -f Staged`

* Format modified c# files

    `ReGitLint.exe format -s .\Example.sln -f Modified -p "*.cs"`

* Format all files modified by commit 3796556

	`ReGitLint.exe format -s .\Example.sln -f Commits -a 3796556`

* Format all files modified between commit 6708090 and 3796556

    `ReGitLint.exe format -s .\Example.sln -f Commits -a 6708090 -b 3796556`

* Format staged files, return 1 if files change. Handy for git hooks.

    `ReGitLint.exe format -s .\Example.sln -f Staged -d`

* Format files between commits and return 1 if files change. Handy for
  enforcing code formatting on build server.

    `ReGitLint.exe format -s .\Example.sln -f Commits -a 6708090 -b 3796556 -d`

* Enforce code formatting on jenkins

    `ReGitLint.exe format -s .\Example.sln -f -d Commits -a $env.GIT_PREVIOUS_SUCCESSFUL_COMMIT -b $env.GIT_COMMIT`
