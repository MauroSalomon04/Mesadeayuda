# Instalación

Hay tres formas de usar la aplicación:

- **Con Docker** (sección 0, recomendada): `docker compose up --build` levanta PostgreSQL, el backend
  y la interfaz, cada uno en su contenedor.
- **Probarla en una PC sin Docker** (sección 1): se ejecuta con `dotnet run` contra un PostgreSQL local.
- **Instalarla para toda la mesa sin Docker** (sección 2): se publica en un servidor de la red interna
  de ASSE y queda corriendo como servicio de Windows (o en IIS).

---

## 0. Con Docker (recomendado)

### Requisitos

- Docker Desktop (Windows/macOS) o Docker Engine + plugin Compose (Linux).
- Acceso a internet la primera vez (descarga las imágenes `postgres:17`, `node:22-alpine`,
  `nginx:1.27-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0` y `aspnet:10.0`, y los paquetes NuGet y npm).

### Servicios

| Servicio | Imagen / Dockerfile | Puerto | Función |
|---|---|---|---|
| `frontend` | `src/HelpDesk.Web/Dockerfile` (nginx) | `8080` → 80 | Sirve la interfaz y reenvía `/api/` al backend |
| `backend` | `src/HelpDesk.Api/Dockerfile` (ASP.NET Core 10) | `127.0.0.1:5080` → 5080 | API; se conecta a `postgres:5432` |
| `postgres` | `postgres:17` | `127.0.0.1:5432` → 5432 | Base `helpdeskasse` (volumen `postgres-data`) |

Los contenedores se comunican por la red interna `helpdesk` usando los nombres de servicio
(`backend`, `postgres`); no se usa `localhost` entre contenedores.

### Pasos

1. (Opcional, recomendado) Copiar `.env.example` a `.env` y cambiar al menos `POSTGRES_PASSWORD` y
   `ADMIN_PASSWORD_INICIAL`. Sin `.env` se usan valores por defecto que sirven solo para probar.
2. En la carpeta del proyecto:

   ```
   docker compose up --build -d
   ```

   La primera vez PostgreSQL crea la base y ejecuta `database/001_esquema.sql` y
   `database/002_datos_iniciales.sql` (carpeta `/docker-entrypoint-initdb.d`). Después arranca el
   backend, que verifica el esquema (tabla `VersionEsquema`) y crea el usuario **admin**.
3. Abrir **http://localhost:8080** y entrar como `admin` con `ADMIN_PASSWORD_INICIAL`. Si se dejó vacía,
   la contraseña generada aparece en `docker compose logs backend`.

### Comandos útiles

```
docker compose ps                    # estado y healthcheck de los tres servicios
docker compose logs -f backend       # registro del backend
docker compose restart backend       # reiniciar un servicio
docker compose down                  # detener (los datos se conservan en el volumen)
docker compose down -v               # detener y BORRAR la base (vuelve a crearse al levantar)
docker compose exec postgres psql -U helpdesk -d helpdeskasse
```

### Respaldo y restauración con Docker

```
docker compose exec -T postgres pg_dump -U helpdesk -d helpdeskasse -Fc > helpdesk.dump
docker compose exec -T postgres pg_restore -U helpdesk -d helpdeskasse --clean --if-exists < helpdesk.dump
```

---

## 1. Probar en una PC (sin Docker)

### Requisitos

| Componente | Versión | Notas |
|---|---|---|
| SDK de .NET | 10 | `dotnet --list-sdks` debe mostrar una versión 10.x. Con .NET 8 también funciona: ver *Usar .NET 8* más abajo. |
| PostgreSQL | 14 o superior | Con la extensión `unaccent` (viene incluida en los instaladores oficiales). |
| Navegador | Edge, Chrome o Firefox actuales | |

La primera compilación descarga tres paquetes de NuGet (Dapper, Npgsql y
Microsoft.Extensions.Hosting.WindowsServices), así que esa PC necesita acceso a `api.nuget.org`.

### Pasos

1. **Crear el usuario de PostgreSQL** (por ejemplo con `psql` como `postgres`):

   ```sql
   CREATE ROLE helpdesk LOGIN PASSWORD 'helpdesk' CREATEDB;
   ```

2. **Revisar la cadena de conexión** en `src/HelpDesk.Api/appsettings.json`
   (`ConnectionStrings:HelpDesk`). Por defecto:

   ```
   Host=localhost;Port=5432;Database=helpdeskasse;Username=helpdesk;Password=helpdesk
   ```

