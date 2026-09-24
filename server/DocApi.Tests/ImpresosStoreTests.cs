using DocApi.Models;
using DocApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocApi.Tests;

public class ImpresosStoreTests
{
    private static ImpresosStore Crear() => new(
        Options.Create(new SyncOptions { ImpresosFilePath = null }),
        Options.Create(new GoogleSheetsOptions()),
        NullLogger<ImpresosStore>.Instance);

    [Fact]
    public void DesmarcarTodos_quita_todas_las_marcas_y_dice_cuantas_eran()
    {
        var store = Crear();
        store.Marcar(1, "firma-1");
        store.Marcar(2, "firma-2");
        store.Marcar(3, "firma-3");

        var desmarcados = store.DesmarcarTodos();

        Assert.Equal(3, desmarcados);
        Assert.Empty(store.Actual);
    }

    [Fact]
    public void DesmarcarTodos_sin_nada_marcado_no_falla_y_devuelve_cero()
    {
        var store = Crear();

        Assert.Equal(0, store.DesmarcarTodos());
        Assert.Empty(store.Actual);
    }

    [Fact]
    public void Tras_desmarcar_todos_se_puede_volver_a_marcar()
    {
        // Es el caso real: se limpia el lote para rehacerlo y se vuelve a generar.
        var store = Crear();
        store.Marcar(1, "firma-1");
        store.DesmarcarTodos();

        var entrada = store.Marcar(1, "firma-1");

        Assert.Equal("firma-1", store.Actual[1].Firma);
        Assert.Equal(entrada.Fecha, store.Actual[1].Fecha);
    }
}
