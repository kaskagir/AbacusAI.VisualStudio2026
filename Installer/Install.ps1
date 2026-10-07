# Installer für "Abacus AI for Visual Studio 2026"
#  1. sucht installierte Visual-Studio-Versionen (2022 / 2026)
#  2. installiert die VSIX-Erweiterung in alle gefundenen Instanzen
#  3. installiert die Abacus-AI-CLI, falls sie fehlt (offizielles Installationsskript des Herstellers)
param(
    [switch]$DryRun,   # nur prüfen, nichts installieren
    [switch]$NoPause   # am Ende nicht auf Tastendruck warten
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Finish([int]$code) {
    if (-not $NoPause) { Write-Host ""; Read-Host "Eingabetaste drücken zum Beenden" | Out-Null }
    exit $code
}
function Step($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "   OK  $t" -ForegroundColor Green }
function Warn($t) { Write-Host "   !!  $t" -ForegroundColor Yellow }
function Fail($t) { Write-Host "   XX  $t" -ForegroundColor Red }

Write-Host "Abacus AI for Visual Studio 2026 - Installation" -ForegroundColor White

# ---------------------------------------------------------------- VSIX suchen
$vsix = Get-ChildItem -Path $here -Filter *.vsix | Select-Object -First 1
if (-not $vsix) { Fail "Keine .vsix-Datei neben dem Installer gefunden."; Finish 1 }

# ---------------------------------------------------------------- Visual Studio
Step "Visual Studio suchen"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    Fail "Visual Studio wurde nicht gefunden (vswhere.exe fehlt)."
    Write-Host "   Bitte zuerst Visual Studio 2022 oder 2026 installieren: https://visualstudio.microsoft.com/"
    Finish 1
}
$json = & $vswhere -all -prerelease -products * -requires Microsoft.VisualStudio.Component.CoreEditor -version '[17.0,19.0)' -format json
$instances = @($json | ConvertFrom-Json) | Where-Object { Test-Path (Join-Path $_.installationPath 'Common7\IDE\VSIXInstaller.exe') }
if ($instances.Count -eq 0) {
    Fail "Keine passende Visual-Studio-Installation (2022 oder 2026, 64-Bit) gefunden."
    Finish 1
}
foreach ($i in $instances) { Ok ("{0}  (Version {1})" -f $i.displayName, $i.installationVersion) }

if (Get-Process devenv -ErrorAction SilentlyContinue) {
    if ($DryRun) { Warn "Visual Studio läuft (für die echte Installation muss es geschlossen sein)." }
    else {
        Fail "Visual Studio läuft noch. Bitte alle Visual-Studio-Fenster schließen und den Installer erneut starten."
        Finish 1
    }
}

# ---------------------------------------------------------------- VSIX installieren
Step "Erweiterung installieren ($($vsix.Name))"
$failed = $false
foreach ($i in $instances) {
    $name = $i.displayName
    if ($DryRun) { Warn "Testlauf: würde in '$name' installieren"; continue }
    $installer = Join-Path $i.installationPath 'Common7\IDE\VSIXInstaller.exe'
    $log = Join-Path $env:TEMP ("AbacusAI-vsix-{0}.log" -f $i.instanceId)
    $p = Start-Process -FilePath $installer `
        -ArgumentList @('/quiet', "/instanceIds:$($i.instanceId)", "/logFile:$log", "`"$($vsix.FullName)`"") `
        -Wait -PassThru
    # 0 = Erfolg, 1001 = bereits installiert
    if ($p.ExitCode -eq 0)        { Ok "In '$name' installiert" }
    elseif ($p.ExitCode -eq 1001) { Ok "In '$name' bereits in dieser Version vorhanden" }
    else { Fail "Installation in '$name' fehlgeschlagen (Code $($p.ExitCode)). Protokoll: $log"; $failed = $true }
}

# ---------------------------------------------------------------- Abacus CLI
Step "Abacus-AI-CLI prüfen"
$cliPath = Join-Path $env:USERPROFILE '.abacusai\bin\abacusai.exe'
$cli = Get-Command abacusai.exe -ErrorAction SilentlyContinue
if ($cli -or (Test-Path $cliPath)) {
    Ok "CLI bereits vorhanden: $(if ($cli) { $cli.Source } else { $cliPath })"
} elseif ($DryRun) {
    Warn "Testlauf: CLI fehlt und würde installiert"
} else {
    Write-Host "   CLI fehlt - Installation über das offizielle Skript https://static.abacus.ai/cli/install.ps1"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-Expression (Invoke-RestMethod -Uri 'https://static.abacus.ai/cli/install.ps1' -UseBasicParsing)
        if (Test-Path $cliPath) { Ok "CLI installiert: $cliPath" }
        else { Warn "Das Installationsskript lief durch, abacusai.exe wurde aber nicht am erwarteten Ort gefunden." }
    } catch {
        Fail "CLI-Installation fehlgeschlagen: $($_.Exception.Message)"
        Write-Host "   Manuell: powershell -c `"irm https://static.abacus.ai/cli/install.ps1 | iex`""
        $failed = $true
    }
}

# ---------------------------------------------------------------- Ende
Step "Fertig"
if ($failed) { Fail "Die Installation wurde nicht vollständig abgeschlossen (siehe oben)."; Finish 1 }
Write-Host "   Nächste Schritte:"
Write-Host "   1. Visual Studio starten, Fenster 'Abacus AI' öffnen."
Write-Host "   2. Auf 'Login' klicken und bei Abacus AI anmelden."
Finish 0
