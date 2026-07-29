using CommunityToolkit.Mvvm.ComponentModel;
using SlopClean.Core.Parameters;

namespace SlopClean.App.ViewModels;

public partial class ParameterItemViewModel : ObservableObject
{
    public ParameterItemViewModel(IModuleParameter parameter)
    {
        Parameter = parameter;
        Value = parameter.DefaultValue;
    }

    public IModuleParameter Parameter { get; }
    public string Id => Parameter.Id;
    public string DisplayName => Parameter.DisplayName;
    public string Description => Parameter.Description;
    public bool IsBool => Parameter is BoolParameter;
    public bool IsInt => Parameter is IntParameter;
    public bool IsEnum => Parameter is EnumParameter;
    public IReadOnlyList<string> EnumValues => Parameter is EnumParameter e ? e.AllowedValues : [];

    [ObservableProperty]
    public partial object? Value { get; set; }

    public bool BoolValue
    {
        get => Value is true;
        set
        {
            if (Value is true == value)
            {
                return;
            }

            Value = value;
        }
    }

    public int IntValue
    {
        get => Value is int i ? i : Convert.ToInt32(Parameter.DefaultValue ?? 0);
        set
        {
            if (Value is int current && current == value)
            {
                return;
            }

            Value = value;
        }
    }

    public string StringValue
    {
        get => Value?.ToString() ?? Parameter.DefaultValue?.ToString() ?? string.Empty;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(StringValue, next, StringComparison.Ordinal))
            {
                return;
            }

            Value = next;
        }
    }

    partial void OnValueChanged(object? value)
    {
        // Keep typed accessors in sync for x:Bind without re-entering TwoWay controls.
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(IntValue));
        OnPropertyChanged(nameof(StringValue));
    }
}
