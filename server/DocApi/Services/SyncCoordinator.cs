using System.Diagnostics;
using DocApi.Models;

namespace DocApi.Services;

/// <summary>
/// Orquesta la sincronización en tres pasos, con single-flight:
///   1. chequeo barato de <c>modifiedTime</c> en Drive — si no cambió, corta ahí;
///   2. una sola lectura de la columna configurada vía Sheets;
///   3. diff por fila: solo las filas cuyo texto cambió vuelven a pasar por el regex.
///
/// El <see cref="SemaphoreSlim"/> garantiza que varias solicitudes concurrentes
/// (polling + botón manual) no disparen varias syncs: las demás esperan y obtienen
/// el resultado de la que corrió.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly ISheetSource _source;
    private readonly RowParser _parser;
    private readonly SheetCache _cache;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SyncResult? _ultimoResultado;

    public SyncCoordinator(
        ISheetSource source,
        RowParser parser,
        SheetCache cache,
        ILogger<SyncCoordinator> logger)
    {
        _source = source;
        _parser = parser;
        _cache = cache;
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
                TotalErroresParseo = anterior.ErroresParseo.Count,
                Mensaje = SheetSourceNoConfigurado.Mensaje
            };
        }

        try
        {
            // --- Paso 1: chequeo barato ---
            DateTimeOffset? modifiedTime = null;
            if (!forzarLecturaCompleta)
            {
                modifiedTime = await _source.GetModifiedTimeAsync(ct).ConfigureAwait(false);

                if (modifiedTime is not null &&
                    anterior.UltimoModifiedTime is not null &&
                    modifiedTime == anterior.UltimoModifiedTime)
                {
                    _logger.LogInformation(
                        "Paso 1: la hoja no cambió (modifiedTime {ModifiedTime}). No se lee Sheets ni se reparsea nada.",
                        modifiedTime);

                    return new SyncResult
                    {
                        Resultado = SyncOutcome.SinCambios,
                        UltimaSync = anterior.UltimaSync,
                        TotalRegistros = anterior.Registros.Count,
                        TotalErroresParseo = anterior.ErroresParseo.Count,
                        FilasReutilizadas = anterior.ValoresRawPorFila.Count
                    };
                }

                _logger.LogInformation(
                    "Paso 1: la hoja cambió (modifiedTime {Nuevo}, anterior {Anterior}). Se procede a leer.",
                    modifiedTime, anterior.UltimoModifiedTime);
            }
            else
            {
                _logger.LogInformation("Paso 1 omitido: se pidió lectura completa forzada.");
                modifiedTime = await _source.GetModifiedTimeAsync(ct).ConfigureAwait(false);
            }

            // --- Paso 2: lectura completa de la columna ---
            var valoresNuevos = await _source.LeerColumnaAsync(ct).ConfigureAwait(false);

            // --- Paso 3: diff por fila ---
            var registrosPorFila = anterior.Registros.ToDictionary(r => r.Fila);
            var erroresPorFila = anterior.ErroresParseo.ToDictionary(e => e.Fila);

            var registros = new List<RegistroDto>(valoresNuevos.Count);
            var errores = new List<ErrorParseoDto>();
            var reparseadas = 0;
            var reutilizadas = 0;

            foreach (var (fila, valor) in valoresNuevos.OrderBy(kv => kv.Key))
            {
                var sinCambios = !forzarLecturaCompleta
                    && anterior.ValoresRawPorFila.TryGetValue(fila, out var valorAnterior)
                    && string.Equals(valorAnterior, valor, StringComparison.Ordinal);

                if (sinCambios)
                {
                    // El texto es idéntico: se conserva el resultado ya calculado, sea registro o error.
                    if (registrosPorFila.TryGetValue(fila, out var registroPrevio))
                    {
                        registros.Add(registroPrevio);
                        reutilizadas++;
                        continue;
                    }

                    if (erroresPorFila.TryGetValue(fila, out var errorPrevio))
                    {
                        errores.Add(errorPrevio);
                        reutilizadas++;
                        continue;
                    }
                }

                if (_parser.TryParse(fila, valor, out var registro, out var error))
                {
                    registros.Add(registro!);
                }
                else
                {
                    errores.Add(error!);
                }

                reparseadas++;
            }

            var eliminadas = anterior.ValoresRawPorFila.Keys.Count(f => !valoresNuevos.ContainsKey(f));
            var ahora = DateTimeOffset.UtcNow;

            _cache.Reemplazar(new SheetCache.Snapshot(
                registros,
                errores,
                valoresNuevos.ToDictionary(kv => kv.Key, kv => kv.Value),
                ahora,
                modifiedTime ?? anterior.UltimoModifiedTime));

            _logger.LogInformation(
                "Sync completa: {Total} registros, {Errores} errores de parseo. " +
                "Filas reparseadas: {Reparseadas}, reutilizadas: {Reutilizadas}, eliminadas: {Eliminadas}.",
                registros.Count, errores.Count, reparseadas, reutilizadas, eliminadas);

            return new SyncResult
            {
                Resultado = SyncOutcome.Actualizado,
                UltimaSync = ahora,
                TotalRegistros = registros.Count,
                TotalErroresParseo = errores.Count,
                FilasReparseadas = reparseadas,
                FilasReutilizadas = reutilizadas
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
                TotalErroresParseo = anterior.ErroresParseo.Count,
                Mensaje = ex.Message
            };
        }
    }

    public void Dispose() => _gate.Dispose();
}
