/* =====================================================================
   Mesa de Ayuda ASSE - IntegradoC
   Script 002: datos iniciales de los catálogos.

   Son los valores que se usan hoy en el Excel de la mesa de ayuda.
   Después se administran desde Configuración (sin tocar código).
   ===================================================================== */
SET NOCOUNT ON;

INSERT INTO dbo.Responsable (Nombre, Activo, Orden) VALUES
    (N'Agustín', 1, 1),
    (N'Facundo', 1, 2),
    (N'Mauro',   1, 3);

INSERT INTO dbo.MedioContacto (Nombre, Activo, Orden, Predeterminado) VALUES
    (N'Teléfono',   1, 1, 1),
    (N'Mail',       1, 2, 0),
    (N'Redmine',    1, 3, 0),
    (N'En persona', 1, 4, 0),
    (N'RocketChat', 1, 5, 0);

-- Los códigos se mantienen exactamente como se usan en la mesa.
INSERT INTO dbo.TipoSolicitud (Codigo, Descripcion, Activo, Orden) VALUES
    (N'AD',     NULL, 1, 1),
    (N'FFRRHH', NULL, 1, 2),
    (N'Seg',    NULL, 1, 3),
    (N'SF',     NULL, 1, 4),
    (N'NC',     NULL, 1, 5),
    (N'FIEE',   NULL, 1, 6),
    (N'EE',     NULL, 1, 7),
    (N'Lotus',  NULL, 1, 8),
    (N'Error',  NULL, 1, 9);

-- Color: verde, naranja, ambar, rojo, azul o gris (lo interpreta la interfaz).
INSERT INTO dbo.Estado (Nombre, EsResuelto, Color, Activo, Orden, Predeterminado) VALUES
    (N'Resuelto',           1, N'verde',   1, 1, 1),
    (N'Pendiente HelpDesk', 0, N'naranja', 1, 2, 0),
    (N'Pendiente Joaco',    0, N'ambar',   1, 3, 0),
    (N'Pendiente Mica',     0, N'ambar',   1, 4, 0),
    (N'Pendiente José',     0, N'ambar',   1, 5, 0);

INSERT INTO dbo.Prioridad (Nombre, Nivel, Activo, Orden, Predeterminado) VALUES
    (N'Alta',  1, 1, 1, 0),
    (N'Media', 2, 1, 2, 1),
    (N'Baja',  3, 1, 3, 0);

INSERT INTO dbo.AreaAsse (Nombre, Activo, Orden) VALUES
    (N'Soporte (068)',         1, 1),
    (N'Arquitectura (068)',    1, 2),
    (N'Infraestructura (068)', 1, 3),
    (N'Lotus',                 1, 4),
    (N'Operaciones (068)',     1, 5);

INSERT INTO dbo.Secuencia (Nombre, UltimoValor) VALUES (N'Solicitud', 0);
GO
