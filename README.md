# Mesa de Ayuda ASSE – IntegradoC

Aplicación web interna para registrar y seguir las solicitudes de soporte de IntegradoC.
Reemplaza al Excel compartido de la mesa de ayuda **sin cambiar la forma de trabajar**: los mismos
12 campos y los mismos valores, pero con número y fecha automáticos, historial de cambios, búsqueda
de casos anteriores, pendientes, estadísticas y exportación a Excel.

- **Backend:** C# / ASP.NET Core 10 (Minimal APIs + Dapper + Npgsql)
- **Base de datos:** PostgreSQL 14 o superior (en Docker, PostgreSQL 17)
- **Interfaz:** React + TypeScript (esbuild); en Docker la sirve nginx. También queda compilada
  dentro de `src/HelpDesk.Api/wwwroot` para usarla sin Docker.
- **Sin servicios externos:** no usa CDN, fuentes de internet ni telemetría.

## Puesta en marcha con Docker (recomendado)

```
cp .env.example .env        # opcional: cambiar POSTGRES_PASSWORD y ADMIN_PASSWORD_INICIAL
docker compose up --build
```

Levanta tres contenedores: `frontend` (nginx, http://localhost:8080) → `backend` (ASP.NET Core) →
`postgres` (PostgreSQL 17, datos en el volumen `postgres-data`). La primera vez se crean la base, las
tablas y los catálogos iniciales, y el usuario `admin` (contraseña `ADMIN_PASSWORD_INICIAL`, o la que
aparece en `docker compose logs backend` si se dejó vacía).

Luego: **Configuración → Importar Excel** para cargar el registro histórico y **Configuración → Usuarios**
para crear los usuarios de la mesa. Detalles, comandos y respaldos: [`docs/INSTALACION.md`](docs/INSTALACION.md).

## Puesta en marcha sin Docker (PC con Windows)

1. Instalar el **SDK de .NET 10** (https://dotnet.microsoft.com/download) y **PostgreSQL**.
2. Revisar la cadena de conexión en `src/HelpDesk.Api/appsettings.json`
   (`ConnectionStrings:HelpDesk`). Por defecto: `Host=localhost;Port=5432;Database=helpdeskasse;Username=helpdesk;Password=helpdesk`.
3. Ejecutar `iniciar.bat` (o, en una consola, `cd src/HelpDesk.Api` y `dotnet run`).
   También se puede abrir `HelpDeskAsse.sln` con Visual Studio 2022 y ejecutar con F5.
   La primera vez crea la base `helpdeskasse`, sus tablas y los catálogos iniciales.
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
database/               Scripts de PostgreSQL (esquema y datos iniciales). Se aplican solos.
src/HelpDesk.Api/       Backend ASP.NET Core + interfaz compilada (wwwroot) + Dockerfile
src/HelpDesk.Web/       Código fuente de la interfaz (React + TypeScript) + Dockerfile y nginx.conf
docker-compose.yml      Servicios frontend, backend y postgres
.env.example            Variables de entorno de Docker Compose (copiar como .env)
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