3. **Ejecutar** `iniciar.bat` (doble clic) o, desde una consola:

   ```
   cd src/HelpDesk.Api
   dotnet run
   ```

   La primera vez:
   - crea la base `helpdeskasse` en UTF-8 (si el usuario tiene permiso `CREATEDB`);
   - crea las tablas, la función `normalizar` (búsquedas sin tildes) y carga los catálogos del Excel;
   - crea el usuario **admin** con una contraseña al azar, que se muestra en la consola y se guarda en
     `src/HelpDesk.Api/admin-password-inicial.txt`. Borrá ese archivo después de entrar.

4. **Abrir** http://localhost:5080, entrar como `admin` y elegir una contraseña nueva.

5. **Importar el Excel**: *Configuración → Importar Excel*. Antes de guardar muestra el resumen
   (registros encontrados, nuevos, ya existentes, con error y advertencias). Se puede importar el mismo
   archivo varias veces: las solicitudes cuyo ID ya existe se omiten.

6. **Crear usuarios**: *Configuración → Usuarios*. Conviene vincular cada usuario con su responsable
   (Agustín, Facundo, Mauro) para que funcione *Mis solicitudes* y se complete solo el responsable al registrar.

Para detener la aplicación: `Ctrl+C` en la consola.

---

## 2. Instalar para toda la mesa sin Docker (servidor interno)

### 2.1 Base de datos

Lo recomendable es pedirle al DBA una base en el PostgreSQL de ASSE:

```sql
CREATE ROLE helpdesk LOGIN PASSWORD '...';
CREATE DATABASE helpdeskasse OWNER helpdesk ENCODING 'UTF8' TEMPLATE template0;
```

El dueño de la base puede crear la extensión `unaccent` (es una extensión *trusted* desde PostgreSQL 13).

Si el DBA prefiere crear las tablas él mismo, puede ejecutar en orden los scripts de `database/`:

```
psql -U helpdesk -d helpdeskasse -v ON_ERROR_STOP=1 -f database/001_esquema.sql
psql -U helpdesk -d helpdeskasse -v ON_ERROR_STOP=1 -f database/002_datos_iniciales.sql
```

Cada script se registra solo en la tabla `VersionEsquema`, así que la aplicación no los vuelve a
aplicar. Conviene poner `"CrearBaseSiNoExiste": false` en `appsettings.json`.

### 2.2 Publicar

En la PC donde está el código (con el SDK de .NET):

```powershell
.\publicar.ps1
```

Genera la carpeta `publicado\` con todo lo necesario. Por defecto es *independiente* (incluye el
runtime de .NET), así que el servidor no necesita tener .NET instalado.

Copiar la carpeta `publicado\` al servidor (por ejemplo a `C:\HelpDeskAsse`) y editar ahí
`appsettings.json` con la cadena de conexión del servidor.

### 2.3 Ejecutar como servicio de Windows

En el servidor, en una consola de PowerShell **como administrador**:

```powershell
.\instalar-servicio.ps1 -Carpeta C:\HelpDeskAsse
```

Registra el servicio **HelpDeskAsse** con inicio automático y lo arranca.

Abrir el puerto en el firewall de Windows (solo red interna):

```powershell
New-NetFirewallRule -DisplayName "Mesa de Ayuda ASSE" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain
```

Los usuarios entran a `http://NOMBRE-DEL-SERVIDOR:5080`.

### 2.4 Alternativa: IIS

1. Instalar el *ASP.NET Core Hosting Bundle* para .NET 10 en el servidor.
2. Crear un sitio en IIS que apunte a la carpeta `publicado\`, con un *Application Pool* sin código administrado.
3. La identidad del Application Pool necesita permiso de escritura en la carpeta `claves\`.

La aplicación tiene que estar en la raíz del sitio (por ejemplo `http://mesa-ayuda.asse.local/`), no en un subdirectorio.

### 2.5 HTTPS

Dentro de la red interna funciona por HTTP. Para HTTPS hay dos opciones:

- **Con IIS:** agregar un enlace HTTPS al sitio con el certificado de la organización.
- **Sin IIS:** agregar en `appsettings.json` (con el certificado .pfx que provea Infraestructura):

  ```json
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:5443",
        "Certificate": { "Path": "C:\\HelpDeskAsse\\certificado.pfx", "Password": "..." }
      }
    }
  }
  ```

  y quitar la línea `"Urls"`. La cookie de sesión se marca como segura automáticamente al usar HTTPS.

