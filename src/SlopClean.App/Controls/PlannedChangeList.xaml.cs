using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Controls;

public sealed partial class PlannedChangeList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(IEnumerable<PlannedChangeItemViewModel>),
        typeof(PlannedChangeList),
        new PropertyMetadata(null));

    public PlannedChangeList()
    {
        InitializeComponent();
    }

    public IEnumerable<PlannedChangeItemViewModel>? Items
    {
        get => (IEnumerable<PlannedChangeItemViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
