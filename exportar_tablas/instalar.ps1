# Compila e instala el addin para el usuario actual (no requiere admin).
# Cierra Revit 2025 antes de ejecutar: si esta abierto, la DLL queda bloqueada.

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$destino = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025'

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    throw 'Revit esta abierto. Cerralo y volve a ejecutar.'
}

Push-Location $raiz
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion.' }
} finally {
    Pop-Location
}

if (-not (Test-Path $destino)) { New-Item -ItemType Directory -Force $destino | Out-Null }

$bin = Join-Path $raiz 'bin\Release'
foreach ($f in 'ExportarTablas.dll', 'ExportarTablas.deps.json', 'ExportarTablas.runtimeconfig.json', 'ExportarTablas.addin') {
    Copy-Item (Join-Path $bin $f) $destino -Force
}

Write-Output "Instalado en: $destino"
Write-Output 'Abri Revit 2025: el panel "Exportar" de la pestana RIGA suma el boton de tablas.'
