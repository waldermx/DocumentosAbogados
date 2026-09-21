using DocApi.Models;
using DocApi.Services;

namespace DocApi.Endpoints;

public static class RegistrosEndpoints
{
    public static void MapRegistrosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/registros");

        grupo.MapGet("/", (SheetCache cache) =>
        {
            var snapshot = cache.Actual;
            return Results.Ok(new RegistrosResponseDto
            {
                Registros = snapshot.Registros,
                ErroresParseo = snapshot.ErroresParseo,
                UltimaSync = snapshot.UltimaSync
            });
        });

        grupo.MapGet("/{id:int}", (int id, SheetCache cache) =>
        {
            var registro = cache.Actual.Registros.FirstOrDefault(r => r.Fila == id);
            return registro is null
                ? Results.NotFound(new { error = $"No hay registro para la fila {id}." })
                : Results.Ok(registro);
        });
    }
}
