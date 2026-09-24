using DocApi.Models;
using DocApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocApi.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // Única forma de sincronizar: el botón «Sincronizar» del cliente.
        // Pasa por el coordinator, así que varias llamadas simultáneas se coalescen.
        app.MapPost("/sync", async (
            SyncCoordinator coordinator,
            CancellationToken ct,
            bool? forzar) =>
        {
            var resultado = await coordinator.SyncAsync(forzarLecturaCompleta: forzar ?? false, ct);

            return resultado.Resultado == SyncOutcome.Error
                ? Results.Problem(
                    title: "No se pudo sincronizar con Google Sheets",
                    detail: resultado.Mensaje,
                    statusCode: StatusCodes.Status502BadGateway)
                : Results.Ok(resultado);
        });

        // Público, sin auth: lo consulta Traefik/Dokploy.
        app.MapGet("/health", (SheetCache cache, SyncCoordinator coordinator) =>
        {
            var snapshot = cache.Actual;
            return Results.Ok(new SyncStatusDto
            {
                UltimaSync = snapshot.UltimaSync,
                TotalRegistros = snapshot.Registros.Count,
                Estado = coordinator.UltimoResultado?.Resultado.ToString() ?? "SinSyncAun"
            });
        });

        // Qué configuración tiene cargada ESTA instancia. Sirve para distinguir
        // "la hoja o las columnas están mal" de "el servidor desplegado es una versión
        // anterior", que desde el cliente se ven exactamente igual.
        app.MapGet("/diagnostico", (
            [FromServices] IOptions<GoogleSheetsOptions> sheets,
            [FromServices] SheetCache cache) =>
        {
            var opciones = sheets.Value;
            var snapshot = cache.Actual;

            return Results.Ok(new
            {
                spreadsheetId = opciones.SpreadsheetId,
                hoja = opciones.Hoja,
                columnas = opciones.ColumnasValidas
                    .Select(c => new { c.Nombre, c.Columna, Rango = opciones.RangoDe(c) }),
                registrosEnCache = snapshot.Registros.Count,
                ultimaSync = snapshot.UltimaSync
            });
        });
    }
}
