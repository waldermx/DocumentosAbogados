using DocApi.Models;
using DocApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocApi.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // Refresco manual desde el botón "Actualizar" del cliente.
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
                TotalErrores = snapshot.ErroresParseo.Count,
                Estado = coordinator.UltimoResultado?.Resultado.ToString() ?? "SinSyncAun"
            });
        });

        // Qué configuración tiene cargada ESTA instancia. Sirve para distinguir
        // "el patrón está mal" de "el servidor desplegado es una versión anterior",
        // que desde el cliente se ven exactamente igual.
        // [FromServices] es obligatorio en RowParser: al tener un método público TryParse,
        // Minimal APIs lo toma por un tipo enlazable desde la URL, no encuentra la firma
        // estática que espera y lanza al construir el enrutado — tumbando TODAS las rutas,
        // no solo esta.
        app.MapGet("/diagnostico", (
            [FromServices] RowParser parser,
            [FromServices] IOptions<GoogleSheetsOptions> sheets,
            [FromServices] SheetCache cache) =>
        {
            var opciones = sheets.Value;
            var snapshot = cache.Actual;

            return Results.Ok(new
            {
                patrones = parser.Patrones,
                gruposDisponibles = parser.NombresDeGrupo,
                rangoPrincipal = opciones.Range,
                columnasExtra = opciones.ColumnasExtra
                    .Where(c => c.EsValida)
                    .Select(c => new { c.Nombre, c.Range }),
                registrosEnCache = snapshot.Registros.Count,
                erroresDeParseoEnCache = snapshot.ErroresParseo.Count,
                // Cuántas filas cayó en cada patrón: si todas están en el 0 y hay
                // errores de parseo, el patrón de respaldo no se está aplicando.
                filasPorPatron = snapshot.Registros
                    .GroupBy(r => r.PatronUsado)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                ultimaSync = snapshot.UltimaSync
            });
        });
    }
}
