namespace DocApi.Models;

public sealed class GoogleSheetsOptions
{
    public const string Section = "GoogleSheets";

    /// <summary>Ruta al JSON de service account. Alternativa: <see cref="ServiceAccountJson"/> con el contenido inline.</summary>
    public string? ServiceAccountJsonPath { get; set; }

    /// <summary>Contenido del JSON de service account (útil en Docker: env var GoogleSheets__ServiceAccountJson).</summary>
    public string? ServiceAccountJson { get; set; }

    public string? SpreadsheetId { get; set; }

    /// <summary>Nombre de la pestaña de la hoja de cálculo, tal como aparece abajo en Google Sheets.</summary>
    public string Hoja { get; set; } = "Hoja1";

    /// <summary>Si la primera fila de la hoja es encabezado y debe ignorarse.</summary>
    public bool TieneEncabezado { get; set; } = true;

    /// <summary>
    /// Columnas que se leen de la hoja. Cada valor se copia tal cual, sin interpretarlo,
    /// y queda disponible como marcador <c>{{Nombre}}</c> en la plantilla.
    /// </summary>
    public List<ColumnaOptions> Columnas { get; set; } = [];

    public IReadOnlyList<ColumnaOptions> ColumnasValidas => Columnas.Where(c => c.EsValida).ToArray();

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(SpreadsheetId) &&
        (!string.IsNullOrWhiteSpace(ServiceAccountJson) || !string.IsNullOrWhiteSpace(ServiceAccountJsonPath));

    /// <summary>Rango A1 de una columna completa de la pestaña configurada, p. ej. <c>'Hoja1'!B:B</c>.</summary>
    public string RangoDe(ColumnaOptions columna)
    {
        // Entre comillas siempre: así funcionan pestañas con espacios o guiones ('S-da').
        var hoja = Hoja.Replace("'", "''");
        var letra = columna.Columna.Trim().ToUpperInvariant();
        return $"'{hoja}'!{letra}:{letra}";
    }

    /// <summary>
    /// Identifica de dónde salen los datos. El caché y las marcas de impreso lo guardan
    /// junto con su contenido: si se cambia de hoja o de columnas, lo persistido describe
    /// otras filas y se descarta al arrancar en vez de mezclarse con los datos nuevos.
    /// </summary>
    public string Origen =>
        string.Join('|', ColumnasValidas
            .Select(c => $"{c.Nombre.Trim().ToLowerInvariant()}={c.Columna.Trim().ToUpperInvariant()}")
            .Prepend(Hoja)
            .Prepend(SpreadsheetId ?? string.Empty));
}

/// <summary>Una columna de la hoja: el nombre del campo y la letra de donde sale.</summary>
public sealed class ColumnaOptions
{
    /// <summary>Nombre del campo; se convierte en el marcador <c>{{Nombre}}</c>.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Letra de la columna, p. ej. <c>B</c>.</summary>
    public string Columna { get; set; } = string.Empty;

    public bool EsValida =>
        !string.IsNullOrWhiteSpace(Nombre) &&
        !string.IsNullOrWhiteSpace(Columna) &&
        Columna.Trim().All(char.IsAsciiLetter);
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

    /// <summary>Archivo donde se persiste el caché para sobrevivir reinicios. Vacío = solo memoria.</summary>
    public string? CacheFilePath { get; set; } = "cache/sheet-cache.json";

    /// <summary>Archivo donde se persisten las marcas de "impreso". Vacío = solo memoria.</summary>
    public string? ImpresosFilePath { get; set; } = "cache/impresos.json";
}
