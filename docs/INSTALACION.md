# Instalación

Hay dos formas de usar la aplicación:

- **Probarla en una PC** (sección 1): se ejecuta con `dotnet run` y se abre en el navegador de esa PC.
- **Instalarla para toda la mesa** (sección 2): se publica en un servidor o PC de la red interna de
  ASSE y queda corriendo como servicio de Windows (o en IIS). Los demás entran desde su navegador.

---

## 1. Probar en una PC

### Requisitos

| Componente | Versión | Notas |
|---|---|---|
| SDK de .NET | 10 | `dotnet --list-sdks` debe mostrar una versión 10.x. Con .NET 8 también funciona: ver *Usar .NET 8* más abajo. |
| SQL Server | 2017 o superior | Sirve SQL Server Express, Developer o LocalDB (viene con Visual Studio). |
| Navegador | Edge, Chrome o Firefox actuales | |

La primera compilación descarga tres paquetes de NuGet (Dapper, Microsoft.Data.SqlClient y
Microsoft.Extensions.Hosting.WindowsServices), así que esa PC necesita acceso a `api.nuget.org`.

### Pasos

1. **Elegir la cadena de conexión** en `src/HelpDesk.Api/appsettings.json`:

   | SQL Server | Valor de `ConnectionStrings:HelpDesk` |
   |---|---|
   | Instancia local por defecto | `Server=localhost;Database=HelpDeskAsse;Trusted_Connection=True;TrustServerCertificate=True;` |
   | SQL Server Express | `Server=localhost\\SQLEXPRESS;Database=HelpDeskAsse;Trusted_Connection=True;TrustServerCertificate=True;` |
   | LocalDB | `Server=(localdb)\\MSSQLLocalDB;Database=HelpDeskAsse;Trusted_Connection=True;` |
   | Usuario y contraseña SQL | `Server=SERVIDOR;Database=HelpDeskAsse;User Id=helpdesk;Password=...;TrustServerCertificate=True;` |

   En JSON la barra invertida se escribe doble (`\\`).

2. **Ejecutar** `iniciar.bat` (doble clic) o, desde una consola:

   ```
   cd src/HelpDesk.Api
   dotnet run
   ```

   Hay que ejecutarlo desde la carpeta `src/HelpDesk.Api`, porque ahí están `appsettings.json` y la
   interfaz (`wwwroot`). También se puede abrir `HelpDeskAsse.sln` con Visual Studio 2022 y presionar F5.

   La primera vez:
   - crea la base `HelpDeskAsse` con intercalación `Modern_Spanish_CI_AS` (si el usuario tiene permiso);
   - crea las tablas y carga los catálogos del Excel (responsables, tipos, medios, estados, prioridades);
   - crea el usuario **admin** con una contraseña al azar, que se muestra en la consola y se guarda en
     `src/HelpDesk.Api/admin-password-inicial.txt`. Borrá ese archivo después de entrar.

3. **Abrir** http://localhost:5080, entrar como `admin` y elegir una contraseña nueva.

4. **Importar el Excel**: *Configuración → Importar Excel*. Antes de guardar muestra el resumen
   (registros encontrados, nuevos, ya existentes, con error y advertencias). Se puede importar el mismo
   archivo varias veces: las solicitudes cuyo ID ya existe se omiten.

5. **Crear usuarios**: *Configuración → Usuarios*. Conviene vincular cada usuario con su responsable
   (Agustín, Facundo, Mauro) para que funcione *Mis solicitudes* y se complete solo el responsable al registrar.

Para detener la aplicación: `Ctrl+C` en la consola.

---

## 2. Instalar para toda la mesa (servidor interno)

### 2.1 Base de datos

Lo recomendable es pedirle al DBA una base en el SQL Server de ASSE:

```sql
CREATE DATABASE HelpDeskAsse COLLATE Modern_Spanish_CI_AS;
```

y un usuario con permisos `db_datareader`, `db_datawriter` y `db_ddladmin` sobre esa base
(`db_ddladmin` solo hace falta para crear las tablas la primera vez; después se puede quitar).

Si el DBA prefiere crear las tablas él mismo, puede ejecutar en orden los scripts de `database/`
(`001_esquema.sql` y `002_datos_iniciales.sql`) y luego registrar que ya se aplicaron:

```sql
CREATE TABLE dbo.VersionEsquema (Version INT NOT NULL PRIMARY KEY, Nombre NVARCHAR(200) NOT NULL, AplicadaEn DATETIME2(0) NOT NULL);
INSERT INTO dbo.VersionEsquema VALUES (1, N'esquema', SYSDATETIME()), (2, N'datos_iniciales', SYSDATETIME());
```

