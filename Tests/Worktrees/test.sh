#!/bin/bash
set -e

target_framework="${1:-net10.0}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tests_dir="$(cd "$script_dir/.." && pwd)"

regitlint_binary="$tests_dir/../ReGitLint/bin/Release/$target_framework/ReGitLint.dll"

temp_dir=$(mktemp -d)
cleanup() {
    rm -rf "$temp_dir"
}
trap cleanup EXIT

export GIT_AUTHOR_NAME="Test Runner"
export GIT_AUTHOR_EMAIL="test@example.com"
export GIT_COMMITTER_NAME="Test Runner"
export GIT_COMMITTER_EMAIL="test@example.com"

# Restore tools once at the root of temp_dir so all subdirectories inherit it
cp -r "$tests_dir/../.config" "$temp_dir/"
(
    cd "$temp_dir"
    dotnet tool restore >/dev/null
)

# 1. Setup sub-sln-src (submodule that HAS its own .sln in src/)
dotnet new classlib -n SubSlnLib -f "$target_framework" -o "$temp_dir/sub-sln-src/src/SubSlnLib" >/dev/null
mv "$temp_dir/sub-sln-src/src/SubSlnLib/Class1.cs" "$temp_dir/sub-sln-src/src/SubSlnLib/SubSlnClass.cs"
cat << "EOF" > "$temp_dir/sub-sln-src/src/SubSlnLib/SubExtraUnformatted.cs"
public class SubExtraUnformatted {
    int unformattedSubField;
}
EOF
dotnet new sln -n "Sub" -o "$temp_dir/sub-sln-src/src" >/dev/null
(
    cd "$temp_dir/sub-sln-src/src"
    dotnet sln add SubSlnLib/SubSlnLib.csproj >/dev/null
    cd ..
    git init -q && git add . && git commit -q -m "init sub-sln"
)

# 2. Setup sub-nosln-src (submodule with NO .sln)
dotnet new classlib -n SubNoSlnLib -f "$target_framework" -o "$temp_dir/sub-nosln-src/src/SubNoSlnLib" >/dev/null
mv "$temp_dir/sub-nosln-src/src/SubNoSlnLib/Class1.cs" "$temp_dir/sub-nosln-src/src/SubNoSlnLib/SubNoSlnClass.cs"
(
    cd "$temp_dir/sub-nosln-src"
    git init -q && git add . && git commit -q -m "init sub-nosln"
)

# 3. Setup main-repo (has .sln in src/)
dotnet new classlib -n MainLib -f "$target_framework" -o "$temp_dir/main-repo/src/MainLib" >/dev/null
mv "$temp_dir/main-repo/src/MainLib/Class1.cs" "$temp_dir/main-repo/src/MainLib/MainClass.cs"
cat << "EOF" > "$temp_dir/main-repo/src/MainLib/MainExtraUnformatted.cs"
public class MainExtraUnformatted {
    int unformattedMainField;
}
EOF
dotnet new sln -n "Main" -o "$temp_dir/main-repo/src" >/dev/null
(
    cd "$temp_dir/main-repo/src"
    dotnet sln add MainLib/MainLib.csproj >/dev/null
    cd ..
    git init -q && git add . && git commit -q -m "init main"
    git -c protocol.file.allow=always submodule -q add "$temp_dir/sub-sln-src" sub-with-sln
    git -c protocol.file.allow=always submodule -q add "$temp_dir/sub-nosln-src" sub-without-sln
    cd src
    dotnet sln add ../sub-without-sln/src/SubNoSlnLib/SubNoSlnLib.csproj >/dev/null
    cd ..
    git commit -q -a -m "add submodules"
)

# 4. Worktree of main-repo
(
    cd "$temp_dir/main-repo"
    git worktree add -q "$temp_dir/main-wt" -b wt-branch
    cd "$temp_dir/main-wt"
    git -c protocol.file.allow=always submodule update --init -q
)

# 5. Worktree of submodule sub-with-sln
(
    cd "$temp_dir/main-repo/sub-with-sln"
    git worktree add -q "$temp_dir/sub-wt" -b sub-wt-branch
)

