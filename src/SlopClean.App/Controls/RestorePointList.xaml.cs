using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Controls;

public sealed partial class RestorePointList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(IEnumerable<RestorePointItemViewModel>),
        typeof(RestorePointList),
        new PropertyMetadata(null));

    public RestorePointList()
    {
        InitializeComponent();
    }

    public IEnumerable<RestorePointItemViewModel>? Items
    {
        get => (IEnumerable<RestorePointItemViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
