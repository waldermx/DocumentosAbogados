using System.Text.RegularExpressions;
using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Aplica los regex configurados a los valores crudos de la columna principal, probándolos
/// en orden hasta que uno coincide. Los patrones se compilan una sola vez al arrancar; una
/// fila que no matchea ninguno se reporta como error y no interrumpe el procesamiento del
/// resto (RF-05).
/// </summary>
public sealed class RowParser
{
    private readonly Regex[] _regexes;
    private readonly ILogger<RowParser> _logger;

    /// <summary>
    /// Unión de los nombres de grupo declarados en todos los patrones (ignora los numéricos).
    /// Es la unión y no la intersección porque cada patrón puede extraer campos distintos:
    /// los que su match no llene simplemente no aparecen en el registro.
    /// </summary>
    public IReadOnlyList<string> NombresDeGrupo { get; }

    /// <summary>Los patrones realmente activos, en orden. Lo expone <c>GET /diagnostico</c>.</summary>
    public IReadOnlyList<string> Patrones { get; }

    /// <summary>Palabras de estado que mandan una fila sin match al descarte.</summary>
    public IReadOnlyList<string> PalabrasDescarte { get; }

    /// <summary>
    /// Alternancia de las palabras de descarte con <c>\b</c> a los lados: se busca la palabra
    /// entera, para que "pago" no se dispare dentro de "pagos" ni "espera" dentro de "esperar".
    /// <c>null</c> si no hay ninguna configurada (descarte desactivado).
    /// </summary>
    private readonly Regex? _descarte;

    public RowParser(IOptions<ParsingOptions> options, ILogger<RowParser> logger)
    {
        _logger = logger;

        var patrones = options.Value.PatronesEfectivos
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        if (patrones.Length == 0)
        {
            throw new InvalidOperationException(
                "Parsing:Regexes no puede estar vacío. Configúralo en appsettings.json o vía Parsing__Regexes__0.");
        }

        _regexes = patrones.Select(Compilar).ToArray();
        Patrones = patrones;

        NombresDeGrupo = _regexes
            .SelectMany(r => r.GetGroupNames())
            .Where(n => !int.TryParse(n, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        PalabrasDescarte = options.Value.PalabrasDescarteEfectivas
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();

        _descarte = PalabrasDescarte.Count == 0
            ? null
            : new Regex(
                $@"\b(?:{string.Join("|", PalabrasDescarte.Select(Regex.Escape))})\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        _logger.LogInformation(
            "RowParser listo. {Total} patrón(es), se prueban en orden: {Patrones}. Grupos: {Grupos}. " +
            "Palabras de descarte: {Descarte}",
            _regexes.Length,
            string.Join(" | ", patrones),
            NombresDeGrupo.Count > 0 ? string.Join(", ", NombresDeGrupo) : "(ninguno con nombre)",
            PalabrasDescarte.Count > 0 ? string.Join(", ", PalabrasDescarte) : "(ninguna)");
    }

    private static Regex Compilar(string patron)
    {
        try
        {
            return new Regex(patron, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Parsing:Regexes contiene un patrón que no es .NET válido: {patron}", ex);
        }
    }

    /// <summary>
    /// Parsea una fila. Devuelve <c>true</c> y llena <paramref name="registro"/> si algún patrón
    /// matchea; devuelve <c>false</c> y llena <paramref name="error"/> si ninguno lo hace.
    /// Las columnas extra se copian tal cual, matchee el patrón que matchee, y tienen prioridad
    /// sobre un grupo del regex que se llame igual (una columna propia es un dato explícito).
    /// </summary>
    public bool TryParse(int fila, FilaCruda filaCruda, out RegistroDto? registro, out ErrorParseoDto? error)
    {
        var valor = (filaCruda.Valor ?? string.Empty).Trim();

        if (valor.Length == 0)
        {
            registro = null;
            error = new ErrorParseoDto { Fila = fila, ValorCrudo = valor, Motivo = "Celda vacía" };
            return false;
        }

        var (match, indice) = PrimerMatch(valor);
        if (match is null)
        {
            registro = null;

            // Una fila que no matchea pero trae una palabra de estado ("PENDIENTE",
            // "CANCELADO", ...) no es algo que corregir: es una anotación de la hoja.
            // Se marca como descarte para que el cliente la saque de la lista de pendientes.
            var palabra = _descarte?.Match(valor);
            error = palabra is { Success: true }
                ? new ErrorParseoDto
                {
                    Fila = fila,
                    ValorCrudo = valor,
                    Motivo = $"Descartada: es una anotación de estado ({palabra.Value})",
                    Descartada = true
                }
                : new ErrorParseoDto
                {
                    Fila = fila,
                    ValorCrudo = valor,
                    Motivo = _regexes.Length == 1
                        ? "El valor no coincide con el patrón configurado"
                        : $"El valor no coincide con ninguno de los {_regexes.Length} patrones configurados"
                };
            return false;
        }

        var grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nombre in _regexes[indice].GetGroupNames())
        {
            if (int.TryParse(nombre, out _))
            {
                continue;
            }

            var g = match.Groups[nombre];
            if (g.Success)
            {
                grupos[nombre] = g.Value.Trim();
            }
        }

        foreach (var (nombre, contenido) in filaCruda.Extra)
        {
            grupos[nombre] = (contenido ?? string.Empty).Trim();
        }

        registro = new RegistroDto
        {
            Fila = fila,
            ValorCrudo = valor,
            PatronUsado = indice,
            Campos = new ParsedFields { Grupos = grupos }
        };
        error = null;
        return true;
    }

    private (Match? match, int indice) PrimerMatch(string valor)
    {
        for (var i = 0; i < _regexes.Length; i++)
        {
            var match = _regexes[i].Match(valor);
            if (match.Success)
            {
                return (match, i);
            }
        }

        return (null, -1);
    }
}
