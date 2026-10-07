# Mesa de Ayuda ASSE – IntegradoC

Aplicación web interna para registrar y seguir las solicitudes de soporte de IntegradoC.
Reemplaza al Excel compartido de la mesa de ayuda **sin cambiar la forma de trabajar**: los mismos
12 campos y los mismos valores, pero con número y fecha automáticos, historial de cambios, búsqueda
de casos anteriores, pendientes, estadísticas y exportación a Excel.

- **Backend:** C# / ASP.NET Core 10 (Minimal APIs + Dapper)
- **Base de datos:** SQL Server 2017 o superior (también Express o LocalDB)
- **Interfaz:** React + TypeScript, ya compilada dentro de `src/HelpDesk.Api/wwwroot`
- **Sin servicios externos:** no usa CDN, fuentes de internet ni telemetría.

## Puesta en marcha rápida (PC con Windows)

1. Instalar el **SDK de .NET 10** (https://dotnet.microsoft.com/download) y tener un **SQL Server**
   (puede ser SQL Server Express o LocalDB).
2. Revisar la cadena de conexión en `src/HelpDesk.Api/appsettings.json`
   (`ConnectionStrings:HelpDesk`). Por defecto usa `Server=localhost` con autenticación de Windows.
3. Ejecutar `iniciar.bat` (o, en una consola, `cd src/HelpDesk.Api` y `dotnet run`).
   También se puede abrir `HelpDeskAsse.sln` con Visual Studio 2022 y ejecutar con F5.
   La primera vez crea la base `HelpDeskAsse`, sus tablas y los catálogos iniciales.
4. Abrir **http://localhost:5080** e ingresar con el usuario `admin`. La contraseña inicial
   aparece en la consola y en `src/HelpDesk.Api/admin-password-inicial.txt`. Se pide cambiarla al entrar.
5. Ir a **Configuración → Importar Excel**, cargar el registro histórico y confirmar.
6. Crear los usuarios de la mesa en **Configuración → Usuarios** (vinculando cada uno a su responsable).

Instrucciones completas (servidor, servicio de Windows, IIS, HTTPS, respaldos y problemas comunes):
[`docs/INSTALACION.md`](docs/INSTALACION.md). Diseño, modelo de datos y flujo: [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md).

## Uso diario

| Acción | Cómo |
|---|---|
| Registrar una llamada | Tecla **N** (desde cualquier pantalla) o botón *Nueva solicitud*. **Ctrl+Enter** guarda; **Ctrl+Shift+Enter** guarda y abre otra. |
| Buscar | Tecla **/** y escribir. Busca en número, funcionario, oficina, descripción, observaciones y responsable, sin importar tildes ni mayúsculas. `#2366` busca ese número exacto. |
| Ver o editar | Clic en la fila. Todos los campos se editan desde el panel lateral; cada cambio queda en el *Historial*. |
| Cambiar estado o responsable | Clic sobre el estado o el responsable en la tabla. Aparece un aviso con *Deshacer*. |
| Resolver | Botón *Resolver* en las filas pendientes o en el detalle. Guarda la fecha de resolución y el tiempo total. |
| Casos anteriores | Al escribir la descripción aparecen los casos parecidos y la solución que se anotó. *Copiar a observaciones* la trae al formulario. |
| Exportar | *Exportar* en la tabla: Excel o CSV con los filtros activos. |

## Estructura

```
database/               Scripts T-SQL (esquema y datos iniciales). Se aplican solos al iniciar.
src/HelpDesk.Api/       Backend ASP.NET Core + interfaz compilada (wwwroot)
src/HelpDesk.Web/       Código fuente de la interfaz (React + TypeScript)
docs/                   Arquitectura e instalación
HelpDeskAsse.sln        Solución para Visual Studio
iniciar.bat             Arranque rápido para probar en una PC
publicar.ps1            Genera la versión para instalar en un servidor
instalar-servicio.ps1   Registra la aplicación como servicio de Windows
```

## Modificar la interfaz

La interfaz ya viene compilada. Solo hace falta si se cambia algo en `src/HelpDesk.Web`:

```
cd src/HelpDesk.Web
npm install
npm run build      # deja el resultado en src/HelpDesk.Api/wwwroot
```

Requiere Node.js 20 o superior (solo para compilar; el servidor no lo necesita).
