<#
  Registra la Mesa de Ayuda como servicio de Windows (inicio automático) y lo inicia.
  Ejecutar en PowerShell como administrador, en el servidor:

    .\instalar-servicio.ps1 -Carpeta C:\HelpDeskAsse
#>
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)][string]$Carpeta,
    [string]$Nombre = "HelpDeskAsse"
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $Carpeta "HelpDesk.Api.exe"
if (-not (Test-Path $exe)) {
    throw "No se encontró $exe. Ejecutá primero publicar.ps1 y copiá la carpeta 'publicado' a $Carpeta."
}

$existente = Get-Service -Name $Nombre -ErrorAction SilentlyContinue
if ($existente) {
    Write-Host "El servicio $Nombre ya existe: se reemplaza."
    if ($existente.Status -ne "Stopped") { Stop-Service -Name $Nombre -Force }
    sc.exe delete $Nombre | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $Nombre `
    -BinaryPathName "`"$exe`"" `
    -DisplayName "Mesa de Ayuda ASSE - IntegradoC" `
    -Description "Registro de solicitudes de la mesa de ayuda de IntegradoC" `
    -StartupType Automatic | Out-Null

Start-Service -Name $Nombre
Write-Host "Servicio $Nombre instalado e iniciado."
Write-Host "Probá en este servidor: http://localhost:5080"
Write-Host "La contraseña inicial de 'admin' está en $Carpeta\admin-password-inicial.txt (borralo después de entrar)."
