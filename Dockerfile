# Build desde la raíz del repo:  docker build -f server/DocApi/Dockerfile -t docapi .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY server/DocApi/DocApi.csproj server/DocApi/
RUN dotnet restore server/DocApi/DocApi.csproj

COPY server/ server/
RUN dotnet publish server/DocApi/DocApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Dokploy/Traefik enrutan a este puerto; se puede sobreescribir con ASPNETCORE_HTTP_PORTS.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Variables requeridas en despliegue (nunca se hornean en la imagen):
#   Auth__MasterPassword
#   GoogleSheets__SpreadsheetId
#   GoogleSheets__ServiceAccountJson   (contenido del JSON de service account)
#   GoogleSheets__Hoja                 (nombre de la pestaña; por defecto Hoja1)

# El caché persistido vive aquí; montar un volumen si se quiere conservar entre despliegues.
VOLUME /app/cache

ENTRYPOINT ["dotnet", "DocApi.dll"]
