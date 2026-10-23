using System.Collections.Generic;

namespace EggPdf.Pdf;

internal sealed class PdfObjectCache<TValue> where TValue : class
{
    private readonly struct Slot
    {
        public Slot(int key, TValue value, long cost)
        {
            Key = key;
            Value = value;
            Cost = cost;
        }

        public int Key { get; }
        public TValue Value { get; }
        public long Cost { get; }
    }

    private readonly Dictionary<int, LinkedListNode<Slot>> _byKey = new();
    private readonly LinkedList<Slot> _newestFirst = new();
    private readonly long _capacity;
    private readonly int _alwaysKept;
    private long _cost;

    public PdfObjectCache(long capacity, int alwaysKept = 1)
    {
        _capacity = capacity;
        _alwaysKept = alwaysKept;
    }

    public bool TryGet(int key, out TValue value)
    {
        if (!_byKey.TryGetValue(key, out var node))
        {
            value = null!;
            return false;
        }
        _newestFirst.Remove(node);
        _newestFirst.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Add(int key, TValue value, long cost)
    {
        if (_byKey.TryGetValue(key, out var existing)) Evict(existing);
        _byKey[key] = _newestFirst.AddFirst(new Slot(key, value, cost));
        _cost += cost;
        while (_cost > _capacity && _newestFirst.Count > _alwaysKept) Evict(_newestFirst.Last!);
    }

    private void Evict(LinkedListNode<Slot> node)
    {
        _newestFirst.Remove(node);
        _byKey.Remove(node.Value.Key);
        _cost -= node.Value.Cost;
    }
}
