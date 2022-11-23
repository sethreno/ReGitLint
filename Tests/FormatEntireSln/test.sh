#!/bin/sh
../ReGitLint/bin/Release/$1/ReGitLint

diff HelloWorld/Program.cs FormatEntireSln/Expected/Program.cs
