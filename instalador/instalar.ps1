<#
.SINOPSIS
    Instala las herramientas dsdsaaggr para Revit 2025.

.DESCRIPCION
    Hace exactamente lo mismo que dsdsaaggr.exe, pero como script.

    POR QUE EXISTE ESTE ARCHIVO

    Windows trata muy distinto a un .exe y a un .ps1:

      * Un .exe sin firma digital le dispara a SmartScreen el cartel "Windows protegio su
        PC", y si el equipo tiene activado el Control inteligente de aplicaciones lo bloquea
        de una y no deja ni elegir.
      * Un .ps1 no pasa por ese filtro. Se ejecuta y listo.

    Sacar ese aviso del .exe no se arregla compilando: hace falta comprarle una firma a una
    autoridad certificadora. Mientras tanto, este script es la via que funciona en todas las
    maquinas, sin permisos de administrador y sin tocar configuraciones de seguridad.

    Todo va a la carpeta de complementos DEL USUARIO:
        %APPDATA%\Autodesk\Revit\Addins\2025

.PARAMETER Desinstalar
    Quita los complementos en vez de instalarlos.

.PARAMETER Origen
    Carpeta con los archivos a copiar. Por defecto, la subcarpeta 'addins' que viene al lado
    de este script.

.EJEMPLO
    powershell -NoProfile -ExecutionPolicy Bypass -File .\instalar.ps1

.EJEMPLO
    powershell -NoProfile -ExecutionPolicy Bypass -File .\instalar.ps1 -Desinstalar
#>

[CmdletBinding()]
param(
    [switch] $Desinstalar,
    [string] $Origen
)

$ErrorActionPreference = 'Stop'

$VERSION_REVIT = '2025'
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Origen) { $Origen = Join-Path $aqui 'addins' }

$destino = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\" + $VERSION_REVIT)
$registro = Join-Path $aqui 'instalar.log'

# Restos de nombres anteriores: se borran para no terminar con botones duplicados.
$viejos = @(
    'RigaPublicador.dll', 'RigaPublicador.deps.json',
    'RigaPublicador.runtimeconfig.json', 'RigaPublicador.addin',
    'RigaSectores.dll', 'RigaSectores.deps.json',
    'RigaSectores.runtimeconfig.json', 'RigaSectores.addin',
    'InstalarHerramientas.log'
)

function Anotar([string] $texto) {
    Write-Output $texto
    try { Add-Content -Path $registro -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $texto) -Encoding utf8 } catch { }
}

function RevitAbierto {
    $p = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    return ($null -ne $p)
}

Anotar ''
Anotar ('=== dsdsaaggr para Revit ' + $VERSION_REVIT + ' ===')
Anotar ('destino: ' + $destino)

if (RevitAbierto) {
    Anotar ''
    Anotar 'Revit esta abierto. Cerralo y volve a correr esto.'
    Anotar 'Windows no deja reemplazar una DLL que Revit tiene tomada.'
    exit 1
}

# ---------------- desinstalar ----------------

if ($Desinstalar) {
    $n = 0
    if (Test-Path $Origen) {
        foreach ($f in Get-ChildItem $Origen -File) {
            $t = Join-Path $destino $f.Name
            if (Test-Path $t) { Remove-Item $t -Force; $n++ }
        }
    }
    foreach ($v in $viejos) {
        $t = Join-Path $destino $v
        if (Test-Path $t) { Remove-Item $t -Force; $n++ }
    }
    Anotar ''
    if ($n -gt 0) { Anotar ("Se quitaron $n archivos.") } else { Anotar 'No habia nada que quitar.' }
    exit 0
}

# ---------------- instalar ----------------

if (-not (Test-Path $Origen)) {
    Anotar ''
    Anotar ("No encuentro la carpeta con los complementos: " + $Origen)
    Anotar "Tiene que estar la subcarpeta 'addins' al lado de este script."
    exit 1
}

$archivos = @(Get-ChildItem $Origen -File)
if ($archivos.Count -eq 0) {
    Anotar ''
    Anotar ('La carpeta ' + $Origen + ' esta vacia.')
    exit 1
}

try {
    if (-not (Test-Path $destino)) { New-Item -ItemType Directory -Path $destino -Force | Out-Null }

    $quitados = 0
    foreach ($v in $viejos) {
        $t = Join-Path $destino $v
        if (Test-Path $t) { Remove-Item $t -Force; $quitados++ }
    }
    if ($quitados -gt 0) { Anotar ("Se quitaron $quitados archivos de versiones anteriores.") }

    foreach ($f in $archivos) {
        Copy-Item $f.FullName (Join-Path $destino $f.Name) -Force
        Anotar ('  copiado  ' + $f.Name)
    }

    Anotar ''
    Anotar ('Listo: ' + $archivos.Count + ' archivos en ' + $destino)
    Anotar 'Abri Revit y busca la pestana dsdsaaggr.'
    exit 0
}
catch [System.UnauthorizedAccessException] {
    Anotar ''
    Anotar ('ERROR de permisos: ' + $_.Exception.Message)
    Anotar 'Revisa que la carpeta no este bloqueada por el antivirus o por la sincronizacion.'
    exit 1
}
catch {
    Anotar ''
    Anotar ('ERROR: ' + $_.Exception.GetType().Name + ': ' + $_.Exception.Message)
    exit 1
}
