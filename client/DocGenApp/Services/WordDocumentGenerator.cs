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
    /// encuentra nada. Se reconstruye el texto completo del párrafo para localizar los
    /// marcadores, pero el reemplazo se escribe <em>run por run</em>: cada nodo conserva el
    /// texto que no formaba parte del marcador y, sobre todo, su propio formato. Volcar todo
    /// el párrafo en el primer run haría que la negrita, el subrayado o la fuente de los
    /// demás runs se perdieran.
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

        // inicios[i] = posición del nodo i dentro del texto completo del párrafo.
        var inicios = new int[textos.Count];
        var sb = new StringBuilder();
        for (var i = 0; i < textos.Count; i++)
        {
            inicios[i] = sb.Length;
            sb.Append(textos[i].Text);
        }

        var original = sb.ToString();
        var coincidencias = Placeholder.Matches(original);
        if (coincidencias.Count == 0)
        {
            return;
        }

        var buffer = textos.Select(t => t.Text).ToArray();
        var huboCambios = false;

        // De derecha a izquierda: así los recortes ya aplicados no desplazan las posiciones
        // de los marcadores que todavía faltan por procesar.
        for (var m = coincidencias.Count - 1; m >= 0; m--)
        {
            var match = coincidencias[m];
            var nombre = match.Groups["nombre"].Value;

            if (!valores.TryGetValue(nombre, out var valor) || string.IsNullOrEmpty(valor))
            {
                sinValor.Add(nombre); // se deja tal cual para que el usuario vea qué faltó
                continue;
            }

            var inicio = match.Index;
            var fin = match.Index + match.Length;
            var primero = NodoEn(inicios, textos, inicio);
            var ultimo = NodoEn(inicios, textos, fin - 1);

            var desdeLocal = inicio - inicios[primero];
            var hastaLocal = fin - inicios[ultimo];

            if (primero == ultimo)
            {
                buffer[primero] = buffer[primero][..desdeLocal] + valor + buffer[primero][hastaLocal..];
            }
            else
            {
                // El valor entra en el run donde empieza el marcador (hereda su formato);
                // del último run sólo sobrevive lo que venía después del marcador, y los
                // runs intermedios eran marcador puro.
                buffer[primero] = buffer[primero][..desdeLocal] + valor;
                buffer[ultimo] = buffer[ultimo][hastaLocal..];
                for (var j = primero + 1; j < ultimo; j++)
                {
                    buffer[j] = string.Empty;
                }
            }

            huboCambios = true;
        }

        if (!huboCambios)
        {
            return;
        }

        for (var i = 0; i < textos.Count; i++)
        {
            if (buffer[i] == textos[i].Text)
            {
                continue;
            }

            textos[i].Text = buffer[i];
            textos[i].Space = SpaceProcessingModeValues.Preserve;
        }
    }

    /// <summary>Índice del nodo de texto que contiene la posición <paramref name="posicion"/>.</summary>
    private static int NodoEn(int[] inicios, IReadOnlyList<Text> textos, int posicion)
    {
        for (var i = textos.Count - 1; i >= 0; i--)
        {
            // Los nodos vacíos no contienen ninguna posición, de ahí el largo > 0.
            if (textos[i].Text.Length > 0 && inicios[i] <= posicion)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Crea una plantilla de ejemplo válida con los marcadores de las columnas por defecto,
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
        body.Append(Parrafo("De la hoja:", negrita: true));
        body.Append(Parrafo("Amparo número: {{consecutivo}}"));
        body.Append(Parrafo("Circuito: {{circuito}}"));
        body.Append(Parrafo("Nombre: {{nombre}}"));
        body.Append(Parrafo(string.Empty));
        body.Append(Parrafo("Del abogado (se capturan en la app, no en la hoja):", negrita: true));
        body.Append(Parrafo("Nombre del abogado: {{abogadoNombre}}"));
        body.Append(Parrafo("Usuario FIREL: {{abogadoFirel}}"));
        body.Append(Parrafo("Cédula profesional: {{abogadoCedula}}"));
        body.Append(Parrafo(string.Empty));
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