---

## 3. Configuración (`appsettings.json` o variables de entorno)

Cada clave se puede definir también como variable de entorno reemplazando `:` por `__`
(por ejemplo `ConnectionStrings__HelpDesk` o `HelpDesk__PasswordAdminInicial`). Así lo hace `docker-compose.yml`.

| Clave | Para qué sirve | Valor por defecto |
|---|---|---|
| `ConnectionStrings:HelpDesk` | Conexión a PostgreSQL (Npgsql) | `Host=localhost;Port=5432;Database=helpdeskasse;...` |
| `HelpDesk:ZonaHoraria` | Hora con la que se registran las solicitudes | `America/Montevideo` |
| `HelpDesk:AplicarMigracionesAlIniciar` | Crea o actualiza las tablas al arrancar | `true` |
| `HelpDesk:CrearBaseSiNoExiste` | Crea la base si no existe (requiere permiso `CREATEDB`) | `true` (`false` en Docker) |
| `HelpDesk:HorasSesion` | Horas sin actividad hasta que se cierra la sesión | `12` |
| `HelpDesk:PasswordAdminInicial` | Contraseña del primer `admin` (si se deja vacía se genera una) | vacío |
| `HelpDesk:TamanoMaximoImportacionMb` | Tamaño máximo del Excel a importar | `30` |
| `HelpDesk:CarpetaClaves` | Carpeta de las claves que cifran la cookie de sesión | `claves` |
| `HelpDesk:ConfiarEnProxy` | Usar la IP de `X-Forwarded-For` (solo detrás de un proxy de confianza) | `false` (`true` en Docker) |
| `Urls` | Dirección y puerto donde escucha | `http://0.0.0.0:5080` |

---

## 4. Respaldos

Toda la información está en la base `helpdeskasse`. Alcanza con `pg_dump`, por ejemplo:

```
pg_dump -U helpdesk -d helpdeskasse -Fc -f D:\Respaldos\helpdeskasse.dump
```

(o una tarea programada diaria). Con Docker, ver la sección 0. La carpeta `claves\` (volumen
`backend-claves` en Docker) solo guarda las claves de sesión: si se pierde, los usuarios simplemente
vuelven a iniciar sesión.

---

## 5. Usar .NET 8 en lugar de .NET 10

Si en la PC o el servidor solo está .NET 8, en `src/HelpDesk.Api/HelpDesk.Api.csproj` cambiar:

```xml
<TargetFramework>net8.0</TargetFramework>
...
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.*" />
```

El código no usa nada exclusivo de .NET 10. (En Docker se usan las imágenes de .NET 10.)

---

## 6. Problemas comunes

| Síntoma | Causa probable y solución |
|---|---|
| Al iniciar: *No se pudo conectar o preparar la base de datos PostgreSQL* | Revisar host, puerto, usuario y contraseña en la cadena de conexión y que el servicio de PostgreSQL esté iniciado. Con Docker: `docker compose ps` y `docker compose logs postgres`. |
| *password authentication failed for user "helpdesk"* | Contraseña incorrecta. Con Docker: la contraseña queda fijada al crear el volumen; si se cambió `POSTGRES_PASSWORD` después, cambiarla también en la base (`ALTER ROLE`) o recrear el volumen con `docker compose down -v` (borra los datos). |
| *permission denied to create database* | Crear la base a mano (sección 2.1) y poner `"CrearBaseSiNoExiste": false`. |
| *permission denied to create extension "unaccent"* | Crear la extensión con un superusuario: `CREATE EXTENSION unaccent;` en la base `helpdeskasse`. |
| Con Docker, el puerto 8080, 5080 o 5432 ya está en uso | Cambiar `FRONTEND_PORT`, `BACKEND_PORT` o `POSTGRES_PORT` en `.env`. |
| No se puede entrar desde otra PC | Firewall de Windows (sección 2.3) o la URL no usa el nombre/IP del servidor. |
| Se cierran todas las sesiones al reiniciar | La carpeta `claves\` no tiene permiso de escritura para la cuenta que ejecuta la aplicación. |
| Error de compilación al ejecutar `dotnet run` | Copiar el mensaje completo de la consola; casi siempre indica el archivo y la línea. |
| No restaura paquetes NuGet | La PC necesita acceso a `api.nuget.org` la primera vez (o un feed NuGet interno configurado). |
