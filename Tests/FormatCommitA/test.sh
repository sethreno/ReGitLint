#!/bin/bash

../ReGitLint/bin/Release/$1/ReGitLint -f commits -a HEAD > /dev/null

diff HelloWorld/ClassA.cs FormatCommitA/Expected/ClassA.cs
