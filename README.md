# DISMATEC — Network Traffic System

Plataforma de prevención de riesgos de ciberseguridad que reúne simulaciones
de phishing, actividades de concientización y análisis de registros HTTP.

## Requisitos

- SDK de .NET 10
- Docker Compose para levantar PostgreSQL local
- Herramienta `dotnet ef` 10.0.11 para aplicar las migraciones

## Ejecutar en desarrollo

Desde la raíz del repositorio, configura estas variables en tu terminal con
valores de desarrollo:

- `POSTGRES_PASSWORD` — contraseña local del contenedor de PostgreSQL.
- `ConnectionStrings__PostgreSQL` — conexión a `127.0.0.1:5433`, base
  `DISMATEC`, usuario `postgres` y la misma contraseña del contenedor.
- `Jwt__Key` — clave local de al menos 32 bytes.

Levanta PostgreSQL y aplica las migraciones:

```bash
docker compose up -d db
dotnet ef database update \
  --project proyect.infrastructure \
  --startup-project proyecto.api
```

Inicia la API y la web en terminales separadas:

```bash
dotnet run --project proyecto.api --launch-profile http
dotnet run --project proyecto.web --launch-profile http
```

Abre la web en `http://localhost:5266`. La API escucha en
`http://localhost:5016` y su estado se puede consultar en `/health`.
Para iniciar sesión se necesita una cuenta habilitada con segundo factor.
El análisis de registros HTTP requiere un servicio de análisis configurado.

## Flujo de ramas

- `main` contiene las versiones estables.
- `develop` integra los cambios antes de publicar una versión estable.
- `feature/hu-XX-funcionalidad` y `fix/descripcion` parten de `develop`
  y se integran en ella mediante pull requests.
- Una versión estable se integra mediante un pull request de `develop` a `main`.