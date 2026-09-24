using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

/// <summary>
/// Lo persistido en disco (caché y marcas de impreso) se indexa por número de fila, así que
/// solo vale para la hoja de la que salió. Al cambiar de spreadsheet, de pestaña o de
/// columnas tiene que descartarse al arrancar, sin que nadie borre los archivos a mano.
/// </summary>
public sealed class CambioDeOrigenTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "docapi-tests-" + Guid.NewGuid().ToString("N"));

    private string RutaCache => Path.Combine(_carpeta, "sheet-cache.json");
    private string RutaImpresos => Path.Combine(_carpeta, "impresos.json");

    private static GoogleSheetsOptions Hoja(string spreadsheetId, string circuito = "E") => new()
    {
        SpreadsheetId = spreadsheetId,
        Hoja = "Hoja1",
        Columnas =
        [
            new ColumnaOptions { Nombre = "consecutivo", Columna = "B" },
            new ColumnaOptions { Nombre = "nombre", Columna = "D" },
            new ColumnaOptions { Nombre = "circuito", Columna = circuito }
        ]
    };

    private SheetCache CrearCache(GoogleSheetsOptions hoja) => new(
        Options.Create(new SyncOptions { CacheFilePath = RutaCache }),
        Options.Create(hoja),
        NullLogger<SheetCache>.Instance);

    private ImpresosStore CrearImpresos(GoogleSheetsOptions hoja) => new(
        Options.Create(new SyncOptions { ImpresosFilePath = RutaImpresos }),
        Options.Create(hoja),
        NullLogger<ImpresosStore>.Instance);

    private static SheetCache.Snapshot UnRegistro()
    {
        var fila = new FilaCruda(new Dictionary<string, string> { ["consecutivo"] = "1/2026" });
        return new SheetCache.Snapshot(
            [new RegistroDto { Fila = 2, Campos = new ParsedFields { Grupos = fila.Campos } }],
            new Dictionary<int, FilaCruda> { [2] = fila },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Con_el_mismo_origen_el_cache_se_restaura()
    {
        CrearCache(Hoja("hoja-a")).Reemplazar(UnRegistro());

        var restaurado = CrearCache(Hoja("hoja-a"));

        Assert.Single(restaurado.Actual.Registros);
        Assert.Equal("1/2026", restaurado.Actual.Registros[0].Campos.Consecutivo);
    }

    [Fact]
    public void Con_otro_spreadsheet_el_cache_arranca_vacio()
    {
        CrearCache(Hoja("hoja-a")).Reemplazar(UnRegistro());

        var restaurado = CrearCache(Hoja("hoja-b"));

        Assert.Empty(restaurado.Actual.Registros);
        Assert.Null(restaurado.Actual.UltimaSync);
    }

    [Fact]
    public void Con_otras_columnas_el_cache_arranca_vacio()
    {
        CrearCache(Hoja("hoja-a")).Reemplazar(UnRegistro());

        var restaurado = CrearCache(Hoja("hoja-a", circuito: "F"));

        Assert.Empty(restaurado.Actual.Registros);
    }

    [Fact]
    public void Con_el_mismo_origen_las_marcas_de_impreso_se_conservan()
    {
        CrearImpresos(Hoja("hoja-a")).Marcar(5, "firma");

        var restaurado = CrearImpresos(Hoja("hoja-a"));

        Assert.True(restaurado.Actual.ContainsKey(5));
    }

    [Fact]
    public void Con_otro_spreadsheet_las_marcas_de_impreso_se_descartan()
    {
        CrearImpresos(Hoja("hoja-a")).Marcar(5, "firma");

        var restaurado = CrearImpresos(Hoja("hoja-b"));

        Assert.Empty(restaurado.Actual);
    }

    [Fact]
    public void Un_archivo_de_impresos_del_formato_anterior_se_descarta()
    {
        // Antes se guardaba el diccionario pelado, sin origen: es de la hoja vieja.
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(RutaImpresos,
            """{"5":{"firma":"x","fecha":"2026-09-20T10:00:00+00:00"}}""");

        var restaurado = CrearImpresos(Hoja("hoja-a"));

        Assert.Empty(restaurado.Actual);
    }

    [Fact]
    public void El_rango_de_una_columna_entrecomilla_la_pestana()
    {
        var opciones = new GoogleSheetsOptions { Hoja = "S-da" };

        Assert.Equal("'S-da'!B:B", opciones.RangoDe(new ColumnaOptions { Nombre = "x", Columna = "b" }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }
    }
}
