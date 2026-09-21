using System.Text.RegularExpressions;
using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Aplica el regex configurado a los valores crudos de la columna.
/// El patrón se compila una sola vez al arrancar; una fila que no matchea
/// se reporta como error y no interrumpe el procesamiento del resto (RF-05).
/// </summary>
public sealed class RowParser
{
    private readonly Regex _regex;
    private readonly ILogger<RowParser> _logger;

    /// <summary>Nombres de grupo con nombre declarados en el patrón (ignora los numéricos).</summary>
    public IReadOnlyList<string> NombresDeGrupo { get; }

    public RowParser(IOptions<ParsingOptions> options, ILogger<RowParser> logger)
    {
        _logger = logger;

        var patron = options.Value.Regex;
        if (string.IsNullOrWhiteSpace(patron))
        {
            throw new InvalidOperationException(
                "Parsing:Regex no puede estar vacío. Configúralo en appsettings.json o vía Parsing__Regex.");
        }

        try
        {
            _regex = new Regex(patron, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Parsing:Regex no es un patrón .NET válido: {patron}", ex);
        }

        NombresDeGrupo = _regex.GetGroupNames()
            .Where(n => !int.TryParse(n, out _))
            .ToArray();

        _logger.LogInformation(
            "RowParser listo. Patrón: {Patron}. Grupos: {Grupos}",
            patron,
            NombresDeGrupo.Count > 0 ? string.Join(", ", NombresDeGrupo) : "(ninguno con nombre)");
    }

    /// <summary>
    /// Parsea una fila. Devuelve <c>true</c> y llena <paramref name="registro"/> si matchea;
    /// devuelve <c>false</c> y llena <paramref name="error"/> si no.
    /// </summary>
    public bool TryParse(int fila, string valorCrudo, out RegistroDto? registro, out ErrorParseoDto? error)
    {
        var valor = (valorCrudo ?? string.Empty).Trim();

        if (valor.Length == 0)
        {
            registro = null;
            error = new ErrorParseoDto { Fila = fila, ValorCrudo = valorCrudo ?? string.Empty, Motivo = "Celda vacía" };
            return false;
        }

        var match = _regex.Match(valor);
        if (!match.Success)
        {
            registro = null;
            error = new ErrorParseoDto
            {
                Fila = fila,
                ValorCrudo = valor,
                Motivo = "El valor no coincide con el patrón configurado"
            };
            return false;
        }

        var grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nombre in NombresDeGrupo)
        {
            var g = match.Groups[nombre];
            if (g.Success)
            {
                grupos[nombre] = g.Value.Trim();
            }
        }

        registro = new RegistroDto
        {
            Fila = fila,
            ValorCrudo = valor,
            Campos = new ParsedFields { Grupos = grupos }
        };
        error = null;
        return true;
    }
}
