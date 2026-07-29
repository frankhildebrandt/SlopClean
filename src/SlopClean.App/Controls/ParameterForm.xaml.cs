using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Controls;

public sealed partial class ParameterForm : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(ObservableCollection<ParameterItemViewModel>),
            typeof(ParameterForm),
            new PropertyMetadata(null));

    public ParameterForm()
    {
        InitializeComponent();
    }

    public ObservableCollection<ParameterItemViewModel> Items
    {
        get => (ObservableCollection<ParameterItemViewModel>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
