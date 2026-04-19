using System.Runtime.CompilerServices;

namespace VGAnima.Cache;

/// <summary>Maps a game object (typically <c>BarPatron</c>) to an arbitrary
/// record via <see cref="ConditionalWeakTable{TKey, TValue}"/> so entries are
/// reclaimed when the game releases the patron. Thread-safety comes from the
/// underlying table.</summary>
internal sealed class ConversionRegistry<TKey, TValue>
    where TKey   : class
    where TValue : class
{
    private readonly ConditionalWeakTable<TKey, TValue> _table = new();

    public void Register(TKey key, TValue value) => _table.AddOrUpdate(key, value);

    public bool TryGet(TKey key, out TValue value) => _table.TryGetValue(key, out value!);

    public void Remove(TKey key) => _table.Remove(key);
}
