# Git Worktrees & Submodules Integration Tests

This directory contains automated end-to-end integration tests for `ReGitLint` in various Git worktree and submodule configurations.

## 1. The Core Problem

In a standard Git repository, `.git` is a **directory**.

However:
- In a **Git worktree**, `.git` is a **file** containing a pointer (e.g. `gitdir: /path/to/main/.git/worktrees/...`).
- In a **Git submodule**, `.git` is also a **file** containing a pointer (e.g. `gitdir: ../.git/modules/...`).

When `ReGitLint` runs with options like `-f modified`, it must:
1. Walk up from the current working directory until it finds `.git` (whether it is a directory or a pointer file).
2. Determine the Git repository root.
3. Query `git diff` for modified files relative to that root.
4. Locate the appropriate solution (`.sln`) file and format **only** the target modified files, without crashing, getting confused by parent/child repository boundaries, or formatting files in another repository.

---

## 2. Test Environment Setup

The test script creates an isolated temporary directory using the `dotnet` CLI (`dotnet new`) and standard `git` commands, establishing 5 distinct working trees:

```text
temp_dir/
├── main-repo/                      # Standard Git repository
│   ├── .git/                       # [DIR]
│   ├── src/
│   │   ├── Main.sln                # References MainLib & sub-without-sln
│   │   └── MainLib/MainClass.cs
│   ├── sub-with-sln/               # Submodule WITH its own .sln
│   │   ├── .git                    # [FILE] -> points to main-repo/.git/modules/...
│   │   └── src/
│   │       ├── Sub.sln
│   │       └── SubSlnLib/SubSlnClass.cs
│   └── sub-without-sln/            # Submodule WITHOUT its own .sln
│       ├── .git                    # [FILE] -> points to main-repo/.git/modules/...
│       └── src/
│           └── SubNoSlnLib/SubNoSlnClass.cs
│
├── main-wt/                        # Worktree of main-repo
│   ├── .git                        # [FILE] -> points to main-repo/.git/worktrees/...
│   ├── src/
│   │   ├── Main.sln
│   │   └── MainLib/MainClass.cs
│   └── sub-with-sln/               # Submodule inside a worktree
│       └── ...
│
└── sub-wt/                         # Worktree of the submodule
    ├── .git                        # [FILE] -> points to main-repo/.git/modules/.../worktrees/...
    └── src/
        ├── Sub.sln
        └── SubSlnLib/SubSlnClass.cs
```

### Why two different submodules?
- **`sub-with-sln`**: Represents a standalone library with its own build and solution file. When running inside it, `ReGitLint` formats code using `Sub.sln`.
- **`sub-without-sln`**: Represents a project without its own `.sln`, referenced only by the parent repository's `Main.sln`. This verifies `ReGitLint`'s fallback logic to search upwards into parent directories for a solution.

### Why locate `.sln` files inside `src/`?
Developers frequently run tools from nested directories (like `src/`) rather than the root where `.git` is located. This verifies that `ReGitLint` successfully traverses parent directories to resolve the repository root and solution file.

---

## 3. The 5 Test Cases

Each test introduces unformatted C# code to a committed file, executes `regitlint -f modified`, and verifies that formatting is applied correctly and exclusively to the intended repository.

### Test 1: Worktree of Main Repo (run from `src/`)
- **Action**: Runs `regitlint -f modified` from `main-wt/src`.
- **Verification**:
  - `MainLib/MainClass.cs` inside `main-wt` is formatted.
  - The parent repository `main-repo` remains untouched.

### Test 2: Submodule with its own `.sln` (run from `src/`)
- **Action**: Runs `regitlint -f modified` from `main-repo/sub-with-sln/src`.
- **Verification**:
  - `SubSlnLib/SubSlnClass.cs` is formatted.
  - Other unformatted files in the submodule (`SubExtraUnformatted.cs`) remain unformatted, verifying that only modified files are touched.
  - The parent repository `main-repo` remains untouched.

### Test 3: Submodule without a `.sln` (run from submodule root)
- **Action**: Runs `regitlint -f modified` from `main-repo/sub-without-sln`.
- **Verification**:
  - `SubNoSlnLib/SubNoSlnClass.cs` is formatted using the parent solution fallback.
  - Parent repository uncommitted files remain untouched.

### Test 4: Submodule inside a Worktree (run from `src/`)
- **Action**: Runs `regitlint -f modified` from `main-wt/sub-with-sln/src`.
- **Verification**:
  - The compound pointer path (combining `worktrees` and `modules`) is resolved properly and `SubSlnLib/SubSlnClass.cs` is formatted.

### Test 5: Worktree of a Submodule (run from `src/`)
- **Action**: Runs `regitlint -f modified` from `sub-wt/src`.
- **Verification**:
  - A worktree created directly out of a submodule is recognized and formatted correctly.

---

## 4. Guarantees Verified

1. `GetGitDirectory()` correctly detects both `.git` directories and `.git` pointer files (worktrees and submodules).
2. Directory traversal upwards from nested subdirectories (`src/`) resolves the repository root reliably.
3. Formatting remains isolated to the targeted working tree / submodule without cross-repository contamination.
4. Both standalone solutions and parent-solution fallbacks function properly in worktree/submodule topologies.
