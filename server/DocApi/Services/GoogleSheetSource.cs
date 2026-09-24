using DocApi.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Lee la hoja real vía las APIs de Google, siempre en modo solo lectura.
/// Drive se usa exclusivamente para el chequeo barato de <c>modifiedTime</c>.
/// </summary>
public sealed class GoogleSheetSource : ISheetSource, IDisposable
{
    private readonly GoogleSheetsOptions _options;
    private readonly ILogger<GoogleSheetSource> _logger;
    private readonly SheetsService _sheets;
    private readonly DriveService _drive;
    private readonly IReadOnlyList<ColumnaOptions> _columnas;

    public bool EstaConfigurado => true;

    public GoogleSheetSource(IOptions<GoogleSheetsOptions> options, ILogger<GoogleSheetSource> logger)
    {
        _options = options.Value;
        _logger = logger;
        _columnas = _options.ColumnasValidas;

        if (_columnas.Count == 0)
        {
            throw new InvalidOperationException(
                "GoogleSheets:Columnas está vacío: define al menos una columna (Nombre + Columna).");
        }

        var duplicadas = _columnas.GroupBy(c => c.Nombre.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicadas.Length > 0)
        {
            throw new InvalidOperationException(
                $"GoogleSheets:Columnas tiene nombres repetidos: {string.Join(", ", duplicadas)}. " +
                "Cada columna debe tener un nombre distinto, porque es el marcador de la plantilla.");
        }

        var credential = CrearCredencial(_options)
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly, DriveService.Scope.DriveReadonly);

        var init = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DocumentosAbogados"
        };

        _sheets = new SheetsService(init);
        _drive = new DriveService(init);

        _logger.LogInformation(
            "Google Sheets configurado. Spreadsheet {Id}, pestaña '{Hoja}'. Columnas: {Columnas}.",
            _options.SpreadsheetId, _options.Hoja,
            string.Join(", ", _columnas.Select(c => $"{c.Nombre} <- {_options.RangoDe(c)}")));
    }

    private static GoogleCredential CrearCredencial(GoogleSheetsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            return GoogleCredential.FromJson(options.ServiceAccountJson);
        }

        var ruta = options.ServiceAccountJsonPath!;
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException(
                $"No se encontró el JSON de service account en '{ruta}'.", ruta);
        }

        return GoogleCredential.FromFile(ruta);
    }

    public async Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct)
    {
        var request = _drive.Files.Get(_options.SpreadsheetId);
        request.Fields = "modifiedTime";
        request.SupportsAllDrives = true;

        var archivo = await request.ExecuteAsync(ct).ConfigureAwait(false);
        return archivo.ModifiedTimeDateTimeOffset;
    }

    public async Task<IReadOnlyDictionary<int, FilaCruda>> LeerFilasAsync(CancellationToken ct)
    {
        // Un único batchGet trae todas las columnas: una sola llamada a Sheets por sync.
        var rangos = _columnas.Select(_options.RangoDe).ToList();

        var request = _sheets.Spreadsheets.Values.BatchGet(_options.SpreadsheetId);
        request.Ranges = rangos;
        request.MajorDimension =
            SpreadsheetsResource.ValuesResource.BatchGetRequest.MajorDimensionEnum.ROWS;

        var respuesta = await request.ExecuteAsync(ct).ConfigureAwait(false);

        // La API devuelve los ValueRange en el mismo orden en que se pidieron.
        var devueltos = respuesta.ValueRanges ?? [];
        if (devueltos.Count != rangos.Count)
        {
            throw new InvalidOperationException(
                $"Sheets devolvió {devueltos.Count} rangos para los {rangos.Count} pedidos " +
                $"({string.Join(", ", rangos)}). Revisa que la pestaña '{_options.Hoja}' exista.");
        }

        var columnas = _columnas
            .Select((c, i) => (Nombre: c.Nombre.Trim(), Valores: AColumna(devueltos[i].Values, rangos[i])))
            .ToArray();

        // Una fila existe si cualquiera de sus columnas tiene algo: una celda vacía no la
        // descarta, el campo queda vacío y el generador avisa del marcador sin valor.
        var filas = columnas.SelectMany(c => c.Valores.Keys).Distinct().Order();

        var resultado = new Dictionary<int, FilaCruda>();
        foreach (var fila in filas)
        {
            var campos = new Dictionary<string, string>(columnas.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var (nombre, valores) in columnas)
            {
                campos[nombre] = valores.TryGetValue(fila, out var v) ? v : string.Empty;
            }

            resultado[fila] = new FilaCruda(campos);
        }

        return resultado;
    }

    /// <summary>
    /// Convierte un <c>ValueRange</c> de una columna en un diccionario fila → texto. El
    /// índice de fila es 1-based y coincide con lo que el usuario ve en la hoja, porque
    /// todos los rangos son columnas completas que arrancan en la fila 1.
    /// </summary>
    private Dictionary<int, string> AColumna(IList<IList<object>>? filas, string rango)
    {
        var resultado = new Dictionary<int, string>();
        if (filas is null)
        {
            _logger.LogWarning("El rango {Rango} no devolvió valores.", rango);
            return resultado;
        }

        var primeraFila = _options.TieneEncabezado ? 1 : 0;
        for (var i = primeraFila; i < filas.Count; i++)
        {
            var celda = filas[i].Count > 0 ? filas[i][0]?.ToString()?.Trim() : null;
            if (!string.IsNullOrEmpty(celda))
            {
                resultado[i + 1] = celda;
            }
        }

        return resultado;
    }

    public void Dispose()
    {
        _sheets.Dispose();
        _drive.Dispose();
    }
}
