/* =====================================================================
   Mesa de Ayuda ASSE - IntegradoC
   Script 001: esquema de base de datos (SQL Server 2017 o superior)

   La aplicación ejecuta este script automáticamente la primera vez
   (tabla dbo.VersionEsquema). También puede ejecutarse a mano desde
   SQL Server Management Studio sobre una base vacía.

   Convenciones:
   - Todas las fechas se guardan en hora local de Uruguay
     (America/Montevideo), tipo DATETIME2(0).
   - Los catálogos nunca se borran: se desactivan (Activo = 0) para
     no romper el historial.
   - Las solicitudes no se borran físicamente: EliminadoEn/EliminadoPorId.
   ===================================================================== */

/* ---------------------------------------------------------------------
   Catálogos configurables
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Responsable (
    Id      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Responsable PRIMARY KEY,
    Nombre  NVARCHAR(100)     NOT NULL CONSTRAINT UQ_Responsable_Nombre UNIQUE,
    Activo  BIT               NOT NULL CONSTRAINT DF_Responsable_Activo DEFAULT (1),
    Orden   INT               NOT NULL CONSTRAINT DF_Responsable_Orden DEFAULT (0)
);
GO

CREATE TABLE dbo.MedioContacto (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedioContacto PRIMARY KEY,
    Nombre         NVARCHAR(60)      NOT NULL CONSTRAINT UQ_MedioContacto_Nombre UNIQUE,
    Activo         BIT               NOT NULL CONSTRAINT DF_MedioContacto_Activo DEFAULT (1),
    Orden          INT               NOT NULL CONSTRAINT DF_MedioContacto_Orden DEFAULT (0),
    Predeterminado BIT               NOT NULL CONSTRAINT DF_MedioContacto_Predeterminado DEFAULT (0)
);
GO

/* Los códigos (AD, FFRRHH, Seg, ...) se guardan tal cual se usan en la mesa.
   Descripcion es opcional y la completa un administrador si lo desea. */
CREATE TABLE dbo.TipoSolicitud (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TipoSolicitud PRIMARY KEY,
    Codigo      NVARCHAR(30)      NOT NULL CONSTRAINT UQ_TipoSolicitud_Codigo UNIQUE,
    Descripcion NVARCHAR(200)     NULL,
    Activo      BIT               NOT NULL CONSTRAINT DF_TipoSolicitud_Activo DEFAULT (1),
    Orden       INT               NOT NULL CONSTRAINT DF_TipoSolicitud_Orden DEFAULT (0)
);
GO

/* EsResuelto = 1 indica que el estado cierra la solicitud.
   Todo estado con EsResuelto = 0 se considera "pendiente". */
CREATE TABLE dbo.Estado (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Estado PRIMARY KEY,
    Nombre         NVARCHAR(60)      NOT NULL CONSTRAINT UQ_Estado_Nombre UNIQUE,
    EsResuelto     BIT               NOT NULL CONSTRAINT DF_Estado_EsResuelto DEFAULT (0),
    Color          NVARCHAR(20)      NULL,
    Activo         BIT               NOT NULL CONSTRAINT DF_Estado_Activo DEFAULT (1),
    Orden          INT               NOT NULL CONSTRAINT DF_Estado_Orden DEFAULT (0),
    Predeterminado BIT               NOT NULL CONSTRAINT DF_Estado_Predeterminado DEFAULT (0)
);
GO

/* Nivel: 1 = más urgente. Se usa para ordenar. */
CREATE TABLE dbo.Prioridad (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prioridad PRIMARY KEY,
    Nombre         NVARCHAR(30)      NOT NULL CONSTRAINT UQ_Prioridad_Nombre UNIQUE,
    Nivel          INT               NOT NULL,
    Activo         BIT               NOT NULL CONSTRAINT DF_Prioridad_Activo DEFAULT (1),
    Orden          INT               NOT NULL CONSTRAINT DF_Prioridad_Orden DEFAULT (0),
    Predeterminado BIT               NOT NULL CONSTRAINT DF_Prioridad_Predeterminado DEFAULT (0)
);
GO

CREATE TABLE dbo.Oficina (
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Oficina PRIMARY KEY,
    Nombre   NVARCHAR(150)     NOT NULL CONSTRAINT UQ_Oficina_Nombre UNIQUE,
    Activo   BIT               NOT NULL CONSTRAINT DF_Oficina_Activo DEFAULT (1),
    CreadoEn DATETIME2(0)      NOT NULL
);
GO

CREATE TABLE dbo.AreaAsse (
    Id     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AreaAsse PRIMARY KEY,
    Nombre NVARCHAR(150)     NOT NULL CONSTRAINT UQ_AreaAsse_Nombre UNIQUE,
    Activo BIT               NOT NULL CONSTRAINT DF_AreaAsse_Activo DEFAULT (1),
    Orden  INT               NOT NULL CONSTRAINT DF_AreaAsse_Orden DEFAULT (0)
);
GO

