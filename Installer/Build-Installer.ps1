# Erzeugt Installer\dist\AbacusAI-Setup.exe (selbstextrahierend, per IExpress - in Windows enthalten).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build -c Release --no-incremental -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen" }

$vsix = Get-ChildItem "$root\bin\Release" -Recurse -Filter *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $vsix) { throw "Keine Release-.vsix gefunden" }

$dist = Join-Path $PSScriptRoot 'dist'
$work = Join-Path $dist 'work'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $work -ItemType Directory -Force | Out-Null
Copy-Item $vsix.FullName "$work\AbacusAI.VisualStudio2026.vsix"
Copy-Item "$PSScriptRoot\Install.ps1" "$work\Install.ps1"

$target = Join-Path $dist 'AbacusAI-Setup.exe'
Remove-Item $target -Force -ErrorAction SilentlyContinue

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$target
FriendlyName=Abacus AI for Visual Studio 2026 - Setup
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="Install.ps1"
FILE1="AbacusAI.VisualStudio2026.vsix"
[SourceFiles]
SourceFiles0=$work\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@
$sedFile = Join-Path $dist 'setup.sed'
Set-Content -Path $sedFile -Value $sed -Encoding ASCII

# IExpress verträgt keine Anführungszeichen im Argument -> Kurzpfad (8.3) verwenden.
$sedArg = (New-Object -ComObject Scripting.FileSystemObject).GetFile($sedFile).ShortPath
Start-Process iexpress.exe -ArgumentList '/N', $sedArg | Out-Null
# IExpress startet einen Folgeprozess, daher auf die fertige Datei warten.
$deadline = (Get-Date).AddSeconds(90)
while (-not (Test-Path $target) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (-not (Test-Path $target)) { throw "IExpress hat keine Setup-Datei erzeugt" }
while ((Get-Process iexpress, wextract -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Remove-Item $work -Recurse -Force
Get-Item $target | Select-Object FullName, Length
