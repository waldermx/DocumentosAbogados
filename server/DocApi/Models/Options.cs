namespace DocApi.Models;

public sealed class GoogleSheetsOptions
{
    public const string Section = "GoogleSheets";

    /// <summary>Ruta al JSON de service account. Alternativa: <see cref="ServiceAccountJson"/> con el contenido inline.</summary>
    public string? ServiceAccountJsonPath { get; set; }

    /// <summary>Contenido del JSON de service account (útil en Docker: env var GoogleSheets__ServiceAccountJson).</summary>
    public string? ServiceAccountJson { get; set; }

    public string? SpreadsheetId { get; set; }

    /// <summary>Rango en notación A1, p. ej. <c>Hoja1!C:C</c>. Solo se lee esta columna.</summary>
    public string Range { get; set; } = "Hoja1!C:C";

    /// <summary>Nombre lógico de la columna, solo informativo/para logs.</summary>
    public string SourceColumnName { get; set; } = "Asunto";

    /// <summary>Si la primera celda del rango es encabezado y debe ignorarse.</summary>
    public bool TieneEncabezado { get; set; } = true;

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(SpreadsheetId) &&
        (!string.IsNullOrWhiteSpace(ServiceAccountJson) || !string.IsNullOrWhiteSpace(ServiceAccountJsonPath));
}

public sealed class ParsingOptions
{
    public const string Section = "Parsing";

    public string Regex { get; set; } =
        @"^(?<consecutivo>\d+/\d{4})\s+(?<colegio>.+?)\s+DEL\s+(?<circuito>.+)$";
}

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Se configura por env var <c>Auth__MasterPassword</c>. Nunca en el repo.</summary>
    public string? MasterPassword { get; set; }
}

public sealed class SyncOptions
{
    public const string Section = "Sync";

    public int PollingIntervalMinutes { get; set; } = 5;

    /// <summary>Archivo donde se persiste el caché para sobrevivir reinicios. Vacío = solo memoria.</summary>
    public string? CacheFilePath { get; set; } = "cache/sheet-cache.json";
}
