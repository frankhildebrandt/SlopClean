using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Controls;

public sealed partial class CleanTaskList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(ObservableCollection<CleanTaskItemViewModel>),
        typeof(CleanTaskList),
        new PropertyMetadata(null));

    public CleanTaskList()
    {
        InitializeComponent();
    }

    public ObservableCollection<CleanTaskItemViewModel>? Items
    {
        get => (ObservableCollection<CleanTaskItemViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
