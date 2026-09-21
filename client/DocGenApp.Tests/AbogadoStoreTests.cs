using System.Text.Json;
using DocGenApp.Models;
using DocGenApp.Services;
using Xunit;

namespace DocGenApp.Tests;

public sealed class AbogadoStoreTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "DocGenAppTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* limpieza best-effort */ }
    }

    private AbogadoStore Store() => new(_carpeta);

    [Fact]
    public void Sin_archivo_previo_carga_una_lista_vacia()
    {
        var persistido = Store().Cargar();

        Assert.Empty(persistido.Abogados);
    }

    [Fact]
    public void Guarda_y_recupera_varios_abogados_en_el_mismo_orden()
    {
        var uno = new DatosAbogado { Nombre = "ANA GARCIA", UsuarioFirel = "agarcia01", CedulaProfesional = "1" };
        var dos = new DatosAbogado { Nombre = "LUIS TORRES", UsuarioFirel = "ltorres02", CedulaProfesional = "2" };
        var store = Store();

        store.Guardar(new AbogadosPersistidos { Abogados = [uno, dos] });

        var recuperado = Store().Cargar();

        Assert.Equal(2, recuperado.Abogados.Count);
        // El orden importa: decide qué abogado es abogado1 y cuál abogado2 en la plantilla.
        Assert.Equal("ANA GARCIA", recuperado.Abogados[0].Nombre);
        Assert.Equal("LUIS TORRES", recuperado.Abogados[1].Nombre);
    }

    [Fact]
    public void Un_archivo_corrupto_no_impide_arrancar()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Path.Combine(_carpeta, "abogados.json"), "{ esto no es json válido");

        var persistido = Store().Cargar();

        Assert.Empty(persistido.Abogados);
    }

    [Fact]
    public void Migra_el_archivo_de_un_solo_abogado_del_formato_anterior()
    {
        // Formato previo a que existieran varios: un objeto DatosAbogado suelto,
        // sin envolver en una lista.
        Directory.CreateDirectory(_carpeta);
        var json = JsonSerializer.Serialize(
            new { Nombre = "ANA GARCIA", UsuarioFirel = "agarcia01", CedulaProfesional = "1234567" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(_carpeta, "abogado.json"), json);

        var persistido = Store().Cargar();

        var migrado = Assert.Single(persistido.Abogados);
        Assert.Equal("ANA GARCIA", migrado.Nombre);
        Assert.Equal("agarcia01", migrado.UsuarioFirel);

        // La migración se persiste: una segunda carga ya no depende del archivo viejo.
        Assert.True(File.Exists(Path.Combine(_carpeta, "abogados.json")));
        File.Delete(Path.Combine(_carpeta, "abogado.json"));
        var segunda = Store().Cargar();
        Assert.Single(segunda.Abogados);
    }

    [Fact]
    public void El_archivo_nuevo_tiene_prioridad_sobre_el_formato_anterior()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Path.Combine(_carpeta, "abogado.json"),
            """{"Nombre":"VIEJO","UsuarioFirel":"x","CedulaProfesional":"1"}""");

        var store = Store();
        store.Guardar(new AbogadosPersistidos
        {
            Abogados = [new DatosAbogado { Nombre = "NUEVO", UsuarioFirel = "y", CedulaProfesional = "2" }]
        });

        var persistido = Store().Cargar();

        Assert.Equal("NUEVO", Assert.Single(persistido.Abogados).Nombre);
    }
}
