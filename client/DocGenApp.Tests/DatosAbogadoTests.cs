using DocGenApp.Models;
using DocGenApp.ViewModels;
using Xunit;

namespace DocGenApp.Tests;

public class DatosAbogadoTests
{
    private static RegistroDto Registro() => new()
    {
        Fila = 12,
        ValorCrudo = "644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO",
        Campos = new ParsedFields
        {
            Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["consecutivo"] = "644/2026",
                ["colegio"] = "TERCER COLEGIADO",
                ["circuito"] = "DECIMOPRIMER CIRCUITO",
                ["nombre"] = "JUAN PEREZ LOPEZ"
            }
        }
    };

    private static DatosAbogado Abogado() => new()
    {
        Nombre = "ANA GARCIA",
        UsuarioFirel = "agarcia01",
        CedulaProfesional = "1234567"
    };

    [Fact]
    public void Los_datos_del_abogado_se_suman_a_los_campos_de_la_hoja()
    {
        var vm = new RecordDetailViewModel(Registro());

        var valores = vm.ValoresParaPlantilla(Abogado());

        Assert.Equal("644/2026", valores["consecutivo"]);
        Assert.Equal("JUAN PEREZ LOPEZ", valores["nombre"]);
        Assert.Equal("ANA GARCIA", valores["abogadoNombre"]);
        Assert.Equal("agarcia01", valores["abogadoFirel"]);
        Assert.Equal("1234567", valores["abogadoCedula"]);
        Assert.Equal("12", valores["fila"]);
    }

    [Fact]
    public void Los_datos_del_abogado_se_recortan_al_pasarlos_a_la_plantilla()
    {
        var abogado = new DatosAbogado { Nombre = "  ANA GARCIA  ", UsuarioFirel = " x ", CedulaProfesional = " 1 " };

        var valores = abogado.ValoresParaPlantilla();

        Assert.Equal("ANA GARCIA", valores["abogadoNombre"]);
        Assert.Equal("x", valores["abogadoFirel"]);
        Assert.Equal("1", valores["abogadoCedula"]);
    }

    [Theory]
    [InlineData("", "x", "1", false)]
    [InlineData("ANA", "", "1", false)]
    [InlineData("ANA", "x", "   ", false)]
    [InlineData("ANA", "x", "1", true)]
    public void EstaCompleto_exige_los_tres_campos(string nombre, string firel, string cedula, bool esperado)
    {
        var datos = new DatosAbogado { Nombre = nombre, UsuarioFirel = firel, CedulaProfesional = cedula };

        Assert.Equal(esperado, datos.EstaCompleto);
    }

    [Fact]
    public void El_nombre_encabeza_la_lista_y_el_resto_baja_al_subtitulo()
    {
        var vm = new RecordDetailViewModel(Registro());

        Assert.Equal("JUAN PEREZ LOPEZ", vm.Titulo);
        Assert.Equal("644/2026 · TERCER COLEGIADO · DECIMOPRIMER CIRCUITO", vm.Subtitulo);
    }

    [Fact]
    public void Sin_nombre_el_titulo_cae_al_consecutivo_y_no_lo_repite_abajo()
    {
        var registro = Registro();
        registro.Campos.Grupos.Remove("nombre");

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("644/2026", vm.Titulo);
        Assert.Equal("TERCER COLEGIADO · DECIMOPRIMER CIRCUITO", vm.Subtitulo);
    }

    [Fact]
    public void Sin_nombre_ni_consecutivo_el_titulo_identifica_por_fila()
    {
        var registro = new RegistroDto
        {
            Fila = 9,
            ValorCrudo = "algo raro",
            Campos = new ParsedFields { Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) }
        };

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("Fila 9", vm.Titulo);
        Assert.Equal("algo raro", vm.Subtitulo);
    }

    [Fact]
    public void Un_registro_sin_circuito_muestra_solo_lo_que_tiene()
    {
        var registro = new RegistroDto
        {
            Fila = 3,
            ValorCrudo = "777/2026 SEGUNDO TRIBUNAL UNITARIO",
            PatronUsado = 1,
            Campos = new ParsedFields
            {
                Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["consecutivo"] = "777/2026",
                    ["colegio"] = "SEGUNDO TRIBUNAL UNITARIO",
                    ["nombre"] = "MARIA RUIZ"
                }
            }
        };

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("MARIA RUIZ", vm.Titulo);
        Assert.Equal("777/2026 · SEGUNDO TRIBUNAL UNITARIO", vm.Subtitulo);

        // Sin circuito el marcador ni siquiera se ofrece: el generador lo deja visible
        // en el documento y lo reporta, en vez de sustituirlo por un hueco en blanco.
        Assert.False(vm.ValoresParaPlantilla(Abogado()).ContainsKey("circuito"));
    }

    [Theory]
    [InlineData("juan", true)]      // por el nombre de la columna extra
    [InlineData("COLEGIADO", true)] // por el valor crudo
    [InlineData("12", true)]        // por el número de fila
    [InlineData("zzz", false)]
    public void El_filtro_busca_en_valor_crudo_nombre_y_fila(string termino, bool esperado)
    {
        var vm = new RecordDetailViewModel(Registro());

        Assert.Equal(esperado, vm.Coincide(termino));
    }
}