En ese caso, poner `"AplicarMigracionesAlIniciar": false` y `"CrearBaseSiNoExiste": false` en `appsettings.json`.

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

Registra el servicio **HelpDeskAsse** con inicio automático y lo arranca. Si la base usa autenticación
de Windows, la cuenta del servicio necesita acceso a SQL Server: se puede cambiar la cuenta en
*Servicios → HelpDeskAsse → Iniciar sesión*, o usar usuario y contraseña SQL en la cadena de conexión.

Abrir el puerto en el firewall de Windows (solo red interna):

```powershell
New-NetFirewallRule -DisplayName "Mesa de Ayuda ASSE" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain
```

Los usuarios entran a `http://NOMBRE-DEL-SERVIDOR:5080`.

### 2.4 Alternativa: IIS

1. Instalar el *ASP.NET Core Hosting Bundle* para .NET 10 en el servidor.
2. Crear un sitio en IIS que apunte a la carpeta `publicado\`, con un *Application Pool* sin código administrado.
3. La identidad del Application Pool necesita acceso a la base y permiso de escritura en la carpeta `claves\`.

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

## 3. Configuración (`appsettings.json`)

| Clave | Para qué sirve | Valor por defecto |
|---|---|---|
| `ConnectionStrings:HelpDesk` | Conexión a SQL Server | `localhost`, base `HelpDeskAsse` |
| `HelpDesk:ZonaHoraria` | Hora con la que se registran las solicitudes | `America/Montevideo` |
| `HelpDesk:AplicarMigracionesAlIniciar` | Crea o actualiza las tablas al arrancar | `true` |
| `HelpDesk:CrearBaseSiNoExiste` | Crea la base si no existe (requiere permiso) | `true` |
| `HelpDesk:HorasSesion` | Horas sin actividad hasta que se cierra la sesión | `12` |
| `HelpDesk:PasswordAdminInicial` | Contraseña del primer `admin` (si se deja vacía se genera una) | vacío |
| `HelpDesk:TamanoMaximoImportacionMb` | Tamaño máximo del Excel a importar | `30` |
| `HelpDesk:CarpetaClaves` | Carpeta de las claves que cifran la cookie de sesión | `claves` |
| `Urls` | Dirección y puerto donde escucha | `http://0.0.0.0:5080` |

---

## 4. Respaldos

Toda la información está en la base `HelpDeskAsse`. Alcanza con el respaldo habitual de SQL Server, por ejemplo:

```sql
BACKUP DATABASE HelpDeskAsse TO DISK = N'D:\Respaldos\HelpDeskAsse.bak' WITH INIT, COMPRESSION;
```

(o un plan de mantenimiento diario). La carpeta `claves\` solo guarda las claves de sesión: si se pierde,
los usuarios simplemente vuelven a iniciar sesión.

---

## 5. Usar .NET 8 en lugar de .NET 10

Si en la PC o el servidor solo está .NET 8, en `src/HelpDesk.Api/HelpDesk.Api.csproj` cambiar:

```xml
<TargetFramework>net8.0</TargetFramework>
...
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.*" />
```

El código no usa nada exclusivo de .NET 10.

---

## 6. Problemas comunes

| Síntoma | Causa probable y solución |
|---|---|
| Al iniciar: *No se pudo conectar o preparar la base de datos SQL Server* | Revisar el servidor y la instancia en la cadena de conexión (`localhost`, `localhost\\SQLEXPRESS`, `(localdb)\\MSSQLLocalDB`) y que el servicio de SQL Server esté iniciado. |
| *The certificate chain was issued by an authority that is not trusted* | Agregar `TrustServerCertificate=True;` a la cadena de conexión. |
| *CREATE DATABASE permission denied* | Pedir la base al DBA (sección 2.1) y poner `"CrearBaseSiNoExiste": false`. |
| *Login failed for user 'NT AUTHORITY\\...'* al correr como servicio | La cuenta del servicio no tiene acceso a SQL Server: cambiar la cuenta del servicio o usar usuario SQL. |
| No se puede entrar desde otra PC | Firewall de Windows (sección 2.3) o la URL no usa el nombre/IP del servidor. |
| Se cierran todas las sesiones al reiniciar | La carpeta `claves\` no tiene permiso de escritura para la cuenta que ejecuta la aplicación. |
| Error de compilación al ejecutar `dotnet run` | Copiar el mensaje completo de la consola; casi siempre indica el archivo y la línea. |
| No restaura paquetes NuGet | La PC necesita acceso a `api.nuget.org` la primera vez (o un feed NuGet interno configurado). |
