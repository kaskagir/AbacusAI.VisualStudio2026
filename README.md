# Abacus AI for Visual Studio 2026

Native Visual Studio 2026 VSIX integration around the official Abacus AI CLI.

## Features

- `View -> Other Windows -> Abacus AI` tool window
- Editor context menu:
  - Ask Abacus AI
  - Abacus: Explain Selection
  - Abacus: Fix Selection
- Automatically passes:
  - solution directory as working directory
  - current file
  - selected source code
- Uses `abacusai -p "..."` and therefore the official non-interactive CLI mode.
- No Abacus API key is stored by this extension.

## Prerequisites

1. Visual Studio 2026.
2. Workload: Visual Studio extension development.
3. .NET Framework 4.7.2 targeting pack.
4. Node.js 18+.
5. Abacus CLI installed and logged in.

Install Abacus CLI in PowerShell:

```powershell
irm https://static.abacus.ai/cli/install.ps1 | iex
```

Then:

```powershell
abacusai
```

and log in with:

```text
/login
```

Verify:

```powershell
abacusai --help
```

## Build

Open `AbacusAI.VisualStudio2026.csproj` in Visual Studio 2026 and Build.

The VSIX is produced under:

```text
bin\Debug\
```

or

```text
bin\Release\
```

Install the generated `.vsix` by double-clicking it.

## Important

The project deliberately calls the official CLI rather than an undocumented Abacus HTTP endpoint. The CLI supports `-p` / `--print`, permission modes, workspace settings and AGENTS.md.

## First test

Open a C# project, select some code and right-click:

`Abacus: Explain Selection`

Then open:

`View -> Other Windows -> Abacus AI`

and use the prompt box.

## Security

The extension launches `abacusai.exe` with the solution directory as its working directory. Abacus itself controls permissions for edits/commands through its normal CLI configuration. Start with normal permission mode.

A project-root `AGENTS.md` is recommended for project conventions and safety rules.
