using System;
using System.Text.Json.Serialization;
using Serilog;

namespace ImmersingHomework.Models;

public class ObservableProperty<T>
{
    private readonly ILogger _logger = Log.ForContext<ObservableProperty<T>>();
    private T? _value;

    [JsonPropertyName("Value")]
    public T? Value
    {
        get => _value;
        set
        {
            _value = value;
            ValueChanged?.Invoke(value);
        }
    }
    
    public event Action<T?>? ValueChanged;

    public ObservableProperty(T? value)
    {
        _value = value;
    }

    // 用于 JSON 反序列化的无参构造函数
    [JsonConstructor]
    public ObservableProperty()
    {
    }
}