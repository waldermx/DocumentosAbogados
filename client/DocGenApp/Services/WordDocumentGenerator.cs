using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DocGenApp.Services;

/// <summary>
/// Genera el .docx final a partir de la plantilla, reemplazando <c>{{campo}}</c>
/// por los valores parseados. Todo ocurre en el equipo del usuario: el servidor
/// nunca ve ni genera documentos.
/// </summary>
public sealed class WordDocumentGenerator
{
    /// <summary>Detecta <c>{{nombre}}</c> con espacios opcionales dentro de las llaves.</summary>
    private static readonly Regex Placeholder = new(@"\{\{\s*(?<nombre>[\w.-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Copia <paramref name="rutaPlantilla"/> a <paramref name="rutaSalida"/> y sustituye los
    /// placeholders. La plantilla nunca se modifica.
    /// </summary>
    /// <returns>Los nombres de placeholder que quedaron sin valor (para avisar al usuario).</returns>
    public IReadOnlyList<string> Generar(
        string rutaPlantilla,
        IReadOnlyDictionary<string, string> valores,
        string rutaSalida)
    {
        if (!File.Exists(rutaPlantilla))
        {
            throw new FileNotFoundException(
                $"No se encontró la plantilla en '{rutaPlantilla}'. Revisa TemplatePath en appsettings.json.",
                rutaPlantilla);
        }

        var carpetaSalida = Path.GetDirectoryName(Path.GetFullPath(rutaSalida));
        if (!string.IsNullOrEmpty(carpetaSalida))
        {
            Directory.CreateDirectory(carpetaSalida);
        }

        File.Copy(rutaPlantilla, rutaSalida, overwrite: true);

        var sinValor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = WordprocessingDocument.Open(rutaSalida, isEditable: true);

        // Cuerpo, encabezados y pies: los placeholders suelen vivir también en el membrete.
        var partes = new List<OpenXmlPart>();
        if (doc.MainDocumentPart is { } main)
        {
            partes.Add(main);
            partes.AddRange(main.HeaderParts);
            partes.AddRange(main.FooterParts);
        }

        foreach (var parte in partes)
        {
            foreach (var parrafo in parte.RootElement?.Descendants<Paragraph>() ?? [])
            {
                ProcesarParrafo(parrafo, valores, sinValor);
            }
        }

        doc.Save();
        return sinValor.ToArray();
    }

    /// <summary>
    /// Word suele partir <c>{{consecutivo}}</c> en varios <see cref="Run"/> (por revisiones,
    /// corrección ortográfica o formato), así que buscar dentro de cada run por separado no
    /// encuentra nada. Se reconstruye el texto completo del párrafo, se reemplaza ahí, y se
    /// vuelca el resultado en el primer run que aportaba texto, conservando su formato.
    /// </summary>
    private static void ProcesarParrafo(
        Paragraph parrafo,
        IReadOnlyDictionary<string, string> valores,
        ISet<string> sinValor)
    {
        var textos = parrafo.Descendants<Text>().ToList();
        if (textos.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var t in textos)
        {
            sb.Append(t.Text);
        }

        var original = sb.ToString();
        if (!Placeholder.IsMatch(original))
        {
            return;
        }

        var reemplazado = Placeholder.Replace(original, match =>
        {
            var nombre = match.Groups["nombre"].Value;
            if (valores.TryGetValue(nombre, out var valor) && !string.IsNullOrEmpty(valor))
            {
                return valor;
            }

            sinValor.Add(nombre);
            return match.Value; // se deja tal cual para que el usuario vea qué faltó
        });

        if (reemplazado == original)
        {
            return;
        }

        // Todo el texto del párrafo pasa al primer nodo (hereda su formato);
        // los demás se vacían en lugar de eliminarse, para no romper la estructura del XML.
        textos[0].Text = reemplazado;
        textos[0].Space = SpaceProcessingModeValues.Preserve;

        for (var i = 1; i < textos.Count; i++)
        {
            textos[i].Text = string.Empty;
        }
    }

    /// <summary>
    /// Crea una plantilla de ejemplo válida con los placeholders del regex por defecto,
    /// para poder probar la generación de punta a punta antes de tener la plantilla real.
    /// </summary>
    public static void CrearPlantillaDeEjemplo(string ruta)
    {
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        using var doc = WordprocessingDocument.Create(ruta, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        body.Append(Parrafo("PLANTILLA DE EJEMPLO", negrita: true));
        body.Append(Parrafo(string.Empty));
        body.Append(Parrafo("Reemplaza este archivo por tu plantilla real."));
        body.Append(Parrafo("Los siguientes marcadores se sustituyen al generar el documento:"));
        body.Append(Parrafo(string.Empty));
        body.Append(Parrafo("Consecutivo: {{consecutivo}}"));
        body.Append(Parrafo("Colegio: {{colegio}}"));
        body.Append(Parrafo("Circuito: {{circuito}}"));
        body.Append(Parrafo(string.Empty));
        body.Append(Parrafo("Valor original de la hoja: {{valorCrudo}}"));
        body.Append(Parrafo("Fecha de generación: {{fecha}}"));

        main.Document.Save();
    }

    private static Paragraph Parrafo(string texto, bool negrita = false)
    {
        var run = new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve });
        if (negrita)
        {
            run.RunProperties = new RunProperties(new Bold());
        }

        return new Paragraph(run);
    }
}
