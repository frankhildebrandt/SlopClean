using Microsoft.UI.Xaml.Controls;

namespace SlopClean.App.Services;

public interface INavigationService
{
    void Attach(Frame frame);

    void Navigate(Type pageType, object? parameter = null);

    Type? CurrentPageType { get; }

    bool CanGoBack { get; }

    void GoBack();
}

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    public void Attach(Frame frame) => _frame = frame;

    public Type? CurrentPageType => _frame?.Content?.GetType();

    public void Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("Navigation frame is not attached.");
        }

        if (_frame.Content?.GetType() == pageType && parameter is null)
        {
            return;
        }

        _frame.Navigate(pageType, parameter);
    }

    public bool CanGoBack => _frame?.CanGoBack == true;

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }
}
