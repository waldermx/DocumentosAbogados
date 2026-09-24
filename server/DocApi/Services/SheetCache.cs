using System.Text.Json;
using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Estado cacheado de la hoja. Todas las escrituras vienen del <see cref="SyncCoordinator"/>,
/// que ya está serializado por su semáforo; las lecturas de los endpoints son concurrentes,
/// así que el estado se reemplaza de forma atómica por un snapshot inmutable.
/// </summary>
public sealed class SheetCache
{
    private readonly ILogger<SheetCache> _logger;
    private readonly string? _rutaArchivo;
    private readonly string _origen;
    private volatile Snapshot _actual = Snapshot.Vacio;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public SheetCache(
        IOptions<SyncOptions> options,
        IOptions<GoogleSheetsOptions> sheets,
        ILogger<SheetCache> logger)
    {
        _logger = logger;
        _origen = sheets.Value.Origen;
        var ruta = options.Value.CacheFilePath;
        _rutaArchivo = string.IsNullOrWhiteSpace(ruta) ? null : Path.GetFullPath(ruta);
        CargarDesdeDisco();
    }

    public Snapshot Actual => _actual;

    public void Reemplazar(Snapshot nuevo)
    {
        _actual = nuevo;
        GuardarEnDisco(nuevo);
    }

    /// <summary>Vista inmutable del caché. Se reemplaza entera en cada sync exitosa.</summary>
    public sealed record Snapshot(
        IReadOnlyList<RegistroDto> Registros,
        IReadOnlyDictionary<int, FilaCruda> ValoresRawPorFila,
        DateTimeOffset? UltimaSync,
        DateTimeOffset? UltimoModifiedTime)
    {
        public static readonly Snapshot Vacio = new(
            Array.Empty<RegistroDto>(),
            new Dictionary<int, FilaCruda>(),
            null,
            null);
    }

    // --- Persistencia opcional a JSON, para sobrevivir reinicios sin re-leer la hoja ---

    private sealed record EstadoPersistido(
        string? Origen,
        List<RegistroDto>? Registros,
        Dictionary<int, FilaCruda>? ValoresRawPorFila,
        DateTimeOffset? UltimaSync,
        DateTimeOffset? UltimoModifiedTime);

    private void CargarDesdeDisco()
    {
        if (_rutaArchivo is null || !File.Exists(_rutaArchivo))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_rutaArchivo);
            var estado = JsonSerializer.Deserialize<EstadoPersistido>(json, JsonOpts);
            if (estado?.Registros is null || estado.ValoresRawPorFila is null)
            {
                return;
            }

            // Otro spreadsheet, otra pestaña u otras columnas: las filas guardadas no
            // corresponden a la hoja actual. Se arranca vacío hasta la próxima sync.
            if (!string.Equals(estado.Origen, _origen, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "El caché de {Ruta} es de otro origen de datos ('{Anterior}' vs '{Actual}'). Se descarta.",
                    _rutaArchivo, estado.Origen, _origen);
                return;
            }

            _actual = new Snapshot(
                estado.Registros,
                estado.ValoresRawPorFila,
                estado.UltimaSync,
                estado.UltimoModifiedTime);

            _logger.LogInformation(
                "Caché restaurado desde {Ruta}: {Total} registros, última sync {UltimaSync}.",
                _rutaArchivo, estado.Registros.Count, estado.UltimaSync);
        }
        catch (Exception ex)
        {
            // Un caché corrupto no debe impedir arrancar: se descarta y la primera sync lo rehace.
            _logger.LogWarning(ex, "No se pudo restaurar el caché desde {Ruta}. Se arranca vacío.", _rutaArchivo);
        }
    }

    private void GuardarEnDisco(Snapshot snapshot)
    {
        if (_rutaArchivo is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var estado = new EstadoPersistido(
                _origen,
                snapshot.Registros.ToList(),
                snapshot.ValoresRawPorFila.ToDictionary(kv => kv.Key, kv => kv.Value),
                snapshot.UltimaSync,
                snapshot.UltimoModifiedTime);

            // Escritura atómica: archivo temporal + move, para no dejar un JSON a medias.
            var temp = _rutaArchivo + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(estado, JsonOpts));
            File.Move(temp, _rutaArchivo, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo persistir el caché en {Ruta}.", _rutaArchivo);
        }
    }
}
