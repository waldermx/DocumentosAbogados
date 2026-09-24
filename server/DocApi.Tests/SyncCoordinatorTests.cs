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

    private static (SheetCache cache, ImpresosStore impresos) CrearAlmacenes()
    {
        var sheets = Options.Create(new GoogleSheetsOptions());

        // CacheFilePath/ImpresosFilePath vacíos = solo memoria, sin tocar disco durante los tests.
        var cache = new SheetCache(
            Options.Create(new SyncOptions { CacheFilePath = null }),
            sheets,
            NullLogger<SheetCache>.Instance);
        var impresos = new ImpresosStore(
            Options.Create(new SyncOptions { ImpresosFilePath = null }),
            sheets,
            NullLogger<ImpresosStore>.Instance);
        return (cache, impresos);
    }

    private static (SyncCoordinator coordinator, FakeSheetSource source, SheetCache cache, ImpresosStore impresos) Crear(
        Dictionary<int, FilaCruda> valoresIniciales)
    {
        var source = new FakeSheetSource { Valores = valoresIniciales };
        var (cache, impresos) = CrearAlmacenes();
        var coordinator = new SyncCoordinator(source, cache, impresos, NullLogger<SyncCoordinator>.Instance);
        return (coordinator, source, cache, impresos);
    }

    private static FilaCruda Fila(string consecutivo, string nombre, string circuito) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["consecutivo"] = consecutivo,
            ["nombre"] = nombre,
            ["circuito"] = circuito
        });

    private static Dictionary<int, FilaCruda> TresFilas() => new()
    {
        [2] = Fila("644/2026", "JUAN PEREZ", "Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito"),
        [3] = Fila("12/2025", "ANA LOPEZ", "Primer Tribunal Colegiado en Materia Administrativa del Segundo Circuito"),
        [4] = Fila("99/2024", "LUIS DIAZ", "Tercer Tribunal Colegiado en Materia Administrativa del Tercer Circuito")
    };

    [Fact]
    public async Task Los_valores_se_copian_tal_cual_sin_interpretarlos()
    {
        var (coordinator, _, cache, _) = Crear(TresFilas());

        var resultado = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Actualizado, resultado.Resultado);
        Assert.Equal(3, resultado.TotalRegistros);

        var registro = cache.Actual.Registros.Single(r => r.Fila == 2);
        Assert.Equal("644/2026", registro.Campos.Consecutivo);
        Assert.Equal("JUAN PEREZ", registro.Campos.Nombre);
        Assert.Equal(
            "Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito",
            registro.Campos.Circuito);
    }

    [Fact]
    public async Task Una_fila_con_una_columna_vacia_sigue_siendo_un_registro()
    {
        var valores = TresFilas();
        valores[5] = Fila("777/2026", string.Empty, string.Empty);
        var (coordinator, _, cache, _) = Crear(valores);

        await coordinator.SyncAsync(false, CancellationToken.None);

        var registro = cache.Actual.Registros.Single(r => r.Fila == 5);
        Assert.Equal("777/2026", registro.Campos.Consecutivo);
        Assert.Equal(string.Empty, registro.Campos.Circuito);
    }

    [Fact]
    public async Task Si_modifiedTime_no_cambio_no_se_lee_la_hoja()
    {
        var (coordinator, source, _, _) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        var segunda = await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.Equal(SyncOutcome.SinCambios, segunda.Resultado);
        Assert.Equal(1, source.LlamadasLeerColumna); // sigue siendo la de la primera sync
        Assert.Equal(2, source.LlamadasModifiedTime);
    }

    [Fact]
    public async Task La_sync_forzada_lee_la_hoja_aunque_modifiedTime_no_cambie()
    {
        // Es la que dispara el botón del cliente: ediciones en bloque en Sheets no
        // siempre mueven modifiedTime, y el usuario pulsó para ver datos frescos.
        var (coordinator, source, cache, _) = Crear(TresFilas());
        await coordinator.SyncAsync(false, CancellationToken.None);

        source.Valores[3] = Fila("13/2025", "ANA LOPEZ", "Otro circuito");
        var segunda = await coordinator.SyncAsync(true, CancellationToken.None);

        Assert.Equal(SyncOutcome.Actualizado, segunda.Resultado);
        Assert.Equal(2, source.LlamadasLeerColumna);
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
    public async Task Syncs_concurrentes_se_coalescen_en_una_sola_lectura()
    {
        var (coordinator, source, _, _) = Crear(TresFilas());

        var tareas = Enumerable.Range(0, 5)
            .Select(_ => coordinator.SyncAsync(false, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tareas);

        Assert.Equal(1, source.LlamadasLeerColumna);
    }

    [Fact]
    public async Task Sin_credenciales_devuelve_error_explicativo_en_vez_de_lanzar()
    {
        var (cache, impresos) = CrearAlmacenes();
        var coordinator = new SyncCoordinator(
            new SheetSourceNoConfigurado(), cache, impresos, NullLogger<SyncCoordinator>.Instance);

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

        // Cualquier columna de la fila cuenta: aquí cambia solo el circuito.
        source.Valores[3] = Fila("12/2025", "ANA LOPEZ", "Otro circuito");
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

        source.Valores[3] = Fila("13/2025", "ANA LOPEZ", "Otro circuito");
        source.ModifiedTime = source.ModifiedTime!.Value.AddMinutes(1);
        await coordinator.SyncAsync(false, CancellationToken.None);

        Assert.True(impresos.Actual.ContainsKey(2));
    }
}
