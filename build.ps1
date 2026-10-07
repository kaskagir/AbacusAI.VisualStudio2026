$ErrorActionPreference = "Stop"
dotnet restore
dotnet build -c Release
Write-Host ""
Write-Host "VSIX output:"
Get-ChildItem .\bin\Release\*.vsix -ErrorAction SilentlyContinue | Select-Object FullName
