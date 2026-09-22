using DocGenApp.Models;
using DocGenApp.ViewModels;
using Xunit;

namespace DocGenApp.Tests;

public class NombreArchivoSugeridoTests
{
    private static RecordDetailViewModel Registro(string? nombre, string? consecutivo, int fila = 12)
    {
        var grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (nombre is not null)
        {
            grupos["nombre"] = nombre;
        }
        if (consecutivo is not null)
        {
            grupos["consecutivo"] = consecutivo;
        }

        return new RecordDetailViewModel(new RegistroDto
        {
            Fila = fila,
            ValorCrudo = "crudo",
            Campos = new ParsedFields { Grupos = grupos }
        });
    }

    [Fact]
    public void Usa_el_nombre_del_quejoso_y_el_numero_de_amparo()
    {
        var vm = Registro("JUAN PEREZ LOPEZ", "644/2026");

        // La barra del consecutivo no es válida en un nombre de archivo de Windows.
        Assert.Equal("JUAN PEREZ LOPEZ - 644-2026.docx", vm.NombreArchivoSugerido());
    }

    [Fact]
    public void Sin_nombre_se_queda_solo_el_numero_de_amparo()
    {
        var vm = Registro(nombre: string.Empty, consecutivo: "644/2026");

        Assert.Equal("644-2026.docx", vm.NombreArchivoSugerido());
    }

    [Fact]
    public void Sin_numero_de_amparo_se_queda_solo_el_nombre()
    {
        var vm = Registro("JUAN PEREZ LOPEZ", consecutivo: null);

        Assert.Equal("JUAN PEREZ LOPEZ.docx", vm.NombreArchivoSugerido());
    }

    [Fact]
    public void Sin_ninguno_de_los_dos_se_cae_a_la_fila()
    {
        // Así el archivo sigue siendo único y rastreable hasta la hoja.
        var vm = Registro(nombre: null, consecutivo: null, fila: 37);

        Assert.Equal("registro-37.docx", vm.NombreArchivoSugerido());
    }

    [Fact]
    public void Limpia_los_caracteres_que_Windows_no_admite()
    {
        var vm = Registro("MARIA \"LA GUERA\" <SOTO>", "12/2026*");

        var nombre = vm.NombreArchivoSugerido();

        Assert.DoesNotContain('"', nombre);
        Assert.DoesNotContain('<', nombre);
        Assert.DoesNotContain('*', nombre);
        Assert.Equal("MARIA -LA GUERA- -SOTO- - 12-2026-.docx", nombre);
    }

    [Fact]
    public void No_deja_el_nombre_acabado_en_punto_ni_en_espacio()
    {
        // Windows rechaza "ALGO .docx" y "ALGO..docx" al guardar.
        var vm = Registro("JUAN PEREZ JR. ", consecutivo: null);

        Assert.Equal("JUAN PEREZ JR.docx", vm.NombreArchivoSugerido());
    }
}
