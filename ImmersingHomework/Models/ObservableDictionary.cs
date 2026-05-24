using System;
using System.Collections;
using System.Collections.Generic;
using Serilog;

namespace ImmersingHomework.Models;

public class ObservableDictionary<TK, TV> : IEnumerable<KeyValuePair<TK, TV>> where TK : notnull
{
    private readonly ILogger _logger = Log.ForContext<ObservableDictionary<TK, TV>>();
    private Dictionary<TK, TV> _dictionary = new();

    public event Action<TK, TV>? ItemAdded;
    public event Action<TK, TV>? ItemRemoved;
    public event Action<TK, TV, TV>? ItemUpdated;
    public event Action? Cleared;

    public int Count => _dictionary.Count;

    public ObservableDictionary()
    {
    }

    public ObservableDictionary(IEnumerable<KeyValuePair<TK, TV>> collection)
    {
        foreach (var item in collection)
        {
            _dictionary.Add(item.Key, item.Value);
        }
    }

    public bool ContainsKey(TK key)
    {
        return _dictionary.ContainsKey(key);
    }

    public bool TryGetValue(TK key, out TV value)
    {
        return _dictionary.TryGetValue(key, out value!);
    }

    public TV? this[TK key]
    {
        get
        {
            _dictionary.TryGetValue(key, out var value);
            return value;
        }
        set
        {
            if (_dictionary.TryGetValue(key, out var oldValue))
            {
                _dictionary[key] = value!;
                ItemUpdated?.Invoke(key, oldValue!, value!);
            }
            else
            {
                Add(key, value!);
            }
        }
    }

    public void Add(TK key, TV value)
    {
        _dictionary.Add(key, value);
        ItemAdded?.Invoke(key, value);
    }

    public void Add(KeyValuePair<TK, TV> item)
    {
        _dictionary.Add(item.Key, item.Value);
        ItemAdded?.Invoke(item.Key, item.Value);
    }

    public bool Remove(TK key)
    {
        if (_dictionary.TryGetValue(key, out var value))
        {
            _dictionary.Remove(key);
            ItemRemoved?.Invoke(key, value);
            return true;
        }
        return false;
    }

    public void Clear()
    {
        _dictionary.Clear();
        Cleared?.Invoke();
    }

    public Dictionary<TK, TV>.KeyCollection Keys => _dictionary.Keys;

    public Dictionary<TK, TV>.ValueCollection Values => _dictionary.Values;

    public IEnumerator<KeyValuePair<TK, TV>> GetEnumerator()
    {
        return _dictionary.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _dictionary.GetEnumerator();
    }
}