/* ---------------------------------------------------------------------
   Usuarios del sistema
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Usuario (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    NombreUsuario       NVARCHAR(50)      NOT NULL CONSTRAINT UQ_Usuario_NombreUsuario UNIQUE,
    NombreCompleto      NVARCHAR(120)     NOT NULL,
    PasswordHash        NVARCHAR(200)     NOT NULL,
    Rol                 NVARCHAR(20)      NOT NULL CONSTRAINT CK_Usuario_Rol CHECK (Rol IN (N'ADMIN', N'SOPORTE')),
    ResponsableId       INT               NULL CONSTRAINT FK_Usuario_Responsable REFERENCES dbo.Responsable (Id),
    Activo              BIT               NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    DebeCambiarPassword BIT               NOT NULL CONSTRAINT DF_Usuario_DebeCambiarPassword DEFAULT (0),
    -- Cambia al modificar contraseña, rol o estado: invalida las sesiones abiertas.
    SelloSeguridad      UNIQUEIDENTIFIER  NOT NULL CONSTRAINT DF_Usuario_SelloSeguridad DEFAULT (NEWID()),
    CreadoEn            DATETIME2(0)      NOT NULL,
    UltimoAcceso        DATETIME2(0)      NULL
);
GO

/* ---------------------------------------------------------------------
   Importaciones de Excel (trazabilidad)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Importacion (
    Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Importacion PRIMARY KEY,
    FechaHora             DATETIME2(0)      NOT NULL,
    UsuarioId             INT               NOT NULL CONSTRAINT FK_Importacion_Usuario REFERENCES dbo.Usuario (Id),
    NombreArchivo         NVARCHAR(260)     NOT NULL,
    HashArchivo           CHAR(64)          NOT NULL,
    FilasLeidas           INT               NOT NULL,
    SolicitudesNuevas     INT               NOT NULL,
    SolicitudesExistentes INT               NOT NULL,
    FilasConError         INT               NOT NULL,
    FilasConAdvertencia   INT               NOT NULL,
    TareasExtraNuevas     INT               NOT NULL
);
GO

/* ---------------------------------------------------------------------
   Numeración de solicitudes.
   Guarda el último ID emitido: un ID nunca se reutiliza, aunque la
   solicitud se elimine.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Secuencia (
    Nombre      NVARCHAR(50) NOT NULL CONSTRAINT PK_Secuencia PRIMARY KEY,
    UltimoValor INT          NOT NULL
);
GO

/* ---------------------------------------------------------------------
   Solicitudes (equivale a cada fila del Excel)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Solicitud (
    Id                  INT            NOT NULL CONSTRAINT PK_Solicitud PRIMARY KEY,  -- asignado desde dbo.Secuencia
    FechaIngreso        DATETIME2(0)   NOT NULL,                                       -- automática, no cambia al editar
    DiaSemana           INT            NOT NULL CONSTRAINT CK_Solicitud_DiaSemana CHECK (DiaSemana BETWEEN 1 AND 7), -- 1 = lunes
    NombreFuncionario   NVARCHAR(150)  NULL,
    OficinaId           INT            NULL CONSTRAINT FK_Solicitud_Oficina       REFERENCES dbo.Oficina (Id),
    MedioContactoId     INT            NULL CONSTRAINT FK_Solicitud_MedioContacto REFERENCES dbo.MedioContacto (Id),
    TipoSolicitudId     INT            NULL CONSTRAINT FK_Solicitud_TipoSolicitud REFERENCES dbo.TipoSolicitud (Id),
    Descripcion         NVARCHAR(500)  NULL,
    Observaciones       NVARCHAR(MAX)  NULL,
    ResponsableId       INT            NULL CONSTRAINT FK_Solicitud_Responsable   REFERENCES dbo.Responsable (Id),
    DuracionEstimadaMin INT            NULL CONSTRAINT CK_Solicitud_Duracion CHECK (DuracionEstimadaMin IS NULL OR DuracionEstimadaMin >= 0),
    EstadoId            INT            NULL CONSTRAINT FK_Solicitud_Estado        REFERENCES dbo.Estado (Id),
    PrioridadId         INT            NULL CONSTRAINT FK_Solicitud_Prioridad     REFERENCES dbo.Prioridad (Id),
    FechaResolucion     DATETIME2(0)   NULL,   -- se completa al pasar a un estado resuelto
    MinutosResolucion   INT            NULL,   -- minutos entre FechaIngreso y FechaResolucion
    Origen              NVARCHAR(20)   NOT NULL CONSTRAINT CK_Solicitud_Origen CHECK (Origen IN (N'APP', N'IMPORTACION')),
    ImportacionId       INT            NULL CONSTRAINT FK_Solicitud_Importacion   REFERENCES dbo.Importacion (Id),
    FilaExcel           INT            NULL,
    Version             INT            NOT NULL CONSTRAINT DF_Solicitud_Version DEFAULT (1),
    CreadoPorId         INT            NULL CONSTRAINT FK_Solicitud_CreadoPor     REFERENCES dbo.Usuario (Id),
    CreadoEn            DATETIME2(0)   NOT NULL,
    ActualizadoPorId    INT            NULL CONSTRAINT FK_Solicitud_ActualizadoPor REFERENCES dbo.Usuario (Id),
    ActualizadoEn       DATETIME2(0)   NOT NULL,
    EliminadoEn         DATETIME2(0)   NULL,
    EliminadoPorId      INT            NULL CONSTRAINT FK_Solicitud_EliminadoPor  REFERENCES dbo.Usuario (Id)
);
GO

CREATE INDEX IX_Solicitud_FechaIngreso   ON dbo.Solicitud (FechaIngreso) INCLUDE (EstadoId, ResponsableId, EliminadoEn);
CREATE INDEX IX_Solicitud_EstadoId       ON dbo.Solicitud (EstadoId) INCLUDE (FechaIngreso, ResponsableId, EliminadoEn);
CREATE INDEX IX_Solicitud_ResponsableId  ON dbo.Solicitud (ResponsableId);
CREATE INDEX IX_Solicitud_OficinaId      ON dbo.Solicitud (OficinaId);
CREATE INDEX IX_Solicitud_TipoSolicitudId ON dbo.Solicitud (TipoSolicitudId);
GO

/* ---------------------------------------------------------------------
   Historial de cambios de cada solicitud (una fila por campo cambiado)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.SolicitudHistorial (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SolicitudHistorial PRIMARY KEY,
    SolicitudId   INT                  NOT NULL CONSTRAINT FK_SolicitudHistorial_Solicitud REFERENCES dbo.Solicitud (Id),
    FechaHora     DATETIME2(0)         NOT NULL,
    UsuarioId     INT                  NULL CONSTRAINT FK_SolicitudHistorial_Usuario REFERENCES dbo.Usuario (Id),
    Accion        NVARCHAR(30)         NOT NULL,  -- CREADA, MODIFICADA, ESTADO, RESUELTA, REABIERTA, IMPORTADA, FECHA_CORREGIDA, ELIMINADA
    Campo         NVARCHAR(50)         NULL,
    ValorAnterior NVARCHAR(MAX)        NULL,
    ValorNuevo    NVARCHAR(MAX)        NULL,
    Detalle       NVARCHAR(2000)       NULL
);
GO

CREATE INDEX IX_SolicitudHistorial_SolicitudId ON dbo.SolicitudHistorial (SolicitudId, Id);
GO

/* ---------------------------------------------------------------------
   Tareas extra (hoja "Detalle tareas extra")
   --------------------------------------------------------------------- */
