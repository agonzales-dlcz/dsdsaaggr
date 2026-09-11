# Compila los addins y arma las DOS formas de entrega.
# El orden importa: el instalador incrusta los binarios de los addins.
#
# QUE SALE DE ACA, EN instalador\publicado:
#
#   dsdsaaggr.exe      un solo archivo, doble clic. Comodo, pero sin firma digital le
#                      dispara el aviso de SmartScreen, y si el equipo tiene activado el
#                      Control inteligente de aplicaciones lo bloquea sin dar opcion.
#
#   instalar.ps1       + la carpeta addins\. Hace lo mismo y NO pasa por ese filtro, asi
#                      que funciona en cualquier maquina. Es la via recomendada para
#                      repartir mientras el exe no este firmado.
#
# FIRMAR EL EXE
#
# Si tenes un certificado de firma de codigo, pasale el .pfx:
#
#     .\compilar-todo.ps1 -Pfx C:\ruta\certificado.pfx -Clave 'la contrasena'
#
# Sin eso el exe sale sin firmar y el script lo avisa al final. Ver LEEME.md.

[CmdletBinding()]
param(
    [string] $Pfx,
    [string] $Clave,
    [string] $Sellador = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path

$proyectos = @('sectores', 'vistas', 'guardado', 'exportar_planos', 'exportar_tablas')

foreach ($p in $proyectos) {
    Write-Output "== compilando $p"
    dotnet build (Join-Path $raiz $p) -c Release --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "fallo la compilacion de $p" }
}

Write-Output '== armando el instalador'
$salida = Join-Path $raiz 'instalador\publicado'
dotnet publish (Join-Path $raiz 'instalador') -c Release -o $salida --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'fallo el instalador' }

$exe = Join-Path $salida 'dsdsaaggr.exe'
if (-not (Test-Path $exe)) { throw "no se genero $exe" }

# Restos del nombre anterior, para que la carpeta de entrega no confunda.
foreach ($v in 'InstalarHerramientas.exe', 'InstalarHerramientas.pdb', 'dsdsaaggr.pdb') {
    $t = Join-Path $salida $v
    if (Test-Path $t) { Remove-Item $t -Force }
}

# ---------------- la via sin exe ----------------

Write-Output '== armando la via de PowerShell'
$addins = Join-Path $salida 'addins'
if (Test-Path $addins) { Remove-Item $addins -Recurse -Force }
New-Item -ItemType Directory -Path $addins -Force | Out-Null

$copiados = 0
foreach ($p in $proyectos) {
    $bin = Join-Path $raiz "$p\bin\Release"
    foreach ($ext in '*.dll', '*.deps.json', '*.runtimeconfig.json', '*.addin') {
        foreach ($f in (Get-ChildItem (Join-Path $bin $ext) -ErrorAction SilentlyContinue)) {
            if ($f.Name -like '*.bloqueada.*') { continue }
            Copy-Item $f.FullName (Join-Path $addins $f.Name) -Force
            $copiados++
        }
    }
}
Copy-Item (Join-Path $raiz 'instalador\instalar.ps1') (Join-Path $salida 'instalar.ps1') -Force

# ---------------- firma ----------------

$firmado = $false
if ($Pfx) {
    if (-not (Test-Path $Pfx)) { throw "no existe el certificado: $Pfx" }

    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like '*x64*' } | Select-Object -First 1
    if (-not $signtool) { throw 'no encuentro signtool.exe. Instala el Windows SDK.' }

    $argumentos = @('sign', '/fd', 'SHA256', '/f', $Pfx)
    if ($Clave) { $argumentos += @('/p', $Clave) }
    $argumentos += @('/tr', $Sellador, '/td', 'SHA256', $exe)

    & $signtool.FullName @argumentos | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'fallo la firma' }
    $firmado = $true
}

# ---------------- resumen ----------------

$mb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Output ''
Write-Output "Listo en $salida"
Write-Output ''
Write-Output "  dsdsaaggr.exe           $mb MB, un solo archivo"
if ($firmado) {
    $s = Get-AuthenticodeSignature $exe
    Write-Output ("                          firmado: " + $s.Status)
} else {
    Write-Output '                          SIN FIRMAR: va a saltar el aviso de SmartScreen, y'
    Write-Output '                          donde este activo el Control inteligente de'
    Write-Output '                          aplicaciones no va a poder ejecutarse.'
}
Write-Output ''
Write-Output "  instalar.ps1 + addins\  $copiados archivos. Esta via no dispara ningun aviso."
Write-Output '                          Para repartir: comprimi las dos cosas juntas.'
Write-Output ''
Write-Output '  Instalar con el script:'
Write-Output '    powershell -NoProfile -ExecutionPolicy Bypass -File .\instalar.ps1'
