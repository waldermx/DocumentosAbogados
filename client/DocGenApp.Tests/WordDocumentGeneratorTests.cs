using DocGenApp.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace DocGenApp.Tests;

public sealed class WordDocumentGeneratorTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "DocGenAppTests", Guid.NewGuid().ToString("N"));

    public WordDocumentGeneratorTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* limpieza best-effort */ }
    }

    private string Ruta(string nombre) => Path.Combine(_carpeta, nombre);

    private static string TextoDe(string ruta)
    {
        using var doc = WordprocessingDocument.Open(ruta, isEditable: false);
        return doc.MainDocumentPart!.Document.Body!.InnerText;
    }

    /// <summary>Crea una plantilla cuyos párrafos son runs sueltos, tal cual los define el caller.</summary>
    private string CrearPlantilla(string nombre, params string[][] parrafosComoRuns)
    {
        var ruta = Ruta(nombre);
        using var doc = WordprocessingDocument.Create(ruta, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());

        foreach (var runs in parrafosComoRuns)
        {
            var parrafo = new Paragraph();
            foreach (var texto in runs)
            {
                parrafo.Append(new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
            }
            main.Document.Body!.Append(parrafo);
        }

        main.Document.Save();
        return ruta;
    }

    private static Dictionary<string, string> Valores() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["consecutivo"] = "644/2026",
        ["colegio"] = "TERCER COLEGIADO",
        ["circuito"] = "DECIMOPRIMER CIRCUITO"
    };

    [Fact]
    public void Reemplaza_un_placeholder_contenido_en_un_solo_run()
    {
        var plantilla = CrearPlantilla("simple.docx", ["Expediente {{consecutivo}} del año"]);
        var salida = Ruta("salida-simple.docx");

        var faltantes = new WordDocumentGenerator().Generar(plantilla, Valores(), salida);

        Assert.Empty(faltantes);
        Assert.Equal("Expediente 644/2026 del año", TextoDe(salida));
    }

    [Fact]
    public void Reemplaza_un_placeholder_partido_en_varios_runs()
    {
        // Es el caso real: Word fragmenta "{{consecutivo}}" en varios Text por formato o revisiones.
        var plantilla = CrearPlantilla("partido.docx",
            ["Expediente ", "{{con", "secu", "tivo}}", " del año"]);
        var salida = Ruta("salida-partido.docx");

        var faltantes = new WordDocumentGenerator().Generar(plantilla, Valores(), salida);

        Assert.Empty(faltantes);
        Assert.Equal("Expediente 644/2026 del año", TextoDe(salida));
    }

    [Fact]
    public void Reemplaza_varios_placeholders_en_el_mismo_parrafo()
    {
        var plantilla = CrearPlantilla("varios.docx",
            ["{{consecutivo}} ", "{{colegio}}", " DEL {{circuito}}"]);
        var salida = Ruta("salida-varios.docx");

        new WordDocumentGenerator().Generar(plantilla, Valores(), salida);

        Assert.Equal("644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO", TextoDe(salida));
    }

    [Fact]
    public void Admite_espacios_dentro_de_las_llaves()
    {
        var plantilla = CrearPlantilla("espacios.docx", ["Valor: {{ consecutivo }}"]);
        var salida = Ruta("salida-espacios.docx");

        new WordDocumentGenerator().Generar(plantilla, Valores(), salida);

        Assert.Equal("Valor: 644/2026", TextoDe(salida));
    }

    [Fact]
    public void Un_placeholder_sin_valor_se_reporta_y_se_deja_visible()
    {
        var plantilla = CrearPlantilla("faltante.docx", ["Juez: {{juez}} — Exp: {{consecutivo}}"]);
        var salida = Ruta("salida-faltante.docx");

        var faltantes = new WordDocumentGenerator().Generar(plantilla, Valores(), salida);

        Assert.Equal(["juez"], faltantes);
        // El marcador se conserva para que el usuario vea exactamente qué quedó sin llenar.
        Assert.Equal("Juez: {{juez}} — Exp: 644/2026", TextoDe(salida));
    }

    [Fact]
    public void Conserva_el_formato_de_cada_run_al_reemplazar()
    {
        // El caso real de la plantilla: "AUTORIZAR" va en negrita y subrayado, y el nombre
        // del abogado en negrita. Si el reemplazo vuelca todo el párrafo en el primer run,
        // el documento generado sale entero en texto plano.
        var ruta = Ruta("formato.docx");
        using (var doc = WordprocessingDocument.Create(ruta, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var parrafo = new Paragraph();
            parrafo.Append(RunConFormato("vengo a ", negrita: false));
            parrafo.Append(RunConFormato("AUTORIZAR", negrita: true));
            parrafo.Append(RunConFormato(" a ", negrita: false));
            parrafo.Append(RunConFormato("{{colegio}}", negrita: true));
            parrafo.Append(RunConFormato(" hoy", negrita: false));
            main.Document.Body!.Append(parrafo);
            main.Document.Save();
        }

        var salida = Ruta("salida-formato.docx");
        new WordDocumentGenerator().Generar(ruta, Valores(), salida);

        using var generado = WordprocessingDocument.Open(salida, isEditable: false);
        var runs = generado.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();

        Assert.Equal("vengo a AUTORIZAR a TERCER COLEGIADO hoy", TextoDe(salida));
        Assert.Collection(runs,
            r => AssertRun(r, "vengo a ", negrita: false),
            r => AssertRun(r, "AUTORIZAR", negrita: true),
            r => AssertRun(r, " a ", negrita: false),
            r => AssertRun(r, "TERCER COLEGIADO", negrita: true),
            r => AssertRun(r, " hoy", negrita: false));
    }

    [Fact]
    public void Conserva_el_formato_cuando_el_placeholder_esta_partido_en_varios_runs()
    {
        // Word parte el marcador; el valor debe quedar con el formato del run donde empieza
        // y el texto que venía después del marcador debe conservar el suyo.
        var ruta = Ruta("formato-partido.docx");
        using (var doc = WordprocessingDocument.Create(ruta, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var parrafo = new Paragraph();
            parrafo.Append(RunConFormato("Exp ", negrita: false));
            parrafo.Append(RunConFormato("{{con", negrita: true));
            parrafo.Append(RunConFormato("secu", negrita: true));
            parrafo.Append(RunConFormato("tivo}} final", negrita: false));
            main.Document.Body!.Append(parrafo);
            main.Document.Save();
        }

        var salida = Ruta("salida-formato-partido.docx");
        new WordDocumentGenerator().Generar(ruta, Valores(), salida);

        using var generado = WordprocessingDocument.Open(salida, isEditable: false);
        var runs = generado.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();

        Assert.Equal("Exp 644/2026 final", TextoDe(salida));
        Assert.Collection(runs,
            r => AssertRun(r, "Exp ", negrita: false),
            r => AssertRun(r, "644/2026", negrita: true),
            r => AssertRun(r, string.Empty, negrita: true),
            r => AssertRun(r, " final", negrita: false));
    }

    private static Run RunConFormato(string texto, bool negrita)
    {
        var run = new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve });
        if (negrita)
        {
            run.RunProperties = new RunProperties(new Bold());
        }

        return run;
    }

    private static void AssertRun(Run run, string texto, bool negrita)
    {
        Assert.Equal(texto, run.InnerText);
        Assert.Equal(negrita, run.RunProperties?.Bold is not null);
    }

    [Fact]
    public void No_modifica_la_plantilla_original()
    {
        var plantilla = CrearPlantilla("intacta.docx", ["Exp {{consecutivo}}"]);
        var antes = TextoDe(plantilla);

        new WordDocumentGenerator().Generar(plantilla, Valores(), Ruta("salida-intacta.docx"));

        Assert.Equal(antes, TextoDe(plantilla));
        Assert.Contains("{{consecutivo}}", TextoDe(plantilla));
    }

    [Fact]
    public void Falla_con_mensaje_claro_si_no_existe_la_plantilla()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            new WordDocumentGenerator().Generar(Ruta("no-existe.docx"), Valores(), Ruta("x.docx")));

        Assert.Contains("TemplatePath", ex.Message);
    }

    [Fact]
    public void La_plantilla_de_ejemplo_se_genera_y_es_utilizable_de_punta_a_punta()
    {
        var plantilla = Ruta("ejemplo.docx");
        WordDocumentGenerator.CrearPlantillaDeEjemplo(plantilla);

        Assert.True(File.Exists(plantilla));
        Assert.Contains("{{consecutivo}}", TextoDe(plantilla));

        var valores = Valores();
        valores["fecha"] = "21/09/2026";
        valores["nombre"] = "JUAN PEREZ LOPEZ";
        valores["abogadoNombre"] = "ANA GARCIA";
        valores["abogadoFirel"] = "agarcia01";
        valores["abogadoCedula"] = "1234567";

        var salida = Ruta("salida-ejemplo.docx");
        var faltantes = new WordDocumentGenerator().Generar(plantilla, valores, salida);

        var texto = TextoDe(salida);
        Assert.Empty(faltantes);
        Assert.DoesNotContain("{{", texto);
        Assert.Contains("644/2026", texto);
        Assert.Contains("DECIMOPRIMER CIRCUITO", texto);
        Assert.Contains("JUAN PEREZ LOPEZ", texto);
        Assert.Contains("agarcia01", texto);
    }

    [Fact]
    public void Un_documento_generado_con_datos_de_abogado_vacios_los_reporta_como_faltantes()
    {
        // Es el caso de un usuario que no ha capturado sus datos todavía: el documento
        // sale igual, pero con los marcadores a la vista y el aviso en la app.
        var plantilla = CrearPlantilla("abogado.docx", ["Firma: {{abogadoNombre}} — FIREL {{abogadoFirel}}"]);
        var salida = Ruta("salida-abogado.docx");

        var valores = Valores();
        valores["abogadoNombre"] = string.Empty;
        valores["abogadoFirel"] = string.Empty;

        var faltantes = new WordDocumentGenerator().Generar(plantilla, valores, salida);

        Assert.Equal(["abogadoFirel", "abogadoNombre"], faltantes.Order());
        Assert.Contains("{{abogadoNombre}}", TextoDe(salida));
    }
}
