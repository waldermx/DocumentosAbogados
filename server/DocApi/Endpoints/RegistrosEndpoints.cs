using DocApi.Models;
using DocApi.Services;

namespace DocApi.Endpoints;

public static class RegistrosEndpoints
{
    public static void MapRegistrosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/registros");

        grupo.MapGet("/", (SheetCache cache, ImpresosStore impresos) =>
        {
            var snapshot = cache.Actual;
            return Results.Ok(new RegistrosResponseDto
            {
                Registros = snapshot.Registros,
                ErroresParseo = snapshot.ErroresParseo,
                UltimaSync = snapshot.UltimaSync,
                Impresos = impresos.Actual.ToDictionary(kv => kv.Key, kv => kv.Value.Fecha)
            });
        });

        grupo.MapGet("/{id:int}", (int id, SheetCache cache) =>
        {
            var registro = cache.Actual.Registros.FirstOrDefault(r => r.Fila == id);
            return registro is null
                ? Results.NotFound(new { error = $"No hay registro para la fila {id}." })
                : Results.Ok(registro);
        });

        // Marca manual: el cliente la dispara con un botón aparte, nunca automáticamente
        // al generar el documento, para no dar por impreso algo que solo se generó.
        grupo.MapPost("/{id:int}/impreso", (int id, SheetCache cache, ImpresosStore impresos) =>
        {
            if (!cache.Actual.ValoresRawPorFila.TryGetValue(id, out var fila))
            {
                return Results.NotFound(new { error = $"No hay registro para la fila {id}." });
            }

            var entrada = impresos.Marcar(id, fila.Firma);
            return Results.Ok(new ImpresoEstadoDto { Fila = id, Impreso = entrada.Fecha });
        });

        grupo.MapDelete("/{id:int}/impreso", (int id, ImpresosStore impresos) =>
        {
            impresos.Desmarcar(id);
            return Results.Ok(new ImpresoEstadoDto { Fila = id, Impreso = null });
        });
    }
}
