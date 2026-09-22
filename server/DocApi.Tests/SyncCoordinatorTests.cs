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
        public Dictionary<int, FilaCruda> Valores { get; set; } = new();

        public int LlamadasModifiedTime { get; private set; }
        public int LlamadasLeerColumna { get; private set; }

        public Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct)
        {
            LlamadasModifiedTime++;
            return Task.FromResult(ModifiedTime);
        }

        public Task<IReadOnlyDictionary<int, FilaCruda>> LeerFilasAsync(CancellationToken ct)
        {
            LlamadasLeerColumna++;
            return Task.FromResult<IReadOnlyDictionary<int, FilaCruda>>(
                new Dictionary<int, FilaCruda>(Valores));
        }
    }

    private static (SyncCoordinator coordinator, FakeSheetSource source, SheetCache cache, ImpresosStore impresos) Crear(
        Dictionary<int, FilaCruda> valoresIniciales)
    {
        var source = new FakeSheetSource { Valores = valoresIniciales };
        var parser = new RowParser(Options.Create(new ParsingOptions()), NullLogger<RowParser>.Instance);

        // CacheFilePath/ImpresosFilePath vacíos = solo memoria, sin tocar disco durante los tests.
        var cache = new SheetCache(
            Options.Create(new SyncOptions { CacheFilePath = null }),
            NullLogger<SheetCache>.Instance);
        var impresos = new ImpresosStore(
            Options.Create(new SyncOptions { ImpresosFilePath = null }),
            NullLogger<ImpresosStore>.Instance);

        var coordinator = new SyncCoordinator(source, parser, cache, impresos, NullLogger<SyncCoordinator>.Instance);
        return (coordinator, source, cache, impresos);
    }

    private static Dictionary<int, FilaCruda> TresFilas() => new()
    {
        [2] = FilaCruda.Simple("644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO"),
        [3] = FilaCruda.Simple("12/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO"),
        [4] = FilaCruda.Simple("99/2024 QUINTO COLEGIADO DEL TERCER CIRCUITO")
    };

    private static FilaCruda ConNombre(string valor, string nombre) =>
        new(valor, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["nombre"] = nombre });

    [Fact]
    public async Task Primera_sync_parsea_todo()
    {
        var (coordinator, source, cache, _) = Crear(TresFilas());

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
        var (coordinator, source, _, _) = Crear(TresFilas());
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
        var (coordinator, source, cache, _) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        // Se edita una sola fila y avanza modifiedTime.
        source.Valores[3] = FilaCruda.Simple("13/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO");
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
        var (coordinator, source, cache, _) = Crear(TresFilas());
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
        valores[5] = FilaCruda.Simple("texto sin formato");
        var (coordinator, _, cache, _) = Crear(valores);

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(3, resultado.TotalRegistros);
        Assert.Equal(1, resultado.TotalErroresParseo);
        Assert.Equal(5, cache.Actual.ErroresParseo.Single().Fila);
    }

    [Fact]
    public async Task Una_columna_extra_editada_invalida_la_fila()
    {
        var valores = TresFilas();
        valores[2] = ConNombre("644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO", "JUAN PEREZ");
        var (coordinator, source, cache, _) = Crear(valores);
        await coordinator.SyncAsync(false, CancellationToken.None);

        // Cambia solo el nombre: la columna principal es idéntica, pero la fila debe reparsearse.
        source.Valores[2] = ConNombre("644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO", "ANA LOPEZ");
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);

        var segunda = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(1, segunda.FilasReparseadas);
        Assert.Equal(2, segunda.FilasReutilizadas);
        Assert.Equal("ANA LOPEZ", cache.Actual.Registros.Single(r => r.Fila == 2).Campos.Grupos["nombre"]);
    }

    [Fact]
    public async Task Una_fila_sin_circuito_cae_en_el_patron_laxo_en_vez_de_ir_a_errores()
    {
        var valores = TresFilas();
        valores[5] = FilaCruda.Simple("777/2026 SEGUNDO TRIBUNAL UNITARIO");
        var (coordinator, _, cache, _) = Crear(valores);

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(4, resultado.TotalRegistros);
        Assert.Equal(0, resultado.TotalErroresParseo);

        var registro = cache.Actual.Registros.Single(r => r.Fila == 5);
        Assert.Equal(1, registro.PatronUsado); // el segundo patrón, el de respaldo
        Assert.Equal("777/2026", registro.Campos.Consecutivo);
        Assert.Equal("SEGUNDO TRIBUNAL UNITARIO", registro.Campos.Colegio);
        Assert.Equal(string.Empty, registro.Campos.Circuito);
    }

    [Fact]
    public async Task Syncs_concurrentes_se_coalescen_en_una_sola_lectura()
    {
        var (coordinator, source, _, _) = Crear(TresFilas());

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
        var impresos = new ImpresosStore(
            Options.Create(new SyncOptions { ImpresosFilePath = null }),
            NullLogger<ImpresosStore>.Instance);
        var coordinator = new SyncCoordinator(
            new SheetSourceNoConfigurado(), parser, cache, impresos, NullLogger<SyncCoordinator>.Instance);

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Error, resultado.Resultado);
        Assert.Contains("no está configurado", resultado.Mensaje);
    }

    [Fact]
    public async Task Editar_una_fila_impresa_le_quita_la_marca()
    {
        var (coordinator, source, _, impresos) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        impresos.Marcar(3, source.Valores[3].Firma);
        Assert.True(impresos.Actual.ContainsKey(3));

        // Se edita esa misma fila, aunque sea con buscar y reemplazar: el texto cambia,
        // la firma ya no coincide, y la marca de impreso debe caerse.
        source.Valores[3] = FilaCruda.Simple("13/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO");
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);
        await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.False(impresos.Actual.ContainsKey(3));
    }

    [Fact]
    public async Task Eliminar_una_fila_impresa_le_quita_la_marca()
    {
        var (coordinator, source, _, impresos) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        impresos.Marcar(4, source.Valores[4].Firma);

        source.Valores.Remove(4);
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);
        await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.False(impresos.Actual.ContainsKey(4));
    }

    [Fact]
    public async Task Una_fila_impresa_sin_cambios_conserva_la_marca()
    {
        var (coordinator, source, _, impresos) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        impresos.Marcar(2, source.Valores[2].Firma);

        // Otra fila cambia, pero la marcada como impresa no.
        source.Valores[3] = FilaCruda.Simple("13/2025 PRIMER COLEGIADO DEL SEGUNDO CIRCUITO");
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);
        await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.True(impresos.Actual.ContainsKey(2));
    }
}