# Test 1: Worktree of main repo, run from src/ (subdir of worktree root)
echo "Test 1: Create a git repo, create a worktree, execute from src/ subdir"
(
    cd "$temp_dir/main-wt/src"
    echo "class UnformattedWt { int x; }" >> MainLib/MainClass.cs
    dotnet $regitlint_binary -f modified >/dev/null
    if ! git diff MainLib/MainClass.cs | grep -q "private int x;"; then
        echo "FAIL Test 1: MainClass.cs was not formatted"
        exit 1
    fi
    if git -C "$temp_dir/main-repo" diff | grep -q "private int unformattedMainField;"; then
        echo "FAIL Test 1: main-repo was unexpectedly touched"
        exit 1
    fi
)
echo "Test 1 passed: worktree detected and formatted from src/ subdir."

# Test 2: Submodule with own sln, run from src/ (subdir of submodule root)
echo "Test 2: Create a submodule with its own .sln, execute from src/ subdir"
(
    cd "$temp_dir/main-repo/sub-with-sln/src"
    echo "class UnformattedSubSln { int x; }" >> SubSlnLib/SubSlnClass.cs
    dotnet $regitlint_binary -f modified >/dev/null
    if ! git diff SubSlnLib/SubSlnClass.cs | grep -q "private int x;"; then
        echo "FAIL Test 2: SubSlnClass.cs was not formatted"
        exit 1
    fi
    if git diff SubSlnLib/SubExtraUnformatted.cs | grep -q "private int unformattedSubField;"; then
        echo "FAIL Test 2: untouched file was formatted"
        exit 1
    fi
    if git -C "$temp_dir/main-repo" diff | grep -q "private int unformattedMainField;"; then
        echo "FAIL Test 2: parent repo was unexpectedly touched"
        exit 1
    fi
)
echo "Test 2 passed: submodule with own sln detected and formatted from src/ subdir."

# Test 3: Submodule without sln, run from sub-without-sln root (using parent .sln)
echo "Test 3: Create a submodule without .sln, execute from submodule root"
(
    cd "$temp_dir/main-repo/sub-without-sln"
    echo "class UnformattedSubNoSln { int x; }" >> src/SubNoSlnLib/SubNoSlnClass.cs
    dotnet $regitlint_binary -f modified >/dev/null
    if ! git diff src/SubNoSlnLib/SubNoSlnClass.cs | grep -q "private int x;"; then
        echo "FAIL Test 3: SubNoSlnClass.cs was not formatted"
        exit 1
    fi
    if git -C "$temp_dir/main-repo" diff | grep -q "private int unformattedMainField;"; then
        echo "FAIL Test 3: parent repo was unexpectedly touched"
        exit 1
    fi
)
echo "Test 3 passed: submodule without sln detected and formatted using parent sln."

# Test 4: Submodule inside worktree, run from src/ (subdir of submodule in worktree)
echo "Test 4: Create a submodule inside a worktree, execute from src/ subdir"
(
    cd "$temp_dir/main-wt/sub-with-sln/src"
    echo "class UnformattedSubInWt { int x; }" >> SubSlnLib/SubSlnClass.cs
    dotnet $regitlint_binary -f modified >/dev/null
    if ! git diff SubSlnLib/SubSlnClass.cs | grep -q "private int x;"; then
        echo "FAIL Test 4: SubSlnClass.cs in worktree was not formatted"
        exit 1
    fi
)
echo "Test 4 passed: submodule in worktree detected and formatted from src/ subdir."

# Test 5: Worktree of submodule, run from src/ (subdir of submodule worktree)
echo "Test 5: Create a worktree of a submodule, execute from src/ subdir"
(
    cd "$temp_dir/sub-wt/src"
    echo "class UnformattedSubWt { int x; }" >> SubSlnLib/SubSlnClass.cs
    dotnet $regitlint_binary -f modified >/dev/null
    if ! git diff SubSlnLib/SubSlnClass.cs | grep -q "private int x;"; then
        echo "FAIL Test 5: SubSlnClass.cs in submodule worktree was not formatted"
        exit 1
    fi
)
echo "Test 5 passed: worktree of submodule detected and formatted from src/ subdir."
