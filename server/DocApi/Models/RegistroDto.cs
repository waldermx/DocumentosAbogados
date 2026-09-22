namespace DocApi.Models;

/// <summary>Una fila de la columna configurada, ya parseada.</summary>
public sealed class RegistroDto
{
    /// <summary>Número de fila en la hoja (1-based), usado también como id en la API.</summary>
    public required int Fila { get; init; }

    public required string ValorCrudo { get; init; }

    /// <summary>
    /// Índice (0-based) del patrón de <c>Parsing:Regexes</c> que reconoció la fila.
    /// Sirve para ver de un vistazo qué filas cayeron en el patrón laxo.
    /// </summary>
    public int PatronUsado { get; init; }

    public required ParsedFields Campos { get; init; }
}

/// <summary>Una fila que no matcheó el regex configurado. No interrumpe el resto de la sync.</summary>
public sealed class ErrorParseoDto
{
    public required int Fila { get; init; }
    public required string ValorCrudo { get; init; }
    public required string Motivo { get; init; }
}

/// <summary>Payload de <c>GET /registros</c>.</summary>
public sealed class RegistrosResponseDto
{
    public required IReadOnlyList<RegistroDto> Registros { get; init; }
    public required IReadOnlyList<ErrorParseoDto> ErroresParseo { get; init; }
    public required DateTimeOffset? UltimaSync { get; init; }

    /// <summary>Fila → cuándo se marcó como impresa. Solo aparecen las filas marcadas.</summary>
    public required IReadOnlyDictionary<int, DateTimeOffset> Impresos { get; init; }
}

/// <summary>Payload de <c>POST/DELETE /registros/{id}/impreso</c>: el estado resultante.</summary>
public sealed class ImpresoEstadoDto
{
    public required int Fila { get; init; }

    /// <summary><c>null</c> si la fila no está (o dejó de estar) marcada como impresa.</summary>
    public required DateTimeOffset? Impreso { get; init; }
}
