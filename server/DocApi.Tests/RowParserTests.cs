using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

public class RowParserTests
{
    private static RowParser CrearParser(params string[] regexes) =>
        new(Options.Create(new ParsingOptions
            {
                Regexes = regexes.Length > 0 ? [.. regexes] : []
            }),
            NullLogger<RowParser>.Instance);

    private static FilaCruda Fila(string valor) => FilaCruda.Simple(valor);

    [Fact]
    public void Parsea_el_ejemplo_real()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(2, Fila("644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO"),
            out var registro, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(registro);
        Assert.Equal(2, registro!.Fila);
        Assert.Equal(0, registro.PatronUsado);
        Assert.Equal("644/2026", registro.Campos.Consecutivo);
        Assert.Equal("TERCER COLEGIADO", registro.Campos.Colegio);
        Assert.Equal("DECIMOPRIMER CIRCUITO", registro.Campos.Circuito);
    }

    [Theory]
    // Sin nada detrás del colegio.
    [InlineData("133/2025 CUARTO COLEGIADO", "CUARTO COLEGIADO")]
    [InlineData("644/2026 TERCER COLEGIADO", "TERCER COLEGIADO")]
    // Con otra cola que no es "DEL <circuito>".
    [InlineData("12/2025 PRIMER TRIBUNAL UNITARIO EN MATERIA PENAL", "PRIMER TRIBUNAL UNITARIO EN MATERIA PENAL")]
    [InlineData("99/2024 JUZGADO SEGUNDO DE DISTRITO (AUXILIAR)", "JUZGADO SEGUNDO DE DISTRITO (AUXILIAR)")]
    public void Una_fila_sin_circuito_la_recoge_el_patron_de_respaldo(string valor, string colegioEsperado)
    {
        var parser = CrearParser();

        var ok = parser.TryParse(4, Fila(valor), out var registro, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(1, registro!.PatronUsado);
        Assert.Equal(colegioEsperado, registro.Campos.Colegio);
        Assert.Equal(string.Empty, registro.Campos.Circuito);
    }

    [Fact]
    public void Gana_el_primer_patron_que_coincide()
    {
        // El patrón canónico va primero, así que una fila con "DEL <circuito>"
        // no debe caer en el de respaldo aunque este también la aceptaría.
        var parser = CrearParser();

        parser.TryParse(1, Fila("5/2026 CUARTO COLEGIADO DEL PRIMER CIRCUITO"), out var registro, out _);

        Assert.Equal(0, registro!.PatronUsado);
        Assert.Equal("CUARTO COLEGIADO", registro.Campos.Colegio);
    }

    [Fact]
    public void Un_valor_que_no_matchea_ningun_patron_se_reporta_como_error_sin_excepcion()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(7, Fila("esto no tiene el formato esperado"), out var registro, out var error);

        Assert.False(ok);
        Assert.Null(registro);
        Assert.NotNull(error);
        Assert.Equal(7, error!.Fila);
        Assert.Equal("esto no tiene el formato esperado", error.ValorCrudo);
        Assert.Contains("ninguno de los 2 patrones", error.Motivo);
    }

    [Fact]
    public void Una_celda_vacia_se_reporta_como_error_de_celda_vacia()
    {
        var parser = CrearParser();

        var ok = parser.TryParse(3, Fila("   "), out _, out var error);

        Assert.False(ok);
        Assert.Equal("Celda vacía", error!.Motivo);
    }

    [Fact]
    public void Las_columnas_extra_se_copian_al_registro()
    {
        var parser = CrearParser();
        var fila = new FilaCruda(
            "644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO",
            new Dictionary<string, string> { ["nombre"] = "  JUAN PEREZ LOPEZ  " });

        var ok = parser.TryParse(2, fila, out var registro, out _);

        Assert.True(ok);
        Assert.Equal("JUAN PEREZ LOPEZ", registro!.Campos.Nombre);
        // Los grupos del regex siguen ahí: la columna extra se suma, no sustituye.
        Assert.Equal("644/2026", registro.Campos.Consecutivo);
    }

    [Fact]
    public void Una_columna_extra_gana_sobre_un_grupo_del_regex_con_el_mismo_nombre()
    {
        var parser = CrearParser(@"^(?<consecutivo>\d+/\d{4})\s+(?<nombre>.+)$");
        var fila = new FilaCruda(
            "644/2026 TERCER COLEGIADO",
            new Dictionary<string, string> { ["nombre"] = "JUAN PEREZ" });

        parser.TryParse(2, fila, out var registro, out _);

        Assert.Equal("JUAN PEREZ", registro!.Campos.Nombre);
    }

    [Fact]
    public void Los_nombres_de_grupo_vienen_de_los_patrones_configurados()
    {
        // Los regex son configurables: con otros grupos, el diccionario los refleja
        // sin necesidad de tocar código.
        var parser = CrearParser(@"^(?<expediente>[A-Z]-\d+)\s+(?<materia>.+)$");

        var ok = parser.TryParse(1, Fila("A-123 AMPARO DIRECTO"), out var registro, out _);

        Assert.True(ok);
        Assert.Equal("A-123", registro!.Campos.Grupos["expediente"]);
        Assert.Equal("AMPARO DIRECTO", registro.Campos.Grupos["materia"]);
        Assert.Equal(string.Empty, registro.Campos.Consecutivo);
    }

    [Fact]
    public void Un_regex_invalido_falla_al_arrancar_con_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrearParser("(?<sin cerrar"));
        Assert.Contains("no es .NET válido", ex.Message);
    }

    [Fact]
    public void Un_solo_Regex_heredado_sigue_funcionando()
    {
        // Compatibilidad con la configuración anterior: Parsing:Regex en singular.
        var parser = new RowParser(
            Options.Create(new ParsingOptions { Regex = @"^(?<expediente>[A-Z]-\d+)$" }),
            NullLogger<RowParser>.Instance);

        var ok = parser.TryParse(1, Fila("A-123"), out var registro, out _);

        Assert.True(ok);
        Assert.Equal("A-123", registro!.Campos.Grupos["expediente"]);
    }
}
