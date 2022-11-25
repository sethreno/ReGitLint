#!/bin/bash

# exit if any command fails
set -e

../ReGitLint/bin/Release/$1/ReGitLint --long-form --print-command > long.txt

if grep -q "dotnet tool run jb " long.txt; then
    echo "found 'dotnet tool run jb '"
else
    echo "expected to find 'found 'dotnet tool run jb '"
    exit 1
fi

rm long.txt


../ReGitLint/bin/Release/$1/ReGitLint --print-command > short.txt
if grep -q "dotnet jb " short.txt; then
    echo "found 'dotnet jb '"
else
    echo "fail: expected to find 'dotnet jb '"
    exit 1
fi

rm short.txt