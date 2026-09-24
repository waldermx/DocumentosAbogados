using System.Text.Json;
using DocGenApp.Models;
using Xunit;

namespace DocGenApp.Tests;

/// <summary>
/// El servidor serializaba <c>Resultado</c> como el número del enum. El cliente lo declara
/// como string, así que <c>POST /sync</c> reventaba entero con
/// «The Json value could not be converted to System.String. Path: $.resultado».
/// El servidor ya manda texto; estos tests fijan que el cliente aguante las dos formas,
/// para no depender de que el servidor esté redesplegado.
/// </summary>
public class SyncResultDtoTests
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    private static SyncResultDto Deserializar(string json) =>
        JsonSerializer.Deserialize<SyncResultDto>(json, Opts)!;

    [Fact]
    public void Acepta_el_resultado_como_texto()
    {
        var dto = Deserializar("""{"resultado":"Actualizado","ultimaSync":null,"totalRegistros":3}""");

        Assert.Equal("Actualizado", dto.Resultado);
        Assert.Equal(3, dto.TotalRegistros);
    }

    [Fact]
    public void Acepta_el_resultado_como_numero_de_un_servidor_anterior()
    {
        var dto = Deserializar("""{"resultado":0,"ultimaSync":null,"totalRegistros":3}""");

        Assert.Equal("0", dto.Resultado);
        Assert.Equal(3, dto.TotalRegistros);
    }

    [Fact]
    public void Un_resultado_nulo_no_rompe_la_respuesta()
    {
        var dto = Deserializar("""{"resultado":null,"totalRegistros":1}""");

        Assert.Equal(string.Empty, dto.Resultado);
        Assert.Equal(1, dto.TotalRegistros);
    }

    [Fact]
    public void Los_campos_se_leen_sin_importar_las_mayusculas_del_nombre_de_columna()
    {
        // La columna se configura con un nombre libre ("NOMBRE", "Nombre", "nombre").
        // El registro debe encontrarla igual, o el panel se queda sin nombre según
        // cómo esté escrita la variable de entorno del servidor.
        var payload = JsonSerializer.Deserialize<RegistrosResponseDto>(
            """
            {"registros":[{"fila":2,
              "campos":{"grupos":{"CONSECUTIVO":"133/2025","NOMBRE":"JUAN PEREZ"}}}],
             "ultimaSync":null}
            """, Opts)!;

        var campos = payload.Registros[0].Campos;

        Assert.Equal("JUAN PEREZ", campos.Nombre);
        Assert.Equal("133/2025", campos.Consecutivo);
    }

    [Fact]
    public void El_resto_del_payload_se_lee_igual_venga_como_venga_el_enum()
    {
        var dto = Deserializar(
            """{"resultado":2,"ultimaSync":"2026-09-21T10:00:00+00:00","totalRegistros":5,"mensaje":"algo"}""");

        Assert.Equal(5, dto.TotalRegistros);
        Assert.Equal("algo", dto.Mensaje);
        Assert.NotNull(dto.UltimaSync);
    }
}
