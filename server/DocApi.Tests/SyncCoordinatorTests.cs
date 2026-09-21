using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

public class SyncCoordinatorTests
{
    /// <summary>Origen falso que cuenta llamadas, para poder afirmar qué se evitó.</summary>
    private sealed class FakeSheetSource : ISheetSource
    {
        public bool EstaConfigurado => true;
        public DateTimeOffset? ModifiedTime { get; set; } = DateTimeOffset.UnixEpoch;
        public Dictionary<int, string> Valores { get; set; } = new();

        public int LlamadasModifiedTime { get; private set; }
        public int LlamadasLeerColumna { get; private set; }

        public Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct)
        {
            LlamadasModifiedTime++;
            return Task.FromResult(ModifiedTime);
        }

        public Task<IReadOnlyDictionary<int, string>> LeerColumnaAsync(CancellationToken ct)
        {
            LlamadasLeerColumna++;
            return Task.FromResult<IReadOnlyDictionary<int, string>>(
                new Dictionary<int, string>(Valores));
        }
    }

    private static (SyncCoordinator coordinator, FakeSheetSource source, SheetCache cache) Crear(
        Dictionary<int, string> valoresIniciales)
    {
        var source = new FakeSheetSource { Valores = valoresIniciales };
        var parser = new RowParser(Options.Create(new ParsingOptions()), NullLogger<RowParser>.Instance);

        // CacheFilePath vacío = solo memoria, sin tocar disco durante los tests.
        var cache = new SheetCache(
            Options.Create(new SyncOptions { CacheFilePath = null }),
            NullLogger<SheetCache>.Instance);

        var coordinator = new SyncCoordinator(source, parser, cache, NullLogger<SyncCoordinator>.Instance);
        return (coordinator, source, cache);
    }

    private static Dictionary<int, string> TresFilas() => new()
    {
        [2] = "644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO",
        [3] = "12/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO",
        [4] = "99/2024 QUINTO COLEGIADO DEL TERCER CIRCUITO"
    };

    [Fact]
    public async Task Primera_sync_parsea_todo()
    {
        var (coordinator, source, cache) = Crear(TresFilas());

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Actualizado, resultado.Resultado);
        Assert.Equal(3, resultado.TotalRegistros);
        Assert.Equal(3, resultado.FilasReparseadas);
        Assert.Equal(0, resultado.FilasReutilizadas);
        Assert.Equal(1, source.LlamadasLeerColumna);
        Assert.Equal(3, cache.Actual.Registros.Count);
    }

    [Fact]
    public async Task Si_modifiedTime_no_cambio_no_se_lee_la_hoja()
    {
        var (coordinator, source, _) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        // Segunda sync sin tocar la hoja: debe cortarse en el paso 1.
        var segunda = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.SinCambios, segunda.Resultado);
        Assert.Equal(0, segunda.FilasReparseadas);
        Assert.Equal(1, source.LlamadasLeerColumna); // sigue siendo la de la primera sync
        Assert.Equal(2, source.LlamadasModifiedTime);
    }

    [Fact]
    public async Task Solo_la_fila_editada_vuelve_a_parsearse()
    {
        var (coordinator, source, cache) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        // Se edita una sola fila y avanza modifiedTime.
        source.Valores[3] = "13/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO";
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);

        var segunda = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Actualizado, segunda.Resultado);
        Assert.Equal(1, segunda.FilasReparseadas);
        Assert.Equal(2, segunda.FilasReutilizadas);
        Assert.Equal("13/2025", cache.Actual.Registros.Single(r => r.Fila == 3).Campos.Consecutivo);
    }

    [Fact]
    public async Task Una_fila_eliminada_desaparece_del_cache()
    {
        var (coordinator, source, cache) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        source.Valores.Remove(4);
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);

        await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(2, cache.Actual.Registros.Count);
        Assert.DoesNotContain(cache.Actual.Registros, r => r.Fila == 4);
    }

    [Fact]
    public async Task Una_fila_que_no_matchea_no_impide_parsear_el_resto()
    {
        var valores = TresFilas();
        valores[5] = "texto sin formato";
        var (coordinator, _, cache) = Crear(valores);

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(3, resultado.TotalRegistros);
        Assert.Equal(1, resultado.TotalErroresParseo);
        Assert.Equal(5, cache.Actual.ErroresParseo.Single().Fila);
    }

    [Fact]
    public async Task Syncs_concurrentes_se_coalescen_en_una_sola_lectura()
    {
        var (coordinator, source, _) = Crear(TresFilas());

        // Varias solicitudes a la vez (polling + botón manual) no deben disparar varias lecturas.
        var tareas = Enumerable.Range(0, 5)
            .Select(_ => coordinator.SyncAsync(false, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tareas);

        Assert.Equal(1, source.LlamadasLeerColumna);
    }

    [Fact]
    public async Task Sin_credenciales_devuelve_error_explicativo_en_vez_de_lanzar()
    {
        var parser = new RowParser(Options.Create(new ParsingOptions()), NullLogger<RowParser>.Instance);
        var cache = new SheetCache(
            Options.Create(new SyncOptions { CacheFilePath = null }),
            NullLogger<SheetCache>.Instance);
        var coordinator = new SyncCoordinator(
            new SheetSourceNoConfigurado(), parser, cache, NullLogger<SyncCoordinator>.Instance);

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Error, resultado.Resultado);
        Assert.Contains("no está configurado", resultado.Mensaje);
    }
}
