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

If you don't already have a tool manifest in your project

    dotnet new tool-manifest

Install jb & regitlint

    dotnet tool install JetBrains.ReSharper.GlobalTools
    dotnet tool install ReGitLint

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


## More Examples:

* Format everything

    `dotnet regitlint`

* Format all staged files

    `dotnet regitlint -f staged`

* Format all c# files

    `dotnet regitlint -p "**/*.cs"`

* Format all files modified by commit 3796556

	`dotnet regitlint -f commits -a 3796556`

* Format all files modified between commit 6708090 and 3796556

	`dotnet regitlint -f commits -a 6708090 -b 3796556`

* Format staged files, return 1 if files change. Handy for git hooks.

    `dotnet regitlint -f staged --fail-on-diff`

* Format files between commits and return 1 if files change. Handy for
  enforcing code formatting on build server.

    `dotnet regitlint -f commits -a 6708090 -b 3796556 --fail-on-diff`

* Enforce code formatting on jenkins

    `dotnet regitlint -f -d commits -a $env.GIT_PREVIOUS_SUCCESSFUL_COMMIT -b $env.GIT_COMMIT`


----

If you've found ReGitLint helpful you can
[buy me a coffee](https://www.buymeacoffee.com/sethreno) to say thanks.
Happy linting!
