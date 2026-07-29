using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SlopClean.App.Controls;

public sealed partial class ScanProgressControl : UserControl
{
    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(ScanProgressControl), new PropertyMetadata(false));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ScanProgressControl), new PropertyMetadata(""));

    public ScanProgressControl()
    {
        InitializeComponent();
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }
}
