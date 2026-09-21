using DocApi.Models;
using DocApi.Services;

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
    }
}
