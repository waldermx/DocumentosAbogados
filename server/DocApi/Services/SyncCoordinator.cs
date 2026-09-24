using System.Diagnostics;
using DocApi.Models;

namespace DocApi.Services;

/// <summary>
/// Orquesta la sincronización, que solo se dispara a mano (<c>POST /sync</c>), con single-flight:
///   1. chequeo barato de <c>modifiedTime</c> en Drive — si no cambió, corta ahí
///      (salvo que se pida lectura forzada, que es lo que hace el cliente);
///   2. una sola lectura de las columnas configuradas vía Sheets;
///   3. los valores se copian tal cual a los registros y se reemplaza el caché.
///
/// El <see cref="SemaphoreSlim"/> garantiza que varias solicitudes concurrentes
/// (dos clientes pulsando «Sincronizar» a la vez) no disparen varias lecturas: las
/// demás esperan y obtienen el resultado de la que corrió.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly ISheetSource _source;
    private readonly SheetCache _cache;
    private readonly ImpresosStore _impresos;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SyncResult? _ultimoResultado;

    public SyncCoordinator(
        ISheetSource source,
        SheetCache cache,
        ImpresosStore impresos,
        ILogger<SyncCoordinator> logger)
    {
        _source = source;
        _cache = cache;
        _impresos = impresos;
        _logger = logger;
    }

    public SyncResult? UltimoResultado => _ultimoResultado;

    /// <summary>
    /// Ejecuta una sync. Si ya hay una en curso, espera a que termine y devuelve su resultado
    /// en vez de lanzar una segunda (single-flight).
    /// </summary>
    public async Task<SyncResult> SyncAsync(bool forzarLecturaCompleta, CancellationToken ct)
    {
        // Si el semáforo está tomado, otra sync está corriendo ahora mismo.
        var yaHabiaOtraEnCurso = _gate.CurrentCount == 0;
        var esperaDesde = Stopwatch.GetTimestamp();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (yaHabiaOtraEnCurso && _ultimoResultado is not null && !forzarLecturaCompleta)
            {
                _logger.LogInformation(
                    "Sync coalescida: otra sync ya estaba en curso (esperó {Ms} ms), se reutiliza su resultado.",
                    Stopwatch.GetElapsedTime(esperaDesde).TotalMilliseconds);
                return _ultimoResultado;
            }

            var resultado = await EjecutarAsync(forzarLecturaCompleta, ct).ConfigureAwait(false);
            _ultimoResultado = resultado;
            return resultado;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncResult> EjecutarAsync(bool forzarLecturaCompleta, CancellationToken ct)
    {
        var anterior = _cache.Actual;

        if (!_source.EstaConfigurado)
        {
            _logger.LogWarning("Sync omitida: {Mensaje}", SheetSourceNoConfigurado.Mensaje);
            return new SyncResult
            {
                Resultado = SyncOutcome.Error,
                UltimaSync = anterior.UltimaSync,
                TotalRegistros = anterior.Registros.Count,
                Mensaje = SheetSourceNoConfigurado.Mensaje
            };
        }

        try
        {
            // --- Paso 1: chequeo barato ---
            var modifiedTime = await _source.GetModifiedTimeAsync(ct).ConfigureAwait(false);

            if (!forzarLecturaCompleta &&
                modifiedTime is not null &&
                anterior.UltimoModifiedTime is not null &&
                modifiedTime == anterior.UltimoModifiedTime)
            {
                _logger.LogInformation(
                    "Paso 1: la hoja no cambió (modifiedTime {ModifiedTime}). No se lee Sheets.",
                    modifiedTime);

                return new SyncResult
                {
                    Resultado = SyncOutcome.SinCambios,
                    UltimaSync = anterior.UltimaSync,
                    TotalRegistros = anterior.Registros.Count
                };
            }

            // --- Paso 2: lectura de las columnas configuradas ---
            var filas = await _source.LeerFilasAsync(ct).ConfigureAwait(false);

            // --- Paso 3: los valores van tal cual; no hay nada que interpretar ---
            var registros = filas
                .OrderBy(kv => kv.Key)
                .Select(kv => new RegistroDto
                {
                    Fila = kv.Key,
                    Campos = new ParsedFields { Grupos = kv.Value.Campos }
                })
                .ToList();

            var ahora = DateTimeOffset.UtcNow;

            // Filas marcadas como impresas cuyo contenido cambió (o que desaparecieron)
            // dejan de estarlo: la marca ya no describe lo que hay en la hoja ahora.
            _impresos.Reconciliar(filas);

            _cache.Reemplazar(new SheetCache.Snapshot(
                registros,
                filas.ToDictionary(kv => kv.Key, kv => kv.Value),
                ahora,
                modifiedTime ?? anterior.UltimoModifiedTime));

            _logger.LogInformation("Sync completa: {Total} registros.", registros.Count);

            return new SyncResult
            {
                Resultado = SyncOutcome.Actualizado,
                UltimaSync = ahora,
                TotalRegistros = registros.Count
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cuota excedida, credenciales inválidas, red caída, estructura inesperada (RNF-10).
            _logger.LogError(ex, "Falló la sincronización con Google Sheets. Se conserva el caché anterior.");

            return new SyncResult
            {
                Resultado = SyncOutcome.Error,
                UltimaSync = anterior.UltimaSync,
                TotalRegistros = anterior.Registros.Count,
                Mensaje = ex.Message
            };
        }
    }

    public void Dispose() => _gate.Dispose();
}
