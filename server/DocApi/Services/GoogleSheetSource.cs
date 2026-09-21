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
    private readonly ColumnaExtraOptions[] _extras;

    public bool EstaConfigurado => true;

    public GoogleSheetSource(IOptions<GoogleSheetsOptions> options, ILogger<GoogleSheetSource> logger)
    {
        _options = options.Value;
        _logger = logger;
        _extras = _options.ColumnasExtra.Where(c => c.EsValida).ToArray();

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
            "Google Sheets configurado. Spreadsheet {Id}, rango {Rango} (columna '{Columna}'). Columnas extra: {Extras}.",
            _options.SpreadsheetId, _options.Range, _options.SourceColumnName,
            _extras.Length > 0
                ? string.Join(", ", _extras.Select(c => $"{c.Nombre} <- {c.Range}"))
                : "(ninguna)");

        var duplicadas = _extras.GroupBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicadas.Length > 0)
        {
            throw new InvalidOperationException(
                $"GoogleSheets:ColumnasExtra tiene nombres repetidos: {string.Join(", ", duplicadas)}. " +
                "Cada columna extra debe tener un nombre distinto, porque es el marcador de la plantilla.");
        }
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
        // Un único batchGet trae la columna principal y las extra: sigue siendo una sola
        // llamada a Sheets por sync, igual que cuando solo se leía una columna.
        var rangos = new List<string> { _options.Range };
        rangos.AddRange(_extras.Select(c => c.Range));

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
                $"({string.Join(", ", rangos)}). Revisa que todos existan en la hoja.");
        }

        var principal = AColumna(devueltos[0].Values, _options.Range);
        var columnasExtra = _extras
            .Select((c, i) => (c.Nombre, Valores: AColumna(devueltos[i + 1].Values, c.Range)))
            .ToArray();

        var resultado = new Dictionary<int, FilaCruda>(principal.Count);
        foreach (var (fila, valor) in principal)
        {
            var extra = new Dictionary<string, string>(columnasExtra.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var (nombre, valores) in columnasExtra)
            {
                // Una celda vacía en la columna extra no descarta la fila: el campo
                // queda vacío y el generador avisa de que el marcador se quedó sin valor.
                extra[nombre] = valores.TryGetValue(fila, out var v) ? v : string.Empty;
            }

            resultado[fila] = new FilaCruda(valor, extra);
        }

        return resultado;
    }

    /// <summary>
    /// Convierte un <c>ValueRange</c> de una columna en un diccionario fila → texto.
    /// El índice de fila es 1-based y relativo al inicio del rango, de modo que coincide
    /// con lo que el usuario ve en la hoja cuando el rango arranca en la fila 1
    /// (p. ej. <c>Hoja1!C:C</c>). Por eso todos los rangos deben arrancar en la misma fila.
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
            var celda = filas[i].Count > 0 ? filas[i][0]?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(celda))
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
