using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Controls;

public sealed partial class FindingList : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(ObservableCollection<FindingItemViewModel>),
            typeof(FindingList),
            new PropertyMetadata(null));

    public FindingList()
    {
        InitializeComponent();
    }

    public ObservableCollection<FindingItemViewModel> Items
    {
        get => (ObservableCollection<FindingItemViewModel>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
