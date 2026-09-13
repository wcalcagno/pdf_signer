using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfSigner.App.ViewModels;

/// <summary>Una página del documento en la tira de miniaturas.</summary>
public sealed partial class PageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public PageViewModel(int index) => Index = index;

    public int Index { get; }

    public string Label => (Index + 1).ToString();
}
