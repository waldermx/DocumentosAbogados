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

    /// <summary>
    /// Columnas adicionales de la misma hoja que se leen tal cual (sin pasar por el regex)
    /// y se unen a cada registro por número de fila. Cada una queda disponible como
    /// marcador <c>{{nombre}}</c> en la plantilla.
    /// </summary>
    public List<ColumnaExtraOptions> ColumnasExtra { get; set; } = [];

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(SpreadsheetId) &&
        (!string.IsNullOrWhiteSpace(ServiceAccountJson) || !string.IsNullOrWhiteSpace(ServiceAccountJsonPath));
}

/// <summary>Una columna extra: el nombre del campo y el rango A1 de donde sale.</summary>
public sealed class ColumnaExtraOptions
{
    /// <summary>Nombre del campo; se convierte en el marcador <c>{{Nombre}}</c>.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Rango A1 de la columna, p. ej. <c>Hoja1!D:D</c>. Debe arrancar en la misma fila
    /// que <see cref="GoogleSheetsOptions.Range"/> para que las filas se correspondan.
    /// </summary>
    public string Range { get; set; } = string.Empty;

    public bool EsValida =>
        !string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(Range);
}

public sealed class ParsingOptions
{
    public const string Section = "Parsing";

    /// <summary>
    /// Patrones que se prueban <b>en orden</b> contra cada celda: gana el primero que coincida.
    /// Permite convivir variantes del texto (con circuito al final, sin él, con otra cola)
    /// sin que ninguna quede fuera. Vacío = <see cref="PatronesPorDefecto"/>.
    /// </summary>
    public List<string> Regexes { get; set; } = [];

    /// <summary>Compatibilidad: un único patrón. Solo se usa si <see cref="Regexes"/> está vacío.</summary>
    public string? Regex { get; set; }

    /// <summary>
    /// Del más específico al más laxo. El primero es el formato canónico
    /// (<c>… DEL &lt;circuito&gt;</c>); el segundo recoge todo lo demás — sin circuito,
    /// con otra cola o con nada detrás — dejando <c>circuito</c> vacío en vez de
    /// mandar la fila a errores de parseo.
    /// </summary>
    public static readonly string[] PatronesPorDefecto =
    [
        @"^(?<consecutivo>\d+/\d{4})\s+(?<colegio>.+?)\s+DEL\s+(?<circuito>.+)$",
        @"^(?<consecutivo>\d+/\d{4})\s+(?<colegio>.+)$"
    ];

    /// <summary>Los patrones efectivos, ya resueltos según lo configurado.</summary>
    public IReadOnlyList<string> PatronesEfectivos =>
        Regexes.Count > 0 ? Regexes
        : !string.IsNullOrWhiteSpace(Regex) ? [Regex]
        : PatronesPorDefecto;
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