CREATE TABLE dbo.TareaExtra (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TareaExtra PRIMARY KEY,
    AreaAsseId       INT               NULL CONSTRAINT FK_TareaExtra_AreaAsse REFERENCES dbo.AreaAsse (Id),
    Tarea            NVARCHAR(500)     NOT NULL,
    Impacto          NVARCHAR(MAX)     NULL,
    CargaTrabajo     NVARCHAR(1000)    NULL,
    Origen           NVARCHAR(20)      NOT NULL CONSTRAINT CK_TareaExtra_Origen CHECK (Origen IN (N'APP', N'IMPORTACION')),
    ImportacionId    INT               NULL CONSTRAINT FK_TareaExtra_Importacion REFERENCES dbo.Importacion (Id),
    CreadoPorId      INT               NULL CONSTRAINT FK_TareaExtra_CreadoPor REFERENCES dbo.Usuario (Id),
    CreadoEn         DATETIME2(0)      NOT NULL,
    ActualizadoPorId INT               NULL CONSTRAINT FK_TareaExtra_ActualizadoPor REFERENCES dbo.Usuario (Id),
    ActualizadoEn    DATETIME2(0)      NOT NULL,
    EliminadoEn      DATETIME2(0)      NULL,
    EliminadoPorId   INT               NULL CONSTRAINT FK_TareaExtra_EliminadoPor REFERENCES dbo.Usuario (Id)
);
GO

/* Integrantes de la mesa implicados en cada tarea (muchos a muchos) */
CREATE TABLE dbo.TareaExtraImplicado (
    TareaExtraId  INT NOT NULL CONSTRAINT FK_TareaExtraImplicado_TareaExtra REFERENCES dbo.TareaExtra (Id) ON DELETE CASCADE,
    ResponsableId INT NOT NULL CONSTRAINT FK_TareaExtraImplicado_Responsable REFERENCES dbo.Responsable (Id),
    CONSTRAINT PK_TareaExtraImplicado PRIMARY KEY (TareaExtraId, ResponsableId)
);
GO

/* ---------------------------------------------------------------------
   Auditoría de acciones administrativas (catálogos, usuarios, tareas)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Auditoria (
    Id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Auditoria PRIMARY KEY,
    FechaHora DATETIME2(0)         NOT NULL,
    UsuarioId INT                  NULL CONSTRAINT FK_Auditoria_Usuario REFERENCES dbo.Usuario (Id),
    Entidad   NVARCHAR(50)         NOT NULL,
    EntidadId NVARCHAR(50)         NULL,
    Accion    NVARCHAR(30)         NOT NULL,
    Detalle   NVARCHAR(MAX)        NULL
);
GO

CREATE INDEX IX_Auditoria_FechaHora ON dbo.Auditoria (FechaHora);
GO
