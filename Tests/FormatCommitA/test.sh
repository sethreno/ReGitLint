#!/bin/bash

../ReGitLint/bin/Release/$1/ReGitLint -f commits -a bb3f9ba222 > /dev/null

# ClassA.cs should get formatted
diff ../Tests/HelloWorld/ClassA.cs ../Tests/FormatCommitA/Expected/ClassA.cs

# Program.cs was not in the commit so should still be unformatted
diff ../Tests/HelloWorld/Program.cs ../Tests/FormatCommitA/Expected/Program.cs
