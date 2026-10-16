# Backend: ASP.NET Core 10 + Dapper + Npgsql (PostgreSQL)
# Se construye desde la raíz del repositorio (los scripts de /database se incrustan en el ejecutable):
#   docker build -f src/HelpDesk.Api/Dockerfile .

# ---------- compilación ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo el proyecto, para aprovechar la caché de "restore".
COPY src/HelpDesk.Api/HelpDesk.Api.csproj src/HelpDesk.Api/
RUN dotnet restore src/HelpDesk.Api/HelpDesk.Api.csproj

COPY database/ database/
COPY src/HelpDesk.Api/ src/HelpDesk.Api/
RUN dotnet publish src/HelpDesk.Api/HelpDesk.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- ejecución ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=America/Montevideo

COPY --from=build /app/publish .

# Carpeta de las claves de la cookie de sesión (se monta como volumen persistente).
RUN mkdir -p /app/claves && chown -R "$APP_UID" /app /app/claves

USER $APP_UID
EXPOSE 5080
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]
