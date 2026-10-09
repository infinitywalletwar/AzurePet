using System.Collections;

namespace InPolsure.Web.IntegrationTests.Observability;

/// <summary>
/// Thread-safe <see cref="ICollection{T}"/> for the in-memory exporter: spans are added on
/// request threads while the test thread polls.
/// </summary>
internal sealed class SynchronizedList<T> : ICollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public T[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _items];
        }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _items.Add(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            return _items.Remove(item);
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Snapshot()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
