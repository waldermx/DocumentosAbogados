using DocGenApp.Models;
using DocGenApp.Services;
using DocGenApp.ViewModels;
using Xunit;

namespace DocGenApp.Tests;

/// <summary>
/// Cubre solo la gestión de varios abogados en <see cref="MainViewModel"/>: alta, baja,
/// selección y qué abogado se aplica al generar. No ejercita red ni sincronización
/// (<see cref="ApiClient"/> no llega a hacer ninguna llamada en estos tests).
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
        Assert.Null(vm.AbogadoSeleccionado);
        Assert.True(vm.AbogadoExpandido);
        Assert.False(vm.HayAbogadoSeleccionado);
    }

    [Fact]
    public void Agregar_abogado_lo_suma_a_la_lista_y_lo_selecciona()
    {
        var vm = Crear();

        vm.AgregarAbogadoCommand.Execute(null);
        vm.AgregarAbogadoCommand.Execute(null);

        Assert.Equal(2, vm.Abogados.Count);
        Assert.Same(vm.Abogados[1], vm.AbogadoSeleccionado);
    }

    [Fact]
    public void Eliminar_el_seleccionado_selecciona_el_siguiente_y_persiste()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        var primero = vm.AbogadoSeleccionado!;
        vm.AgregarAbogadoCommand.Execute(null);
        var segundo = vm.AbogadoSeleccionado!;

        vm.AbogadoSeleccionado = primero;
        vm.EliminarAbogadoCommand.Execute(null);

        Assert.Single(vm.Abogados);
        Assert.Same(segundo, vm.AbogadoSeleccionado);

        // Eliminar guarda de inmediato: un cierre accidental de la app no debe resucitar al borrado.
        var persistido = new AbogadoStore(_carpeta).Cargar();
        Assert.Single(persistido.Abogados);
        Assert.Equal(segundo.Id, persistido.SeleccionadoId);
    }

    [Fact]
    public void Eliminar_el_ultimo_abogado_deja_la_seleccion_vacia_sin_lanzar()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);

        vm.EliminarAbogadoCommand.Execute(null);

        Assert.Empty(vm.Abogados);
        Assert.Null(vm.AbogadoSeleccionado);
        Assert.False(vm.EliminarAbogadoCommand.CanExecute(null));
    }

    [Fact]
    public void Guardar_persiste_todos_los_abogados_y_cual_esta_activo()
    {
        var vm = Crear();
        vm.AgregarAbogadoCommand.Execute(null);
        vm.AbogadoSeleccionado!.Nombre = "ANA GARCIA";
        vm.AbogadoSeleccionado!.UsuarioFirel = "agarcia01";
        vm.AbogadoSeleccionado!.CedulaProfesional = "1234567";
        vm.AgregarAbogadoCommand.Execute(null);
        vm.AbogadoSeleccionado!.Nombre = "LUIS TORRES";

        vm.GuardarAbogadosCommand.Execute(null);

        var persistido = new AbogadoStore(_carpeta).Cargar();
        Assert.Equal(2, persistido.Abogados.Count);
        Assert.Contains(persistido.Abogados, a => a.Nombre == "ANA GARCIA" && a.EstaCompleto);
        Assert.Contains(persistido.Abogados, a => a.Nombre == "LUIS TORRES");
    }

    [Fact]
    public void Arranca_con_el_abogado_que_estaba_seleccionado_al_guardar()
    {
        var uno = new DatosAbogado { Nombre = "ANA GARCIA", UsuarioFirel = "a", CedulaProfesional = "1" };
        var dos = new DatosAbogado { Nombre = "LUIS TORRES", UsuarioFirel = "b", CedulaProfesional = "2" };

        var vm = Crear(new AbogadosPersistidos { Abogados = [uno, dos], SeleccionadoId = dos.Id });

        Assert.Equal(2, vm.Abogados.Count);
        Assert.Equal("LUIS TORRES", vm.AbogadoSeleccionado?.Nombre);
        Assert.False(vm.AbogadoExpandido); // el activo está completo: no hace falta abrir el panel
    }

    [Fact]
    public void El_resumen_distingue_sin_abogados_de_ninguno_seleccionado_y_de_uno_activo()
    {
        var vm = Crear();
        Assert.Contains("No hay ningún abogado", vm.ResumenAbogado);

        vm.AgregarAbogadoCommand.Execute(null);
        vm.AbogadoSeleccionado = null;
        Assert.Contains("ninguno seleccionado", vm.ResumenAbogado);

        vm.AbogadoSeleccionado = vm.Abogados[0];
        vm.AbogadoSeleccionado.Nombre = "ANA GARCIA";
        vm.AbogadoSeleccionado.UsuarioFirel = "agarcia01";
        vm.AbogadoSeleccionado.CedulaProfesional = "1234567";
        Assert.Contains("ANA GARCIA", vm.ResumenAbogado);
        Assert.Contains("FIREL agarcia01", vm.ResumenAbogado);
    }
}
