/* =====================================================================
   Mesa de Ayuda ASSE - IntegradoC
   Script 003: número visible y reutilizable de solicitud.

   - Solicitud.Id sigue siendo la clave interna permanente (historial,
     relaciones, enlaces): nunca se reutiliza.
   - Solicitud.Numero es el número que ve el usuario. Al eliminar una
     solicitud su número queda libre y la próxima alta toma el menor
     número disponible.
   - Dos solicitudes activas nunca comparten número: índice único parcial
     (las eliminadas conservan su número, pero no lo bloquean).

   Seguro sobre bases existentes: cada solicitud conserva como número su
   Id actual, así que nada cambia para los usuarios. Puede ejecutarse más
   de una vez sin efectos.
   ===================================================================== */

ALTER TABLE Solicitud ADD COLUMN IF NOT EXISTS Numero INTEGER;

UPDATE Solicitud SET Numero = Id WHERE Numero IS NULL;

ALTER TABLE Solicitud ALTER COLUMN Numero SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS UQ_Solicitud_Numero_Activa
    ON Solicitud (Numero) WHERE EliminadoEn IS NULL;

-- Búsquedas por número incluyendo eliminadas (importación de Excel).
CREATE INDEX IF NOT EXISTS IX_Solicitud_Numero ON Solicitud (Numero);

INSERT INTO VersionEsquema (Version, Nombre, AplicadaEn)
VALUES (3, 'numero_solicitud', LOCALTIMESTAMP(0))
ON CONFLICT (Version) DO NOTHING;
