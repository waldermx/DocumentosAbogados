using DocGenApp.Models;
using DocGenApp.Services;
using DocGenApp.ViewModels;
using Xunit;

namespace DocGenApp.Tests;

/// <summary>
/// Cubre solo la gestión de varios abogados en <see cref="MainViewModel"/>: alta, baja,
/// reordenamiento y que la numeración de sus marcadores siga la posición en la lista.
/// No hay "seleccionar uno": todos aplican a cada generación. No ejercita red ni
/// sincronización (<see cref="ApiClient"/> no llega a hacer ninguna llamada en estos tests).
/// </summary>
public sealed class MainViewModelAbogadosTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "DocGenAppTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* limpieza best-effort */ }
    }

    private MainViewModel Crear(AbogadosPersistidos? previo = null)
    {
        var store = new AbogadoStore(_carpeta);
        if (previo is not null)
        {
            store.Guardar(previo);
        }

        return new MainViewModel(
            new ApiClient(new AppSettings()),
            new LocalCacheService(),
            new CredentialStore(),
            store,
            new WordDocumentGenerator(),
            new AppSettings());
    }

    [Fact]
    public void Sin_abogados_guardados_arranca_con_la_lista_vacia_y_expandido()
    {
        var vm = Crear();

        Assert.Empty(vm.Abogados);
        Assert.Empty(vm.AbogadosParaMostrar);
        Assert.True(vm.AbogadoExpandido);
        Assert.False(vm.HayAlgunAbogado);
    }

    [Fact]
    public void Agregar_abogado_lo_suma_al_final_de_la_lista()
    {
        var vm = Crear();

        vm.AgregarAbogadoCommand.Execute(null);
        vm.AgregarAbogadoCommand.Execute(null);

        Assert.Equal(2, vm.Abogados.Count);
        Assert.True(vm.HayAlgunAbogado);

        // La vista de presentación numera según la posición.
        Assert.Equal(2, vm.AbogadosParaMostrar.Count);
        Assert.Equal(1, vm.AbogadosParaMostrar[0].Numero);
        Assert.Equal(2, vm.AbogadosParaMostrar[1].Numero);
        Assert.Same(vm.Abogados[0], vm.AbogadosParaMostrar[0].Datos);
        Assert.Same(vm.Abogados[1], vm.AbogadosParaMostrar[1].Datos);
    }

    [Fact]
    public void Eliminar_uno_no_afecta_a_los_demas_y_persiste_de_inmediato()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        var primero = vm.Abogados[0];
        vm.AgregarAbogadoCommand.Execute(null);
        var segundo = vm.Abogados[1];

        vm.EliminarAbogadoCommand.Execute(vm.AbogadosParaMostrar[0]); // borra al primero

        Assert.Single(vm.Abogados);
        Assert.Same(segundo, vm.Abogados[0]);
        Assert.DoesNotContain(primero, vm.Abogados);

        // Eliminar guarda de inmediato: un cierre accidental de la app no debe resucitar al borrado.
        var persistido = new AbogadoStore(_carpeta).Cargar();
        Assert.Single(persistido.Abogados);
        Assert.Equal(segundo.Id, persistido.Abogados[0].Id);
    }

    [Fact]
    public void Eliminar_el_ultimo_abogado_deja_la_lista_vacia_sin_lanzar()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);

        vm.EliminarAbogadoCommand.Execute(vm.AbogadosParaMostrar[0]);

        Assert.Empty(vm.Abogados);
        Assert.False(vm.HayAlgunAbogado);
    }

    [Fact]
    public void Eliminar_con_parametro_nulo_no_hace_nada()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);

        vm.EliminarAbogadoCommand.Execute(null);

        Assert.Single(vm.Abogados);
    }

    [Fact]
    public void Mover_arriba_y_abajo_cambia_el_numero_de_marcador()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        vm.Abogados[0].Nombre = "ANA GARCIA";
        vm.AgregarAbogadoCommand.Execute(null);
        vm.Abogados[1].Nombre = "LUIS TORRES";

        // Sube al segundo: pasa a ser abogado1.
        vm.MoverAbogadoArribaCommand.Execute(vm.AbogadosParaMostrar[1]);

        Assert.Equal("LUIS TORRES", vm.Abogados[0].Nombre);
        Assert.Equal("ANA GARCIA", vm.Abogados[1].Nombre);
        Assert.Equal(1, vm.AbogadosParaMostrar[0].Numero);
        Assert.Equal("LUIS TORRES", vm.AbogadosParaMostrar[0].Datos.Nombre);

        // Y de vuelta abajo.
        vm.MoverAbogadoAbajoCommand.Execute(vm.AbogadosParaMostrar[0]);

        Assert.Equal("ANA GARCIA", vm.Abogados[0].Nombre);
        Assert.Equal("LUIS TORRES", vm.Abogados[1].Nombre);
    }

    [Fact]
    public void Mover_el_primero_arriba_o_el_ultimo_abajo_no_hace_nada()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        vm.AgregarAbogadoCommand.Execute(null);
        var orden = vm.Abogados.ToList();

        vm.MoverAbogadoArribaCommand.Execute(vm.AbogadosParaMostrar[0]);
        vm.MoverAbogadoAbajoCommand.Execute(vm.AbogadosParaMostrar[1]);

        Assert.Equal(orden, vm.Abogados);
    }

    [Fact]
    public void Guardar_persiste_todos_los_abogados_en_orden()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        vm.Abogados[0].Nombre = "ANA GARCIA";
        vm.Abogados[0].UsuarioFirel = "agarcia01";
        vm.Abogados[0].CedulaProfesional = "1234567";
        vm.AgregarAbogadoCommand.Execute(null);
        vm.Abogados[1].Nombre = "LUIS TORRES";

        vm.GuardarAbogadosCommand.Execute(null);

        var persistido = new AbogadoStore(_carpeta).Cargar();
        Assert.Equal(2, persistido.Abogados.Count);
        Assert.Equal("ANA GARCIA", persistido.Abogados[0].Nombre);
        Assert.True(persistido.Abogados[0].EstaCompleto);
        Assert.Equal("LUIS TORRES", persistido.Abogados[1].Nombre);
    }

    [Fact]
    public void Arranca_con_todos_los_abogados_guardados_en_el_mismo_orden()
    {
        var uno = new DatosAbogado { Nombre = "ANA GARCIA", UsuarioFirel = "a", CedulaProfesional = "1" };
        var dos = new DatosAbogado { Nombre = "LUIS TORRES", UsuarioFirel = "b", CedulaProfesional = "2" };

        var vm = Crear(new AbogadosPersistidos { Abogados = [uno, dos] });

        Assert.Equal(2, vm.Abogados.Count);
        Assert.Equal("ANA GARCIA", vm.Abogados[0].Nombre);
        Assert.Equal("LUIS TORRES", vm.Abogados[1].Nombre);
        Assert.False(vm.AbogadoExpandido); // ambos completos: no hace falta abrir el panel
    }

    [Fact]
    public void El_resumen_distingue_sin_abogados_de_completos_e_incompletos()
    {
        var vm = Crear();
        Assert.Contains("No hay ningún abogado", vm.ResumenAbogado);

        vm.AgregarAbogadoCommand.Execute(null);
        vm.Abogados[0].Nombre = "ANA GARCIA";
        vm.Abogados[0].UsuarioFirel = "agarcia01";
        vm.Abogados[0].CedulaProfesional = "1234567";
        Assert.Contains("ANA GARCIA", vm.ResumenAbogado);
        Assert.DoesNotContain("incompleto", vm.ResumenAbogado);

        vm.AgregarAbogadoCommand.Execute(null); // el segundo queda vacío
        Assert.Contains("2 abogado(s)", vm.ResumenAbogado);
        Assert.Contains("1 con datos incompletos", vm.ResumenAbogado);
    }

    [Fact]
    public void El_resumen_se_actualiza_al_editar_un_campo_sin_agregar_ni_quitar()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);

        vm.Abogados[0].Nombre = "ANA GARCIA";

        Assert.Contains("ANA GARCIA", vm.ResumenAbogado);
    }
}
