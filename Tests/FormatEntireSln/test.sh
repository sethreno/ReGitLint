#!/bin/sh
dotnet tool restore

# give everyone write permissions
# without doing this I recieved the following error from jb
#
#   error: Files still read-only: /srv/Tests/HelloWorld/Program.cs
sudo chmod -R 777 HelloWorld

../ReGitLint/bin/Release/$1/ReGitLint

diff HelloWorld/Program.cs FormatEntireSln/Expected/Program.cs
