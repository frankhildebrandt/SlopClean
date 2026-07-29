using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SlopClean.App.Controls;

public sealed partial class ModuleCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ModuleCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(ModuleCard), new PropertyMetadata(""));

    public static readonly DependencyProperty CategoryProperty =
        DependencyProperty.Register(nameof(Category), typeof(string), typeof(ModuleCard), new PropertyMetadata(""));

    public static readonly DependencyProperty ModuleIdProperty =
        DependencyProperty.Register(nameof(ModuleId), typeof(string), typeof(ModuleCard), new PropertyMetadata(""));

    public static readonly DependencyProperty IllustrationProperty =
        DependencyProperty.Register(nameof(Illustration), typeof(ImageSource), typeof(ModuleCard), new PropertyMetadata(null));

    public event EventHandler<string>? OpenRequested;

    public ModuleCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Category
    {
        get => (string)GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    public string ModuleId
    {
        get => (string)GetValue(ModuleIdProperty);
        set => SetValue(ModuleIdProperty, value);
    }

    public ImageSource? Illustration
    {
        get => (ImageSource?)GetValue(IllustrationProperty);
        set => SetValue(IllustrationProperty, value);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke(this, ModuleId);
    }
}
