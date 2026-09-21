using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DocGenApp.Views;

public partial class RecordDetailView : UserControl
{
    public RecordDetailView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
