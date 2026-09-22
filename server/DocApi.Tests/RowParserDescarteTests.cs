using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

public class RowParserDescarteTests
{
    private static RowParser CrearParser(List<string>? palabras = null) =>
        new(Options.Create(new ParsingOptions { PalabrasDescarte = palabras ?? [] }),
            NullLogger<RowParser>.Instance);

    private static FilaCruda Fila(string valor) => FilaCruda.Simple(valor);

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("ELABORADO")]
    [InlineData("ESPERA")]
    [InlineData("FIRMAS")]
    [InlineData("SENTENCIA")]
    [InlineData("PRESENTADO")]
    [InlineData("PAGO")]
    [InlineData("CANCELADO")]
    [InlineData("ENVIADO")]
    public void Las_palabras_de_estado_mandan_la_fila_al_descarte(string palabra)
    {
        var parser = CrearParser();

        var ok = parser.TryParse(7, Fila(palabra), out var registro, out var error);

        Assert.False(ok);
        Assert.Null(registro);
        Assert.True(error!.Descartada);
    }

    [Theory]
    [InlineData("pendiente")]
    [InlineData("Cancelado")]
    [InlineData("eNvIaDo")]
    public void No_distingue_mayusculas_de_minusculas(string valor)
    {
        var parser = CrearParser();

        parser.TryParse(7, Fila(valor), out _, out var error);

        Assert.True(error!.Descartada);
    }

    [Fact]
    public void La_palabra_puede_ir_dentro_de_una_frase()
    {
        var parser = CrearParser();

        parser.TryParse(7, Fila("EN ESPERA DE ACUERDO"), out _, out var error);

        Assert.True(error!.Descartada);
        Assert.Contains("ESPERA", error.Motivo);
    }

    [Fact]
    public void Una_fila_que_no_matchea_y_no_trae_palabra_de_estado_sigue_siendo_error()
    {
        // Es justo la que el usuario sí quiere ver: hay algo que limpiar en la hoja.
        var parser = CrearParser();

        parser.TryParse(7, Fila("ESTO NO SE PARECE A NADA"), out _, out var error);

        Assert.False(error!.Descartada);
        Assert.Contains("no coincide", error.Motivo);
    }

    [Fact]
    public void Un_registro_valido_no_se_descarta_aunque_traiga_una_palabra_de_estado()
    {
        // El descarte solo mira las filas que ningún patrón reconoció: si la fila es un
        // amparo bueno, sigue siendo un registro aunque la celda mencione un estado.
        var parser = CrearParser();

        var ok = parser.TryParse(7, Fila("644/2026 TERCER COLEGIADO DEL PAGO CIRCUITO"),
            out var registro, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("644/2026", registro!.Campos.Consecutivo);
    }

    [Fact]
    public void Se_busca_la_palabra_entera_no_un_trozo()
    {
        // "pago" no debe dispararse dentro de "PAGOTE", que sí es una fila a revisar.
        var parser = CrearParser();

        parser.TryParse(7, Fila("PAGOTE SIN SENTIDO"), out _, out var error);

        Assert.False(error!.Descartada);
    }

    [Fact]
    public void La_celda_vacia_sigue_reportandose_como_error_no_como_descarte()
    {
        var parser = CrearParser();

        parser.TryParse(7, Fila("   "), out _, out var error);

        Assert.False(error!.Descartada);
        Assert.Equal("Celda vacía", error.Motivo);
    }

    [Fact]
    public void Las_palabras_se_pueden_configurar()
    {
        var parser = CrearParser(["archivado"]);

        parser.TryParse(7, Fila("ARCHIVADO"), out _, out var archivado);
        parser.TryParse(8, Fila("PENDIENTE"), out _, out var pendiente);

        Assert.True(archivado!.Descartada);
        // Al configurar la lista se reemplaza entera: las de por defecto dejan de aplicar.
        Assert.False(pendiente!.Descartada);
    }

    [Fact]
    public void Con_la_lista_en_blanco_no_se_descarta_nada()
    {
        var parser = CrearParser([" "]);

        parser.TryParse(7, Fila("PENDIENTE"), out _, out var error);

        Assert.False(error!.Descartada);
    }
}
