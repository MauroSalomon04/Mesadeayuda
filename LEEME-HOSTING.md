# Instrucciones para quien aloja el servidor

Mesa de Ayuda ASSE: backend ASP.NET Core 10 + frontend (nginx) + PostgreSQL 17, todo en Docker.
La base de datos arranca **vacía**: solo catálogos iniciales y el usuario `admin`. No hay solicitudes ni tareas.

## Requisitos
- Docker y Docker Compose en el servidor.

## Pasos
1. `cp .env.example .env` y editar:
   - `POSTGRES_PASSWORD`: una clave propia (sin el carácter `;`).
   - `ADMIN_PASSWORD_INICIAL`: contraseña inicial de `admin` (si queda vacía se genera y aparece en `docker compose logs backend`).
   - `FRONTEND_PORT`: puerto público (por defecto 8080).
2. `docker compose up -d --build`
3. Abrir `http://IP-DEL-SERVIDOR:FRONTEND_PORT` e ingresar con `admin`. Se pide cambiar la contraseña.
4. Publicar con HTTPS delante (proxy inverso) si es acceso por internet.

Los datos quedan en el volumen `postgres-data`. Más detalles: `docs/INSTALACION.md`.
Sin Docker (Windows/IIS/servicio): ver `docs/INSTALACION.md`.
