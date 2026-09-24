using DocGenApp.Models;
using DocGenApp.ViewModels;
using Xunit;

namespace DocGenApp.Tests;

public class DatosAbogadoTests
{
    private static RegistroDto Registro() => new()
    {
        Fila = 12,
        Campos = new ParsedFields
        {
            Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["consecutivo"] = "644/2026",
                ["circuito"] = "Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito",
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
    public void Un_solo_abogado_llena_los_marcadores_sin_numerar_y_tambien_el_numero_1()
    {
        var vm = new RecordDetailViewModel(Registro());

        var valores = vm.ValoresParaPlantilla([Abogado()]);

        Assert.Equal("644/2026", valores["consecutivo"]);
        Assert.Equal("JUAN PEREZ LOPEZ", valores["nombre"]);
        Assert.Equal("ANA GARCIA", valores["abogadoNombre"]);
        Assert.Equal("agarcia01", valores["abogadoFirel"]);
        Assert.Equal("1234567", valores["abogadoCedula"]);
        Assert.Equal("ANA GARCIA", valores["abogado1Nombre"]);
        Assert.Equal("agarcia01", valores["abogado1Firel"]);
        Assert.Equal("1234567", valores["abogado1Cedula"]);
        Assert.Equal("12", valores["fila"]);
    }

    [Fact]
    public void Dos_abogados_generan_marcadores_independientes_por_numero()
    {
        var uno = Abogado();
        var dos = new DatosAbogado { Nombre = "LUIS TORRES", UsuarioFirel = "ltorres02", CedulaProfesional = "9876543" };
        var vm = new RecordDetailViewModel(Registro());

        var valores = vm.ValoresParaPlantilla([uno, dos]);

        Assert.Equal("ANA GARCIA", valores["abogado1Nombre"]);
        Assert.Equal("LUIS TORRES", valores["abogado2Nombre"]);
        Assert.Equal("ltorres02", valores["abogado2Firel"]);
        Assert.Equal("9876543", valores["abogado2Cedula"]);

        // Los sin numerar solo los llena el primero: no tendría sentido que el segundo
        // pisara silenciosamente al primero en un marcador ambiguo.
        Assert.Equal("ANA GARCIA", valores["abogadoNombre"]);
    }

    [Fact]
    public void Sin_ningun_abogado_los_marcadores_no_se_agregan_al_diccionario()
    {
        var vm = new RecordDetailViewModel(Registro());

        var valores = vm.ValoresParaPlantilla([]);

        Assert.False(valores.ContainsKey("abogadoNombre"));
        Assert.False(valores.ContainsKey("abogado1Nombre"));
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

    [Fact]
    public void Los_marcadores_numerados_tambien_se_recortan()
    {
        var abogado = new DatosAbogado { Nombre = "  ANA GARCIA  ", UsuarioFirel = " x ", CedulaProfesional = " 1 " };

        var valores = abogado.ValoresParaPlantilla(2);

        Assert.Equal("ANA GARCIA", valores["abogado2Nombre"]);
        Assert.Equal("x", valores["abogado2Firel"]);
        Assert.Equal("1", valores["abogado2Cedula"]);
        Assert.False(valores.ContainsKey("abogadoNombre"));
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
        Assert.Equal("644/2026 · Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito", vm.Subtitulo);
    }

    [Fact]
    public void Sin_nombre_el_titulo_cae_al_consecutivo_y_no_lo_repite_abajo()
    {
        var registro = Registro();
        registro.Campos.Grupos.Remove("nombre");

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("644/2026", vm.Titulo);
        Assert.Equal("Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito", vm.Subtitulo);
    }

    [Fact]
    public void Sin_nombre_ni_consecutivo_el_titulo_identifica_por_fila()
    {
        var registro = new RegistroDto
        {
            Fila = 9,
            Campos = new ParsedFields { Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) }
        };

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("Fila 9", vm.Titulo);
        Assert.Equal(string.Empty, vm.Subtitulo);
    }

    [Fact]
    public void Un_registro_sin_circuito_muestra_solo_lo_que_tiene()
    {
        var registro = new RegistroDto
        {
            Fila = 3,
            Campos = new ParsedFields
            {
                Grupos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["consecutivo"] = "777/2026",
                    ["nombre"] = "MARIA RUIZ"
                }
            }
        };

        var vm = new RecordDetailViewModel(registro);

        Assert.Equal("MARIA RUIZ", vm.Titulo);
        Assert.Equal("777/2026", vm.Subtitulo);

        // Sin circuito el marcador ni siquiera se ofrece: el generador lo deja visible
        // en el documento y lo reporta, en vez de sustituirlo por un hueco en blanco.
        Assert.False(vm.ValoresParaPlantilla([Abogado()]).ContainsKey("circuito"));
    }

    [Theory]
    [InlineData("juan", true)]      // por el nombre de la columna extra
    [InlineData("colegiado", true)] // por el circuito
    [InlineData("644", true)]       // por el número de amparo
    [InlineData("12", true)]        // por el número de fila
    [InlineData("zzz", false)]
    public void El_filtro_busca_en_todas_las_columnas_y_en_la_fila(string termino, bool esperado)
    {
        var vm = new RecordDetailViewModel(Registro());

        Assert.Equal(esperado, vm.Coincide(termino));
    }
}
