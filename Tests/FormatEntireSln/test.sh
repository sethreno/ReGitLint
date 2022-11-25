#!/bin/sh

# give everyone write permissions
# without doing this I recieved the following error from jb
#
#   error: Files still read-only: /srv/Tests/HelloWorld/Program.cs

../ReGitLint/bin/Release/$1/ReGitLint > /dev/null

diff HelloWorld/Program.cs FormatEntireSln/Expected/Program.cs
