# Abacus AI for Visual Studio 2026

Native Visual Studio 2026 VSIX integration around the official Abacus AI CLI.

## Download

**[AbacusAI-Setup.exe herunterladen](Installer/dist/AbacusAI-Setup.exe)** (Version 1.1.0)

Der Installer erkennt Visual Studio 2022 und 2026, installiert die Erweiterung und richtet bei Bedarf die Abacus-AI-CLI ein.

1. Alle Visual-Studio-Fenster schließen.
2. `AbacusAI-Setup.exe` ausführen. Falls Windows SmartScreen warnt: "Weitere Informationen" und "Trotzdem ausführen" wählen (die Datei ist nicht signiert).
3. Visual Studio starten, `Ansicht -> Weitere Fenster -> Abacus AI` öffnen und auf "Login" klicken.

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
- **Datei-Anhänge-Unterstützung:**
  - Drag & Drop von beliebigen Dateien in die Eingabezeile
  - Ctrl+V zum Einfügen von Screenshots aus der Zwischenablage
  - Unterstützte Bildformate: PNG, JPG, JPEG, GIF, BMP, WebP
  - Alle Dateitypen können angehängt werden (Code, Logs, Konfigurationen, etc.)
  - Dateiinhalte werden automatisch in den Prompt eingebettet
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

## Dateien und Screenshots verwenden

Sie können beliebige Dateien und Screenshots direkt in der Eingabezeile des Abacus AI Tool Windows anhängen:

### Methoden zum Anhängen von Dateien:

1. **@filepath-Syntax:** Geben Sie `@` gefolgt vom Dateipfad ein (z.B. `@file.cs` oder `@C:\path\to\file.cs`)
2. **Drag & Drop:** Ziehen Sie eine oder mehrere Dateien direkt in die Eingabezeile
3. **Ctrl+V:** Kopieren Sie einen Screenshot in die Zwischenablage (z.B. mit Windows+Shift+S) und drücken Sie Ctrl+V in der Eingabezeile

### @filepath-Syntax

Die `@filepath`-Syntax ermöglicht es Ihnen, Dateien direkt in der Eingabezeile zu referenzieren:

**Beispiele:**

```
Erkläre diese Datei: @Program.cs
Debugge diese Fehler: @error.log und @config.json
Überprüfe alle C#-Dateien: @*.cs
Analysiere die Logs: @./logs/*.txt
```

**Unterstützte Pfad-Formate:**

- **Relative Pfade:** `@file.cs`, `@./folder/file.cs`, `@../other/file.cs`
- **Absolute Pfade:** `@C:\Users\Name\project\file.cs`
- **Wildcards:** `@*.cs`, `@src/**/*.ts`, `@logs/*.log`

**Verhalten:**

- Die `@filepath`-Referenzen werden automatisch erkannt und die Dateien angehängt
- Die Referenzen werden aus der Nachricht entfernt (nur die Frage wird angezeigt)
- Die Dateien erscheinen in der Anhänge-Liste über der Eingabezeile
- Sie können angehängte Dateien mit dem ✕-Button entfernen, bevor Sie die Nachricht senden
- Fehlerhafte Pfade werden mit einer Warnung angezeigt

**Limits:**

- Maximale Dateigröße: 10 MB (konfigurierbar in Tools > Options > Abacus AI)
- Maximale Dateien bei Wildcards: 50 (konfigurierbar in Tools > Options > Abacus AI)

### Unterstützte Dateitypen:

- **Bilder:** PNG, JPG, JPEG, GIF, BMP, WebP
- **Code-Dateien:** .cs, .js, .py, .java, .cpp, .html, .css, etc.
- **Konfigurationen:** .json, .xml, .yaml, .config, etc.
- **Logs:** .log, .txt, etc.
- **Alle anderen Dateitypen:** Die Inhalte werden als Text in den Prompt eingebettet

### Verwendung:

1. Geben Sie Ihre Frage mit `@filepath`-Referenzen ein, oder ziehen Sie Dateien per Drag & Drop, oder drücken Sie Ctrl+V
2. Die Dateien erscheinen in einer Liste über der Eingabezeile
3. Sie können Dateien mit dem ✕-Button entfernen
4. Senden Sie die Nachricht
5. Die Dateiinhalte werden automatisch an Abacus übermittelt

Abacus kann dann die Dateien analysieren und Ihnen helfen, z.B. Code zu debuggen, Fehler zu erklären oder Konfigurationen zu überprüfen.

## Security

The extension launches `abacusai.exe` with the solution directory as its working directory. Abacus itself controls permissions for edits/commands through its normal CLI configuration. Start with normal permission mode.

A project-root `AGENTS.md` is recommended for project conventions and safety rules.
