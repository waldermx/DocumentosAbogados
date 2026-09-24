namespace DocApi.Models;

/// <summary>Una fila de la hoja con los valores de las columnas configuradas.</summary>
public sealed class RegistroDto
{
    /// <summary>Número de fila en la hoja (1-based), usado también como id en la API.</summary>
    public required int Fila { get; init; }

    public required ParsedFields Campos { get; init; }
}

/// <summary>Payload de <c>GET /registros</c>.</summary>
public sealed class RegistrosResponseDto
{
    public required IReadOnlyList<RegistroDto> Registros { get; init; }
    public required DateTimeOffset? UltimaSync { get; init; }

    /// <summary>Fila → cuándo se marcó como impresa. Solo aparecen las filas marcadas.</summary>
    public required IReadOnlyDictionary<int, DateTimeOffset> Impresos { get; init; }
}

/// <summary>Payload de <c>DELETE /registros/impresos</c>.</summary>
public sealed class ImpresosLimpiadosDto
{
    /// <summary>Cuántas filas dejaron de estar marcadas.</summary>
    public required int Desmarcados { get; init; }
}

/// <summary>Payload de <c>POST/DELETE /registros/{id}/impreso</c>: el estado resultante.</summary>
public sealed class ImpresoEstadoDto
{
    public required int Fila { get; init; }

    /// <summary><c>null</c> si la fila no está (o dejó de estar) marcada como impresa.</summary>
    public required DateTimeOffset? Impreso { get; init; }
}
