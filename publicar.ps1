<#
  Genera la versión para instalar en un servidor en la carpeta "publicado".
  Por defecto incluye el runtime de .NET (el servidor no necesita tener .NET instalado).

  Uso:
    .\publicar.ps1
    .\publicar.ps1 -DependienteDelFramework   # más liviano; el servidor necesita el runtime de ASP.NET Core
#>
param(
    [string]$Salida = "publicado",
    [string]$Runtime = "win-x64",
    [switch]$DependienteDelFramework
)

$ErrorActionPreference = "Stop"
$proyecto = Join-Path $PSScriptRoot "src\HelpDesk.Api\HelpDesk.Api.csproj"
$destino = Join-Path $PSScriptRoot $Salida
$autocontenido = if ($DependienteDelFramework) { "false" } else { "true" }

Write-Host "Publicando en $destino ..."
dotnet publish $proyecto -c Release -r $Runtime --self-contained $autocontenido -o $destino
if ($LASTEXITCODE -ne 0) { throw "La publicación falló. Revisá los mensajes de arriba." }

Write-Host ""
Write-Host "Listo. Copiá la carpeta '$Salida' al servidor, revisá appsettings.json"
Write-Host "y ejecutá instalar-servicio.ps1 como administrador."
