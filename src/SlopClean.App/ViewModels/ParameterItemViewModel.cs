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
    public bool IsPathList => Parameter is PathListParameter;
    public IReadOnlyList<string> EnumValues => Parameter is EnumParameter e ? e.AllowedValues : [];

    [ObservableProperty]
    public partial object? Value { get; set; }

    /// <summary>
    /// Typed value suitable for <see cref="ParameterValidator"/>.
    /// </summary>
    public object? TypedValue => Parameter switch
    {
        BoolParameter => BoolValue,
        IntParameter => IntValue,
        EnumParameter => StringValue,
        PathListParameter => ParameterValueCoercion.CoercePathList(Parameter, Value),
        _ => Value
    };

    public bool BoolValue
    {
        get => ParameterValueCoercion.ReadBool(Parameter, Value);
        set
        {
            if (!IsBool || BoolValue == value)
            {
                return;
            }

            Value = value;
        }
    }

    public int IntValue
    {
        get => ParameterValueCoercion.ReadInt(Parameter, Value);
        set
        {
            if (!IsInt)
            {
                return;
            }

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
            if (!IsEnum)
            {
                return;
            }

            var next = value ?? string.Empty;
            if (string.Equals(StringValue, next, StringComparison.Ordinal))
            {
                return;
            }

            Value = next;
        }
    }

    public string PathsText
    {
        get => ParameterValueCoercion.FormatPathList(
            ParameterValueCoercion.CoercePathList(Parameter, Value));
        set
        {
            if (!IsPathList)
            {
                return;
            }

            var next = ParameterValueCoercion.ParsePathList(value);
            var current = ParameterValueCoercion.CoercePathList(Parameter, Value);
            if (current.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
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
        OnPropertyChanged(nameof(PathsText));
        OnPropertyChanged(nameof(TypedValue));
    }
}
