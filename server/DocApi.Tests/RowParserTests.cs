using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

public class RowParserTests
{
    private static RowParser CrearParser(string? regex = null) =>
        new(Options.Create(new ParsingOptions
            {
                Regex = regex ?? new ParsingOptions().Regex
            }),
            NullLogger<RowParser>.Instance);

    [Fact]
    public void Parsea_el_ejemplo_real()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(2, "644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO",
            out var registro, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(registro);
        Assert.Equal(2, registro!.Fila);
        Assert.Equal("644/2026", registro.Campos.Consecutivo);
        Assert.Equal("TERCER COLEGIADO", registro.Campos.Colegio);
        Assert.Equal("DECIMOPRIMER CIRCUITO", registro.Campos.Circuito);
    }

    [Fact]
    public void Un_valor_que_no_matchea_se_reporta_como_error_sin_excepcion()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(7, "esto no tiene el formato esperado", out var registro, out var error);

        Assert.False(ok);
        Assert.Null(registro);
        Assert.NotNull(error);
        Assert.Equal(7, error!.Fila);
        Assert.Equal("esto no tiene el formato esperado", error.ValorCrudo);
    }

    [Fact]
    public void Una_celda_vacia_se_reporta_como_error_de_celda_vacia()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(3, "   ", out _, out var error);

        Assert.False(ok);
        Assert.Equal("Celda vacía", error!.Motivo);
    }

    [Fact]
    public void Los_nombres_de_grupo_vienen_del_patron_configurado()
    {
        // El regex es configurable: con otros grupos, el diccionario los refleja
        // sin necesidad de tocar código.
        var parser = CrearParser(@"^(?<expediente>[A-Z]-\d+)\s+(?<materia>.+)$");

        var ok = parser.TryParse(1, "A-123 AMPARO DIRECTO", out var registro, out _);

        Assert.True(ok);
        Assert.Equal("A-123", registro!.Campos.Grupos["expediente"]);
        Assert.Equal("AMPARO DIRECTO", registro.Campos.Grupos["materia"]);
        Assert.Equal(string.Empty, registro.Campos.Consecutivo);
    }

    [Fact]
    public void Un_regex_invalido_falla_al_arrancar_con_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrearParser("(?<sin cerrar"));
        Assert.Contains("no es un patrón .NET válido", ex.Message);
    }
}
