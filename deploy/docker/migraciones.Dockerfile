# Aplica las migraciones de EF Core con un ejecutable autónomo (efbundle) y termina.
# Construir desde la raíz del repositorio: docker build -f deploy/docker/migraciones.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["proyecto.api/proyecto.api.csproj", "proyecto.api/"]
COPY ["proyect.domain/proyect.domain.csproj", "proyect.domain/"]
COPY ["proyect.application/proyect.application.csproj", "proyect.application/"]
COPY ["proyect.infrastructure/proyect.infrastructure.csproj", "proyect.infrastructure/"]
RUN dotnet restore "./proyecto.api/proyecto.api.csproj"
COPY . .
RUN dotnet tool install --tool-path /tools dotnet-ef --version 10.0.11
RUN /tools/dotnet-ef migrations bundle --project proyect.infrastructure --startup-project proyecto.api \
    --configuration Release --self-contained -r linux-x64 -o /out/efbundle --force

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /out/efbundle /app/efbundle
USER $APP_UID
# Sin --connection: ApplicationDbContextDesignTimeFactory lee ConnectionStrings__PostgreSQL del entorno,
# así la contraseña no aparece como argumento en `ps` del host.
ENTRYPOINT ["/app/efbundle"]
