using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace DocGenApp.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Diálogo de guardado. Lo consume el ViewModel a través de un delegado,
    /// para que la lógica no dependa de la UI.
    /// </summary>
    public async Task<string?> PedirRutaDeGuardadoAsync(string nombreSugerido)
    {
        var archivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar documento",
            SuggestedFileName = nombreSugerido,
            DefaultExtension = "docx",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Documento de Word")
                {
                    Patterns = ["*.docx"],
                    MimeTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]
                }
            ]
        });

        return archivo?.TryGetLocalPath();
    }
}
