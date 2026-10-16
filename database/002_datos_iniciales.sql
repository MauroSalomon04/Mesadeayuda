/* =====================================================================
   Mesa de Ayuda ASSE - IntegradoC
   Script 002: datos iniciales de los catálogos (PostgreSQL).

   Son los valores que se usan hoy en el Excel de la mesa de ayuda.
   Después se administran desde Configuración (sin tocar código).
   ===================================================================== */

INSERT INTO Responsable (Nombre, Activo, Orden) VALUES
    ('Agustín', TRUE, 1),
    ('Facundo', TRUE, 2),
    ('Mauro',   TRUE, 3);

INSERT INTO MedioContacto (Nombre, Activo, Orden, Predeterminado) VALUES
    ('Teléfono',   TRUE, 1, TRUE),
    ('Mail',       TRUE, 2, FALSE),
    ('Redmine',    TRUE, 3, FALSE),
    ('En persona', TRUE, 4, FALSE),
    ('RocketChat', TRUE, 5, FALSE);

-- Los códigos se mantienen exactamente como se usan en la mesa.
INSERT INTO TipoSolicitud (Codigo, Descripcion, Activo, Orden) VALUES
    ('AD',     NULL, TRUE, 1),
    ('FFRRHH', NULL, TRUE, 2),
    ('Seg',    NULL, TRUE, 3),
    ('SF',     NULL, TRUE, 4),
    ('NC',     NULL, TRUE, 5),
    ('FIEE',   NULL, TRUE, 6),
    ('EE',     NULL, TRUE, 7),
    ('Lotus',  NULL, TRUE, 8),
    ('Error',  NULL, TRUE, 9);

-- Color: verde, naranja, ambar, rojo, azul o gris (lo interpreta la interfaz).
INSERT INTO Estado (Nombre, EsResuelto, Color, Activo, Orden, Predeterminado) VALUES
    ('Resuelto',           TRUE,  'verde',   TRUE, 1, TRUE),
    ('Pendiente HelpDesk', FALSE, 'naranja', TRUE, 2, FALSE),
    ('Pendiente Joaco',    FALSE, 'ambar',   TRUE, 3, FALSE),
    ('Pendiente Mica',     FALSE, 'ambar',   TRUE, 4, FALSE),
    ('Pendiente José',     FALSE, 'ambar',   TRUE, 5, FALSE);

INSERT INTO Prioridad (Nombre, Nivel, Activo, Orden, Predeterminado) VALUES
    ('Alta',  1, TRUE, 1, FALSE),
    ('Media', 2, TRUE, 2, TRUE),
    ('Baja',  3, TRUE, 3, FALSE);

INSERT INTO AreaAsse (Nombre, Activo, Orden) VALUES
    ('Soporte (068)',         TRUE, 1),
    ('Arquitectura (068)',    TRUE, 2),
    ('Infraestructura (068)', TRUE, 3),
    ('Lotus',                 TRUE, 4),
    ('Operaciones (068)',     TRUE, 5);

INSERT INTO Secuencia (Nombre, UltimoValor) VALUES ('Solicitud', 0)
ON CONFLICT (Nombre) DO NOTHING;

INSERT INTO VersionEsquema (Version, Nombre, AplicadaEn)
VALUES (2, 'datos_iniciales', LOCALTIMESTAMP(0))
ON CONFLICT (Version) DO NOTHING;
