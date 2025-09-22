
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using System.Collections;
using System.Linq;

namespace SourceCrafter;

internal class Map<TKey, TValue> : IEnumerable<(TKey, TValue)> where TKey : notnull
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private int[]? _buckets;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private Entry[]? _entries;
#if TARGET_64BIT
    private ulong _fastModMultiplier;
#endif
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private int _count;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private int _freeList;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private int _freeCount;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private int _version;
    private readonly IEqualityComparer<TKey> _comparer;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private const int StartOfFreeList = -3;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public const int HashPrime = 101;

    public int Count => _count;

    public bool IsEmpty => _count == 0;

    public Map(IEqualityComparer<TKey>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        Initialize(0);
    }

    private int Initialize(int capacity)
    {
        int size = GetPrime(capacity);
        int[] buckets = new int[size];
        var entries = new Entry[size];

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _freeList = -1;
#if TARGET_64BIT
            _fastModMultiplier = GetFastModMultiplier((uint)size);
#endif
        _buckets = buckets;
        _entries = entries;

        return size;
    }

    public static int GetPrime(int min)
    {
        if (min < 0)
            throw new ArgumentException("Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table");

        foreach (int prime in Helpers.Primes)
        {
            if (prime >= min)
                return prime;
        }

        // Outside of our predefined table. Compute the hard way.
        for (int i = min | 1; i < int.MaxValue; i += 2)
        {
            if (IsPrime(i) && (i - 1) % HashPrime != 0)
                return i;
        }
        return min;
    }

    public static bool IsPrime(int candidate)
    {
        if ((candidate & 1) != 0)
        {
            int limit = (int)Math.Sqrt(candidate);
            for (int divisor = 3; divisor <= limit; divisor += 2)
            {
                if (candidate % divisor == 0)
                    return false;
            }
            return true;
        }
        return candidate == 2;
    }
    // TODO: apply nullability attributes
    public virtual ref TValue? GetValueRefOrAddDefault(TKey key, out bool exists)
    {
        Entry[]? entries = _entries!;

        uint hashCode = (uint)_comparer.GetHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && _comparer.Equals(key, entries[i].Key))
            {
                exists = true;

                return ref entries[i].Value!;
            }

            i = entries[i].next;

            collisionCount++;

            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            int count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref Entry entry = ref entries![index];
        entry.id = hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        exists = false;

        return ref entry.Value!;
    }
    // TODO: apply nullability attributes
    public virtual bool TryInsert(TKey key, Func<TValue> valueCreator)
    {
        Entry[]? entries = _entries!;

        uint hashCode = (uint)_comparer.GetHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && _comparer.Equals(key, entries[i].Key))
            {
                return false;
            }

            i = entries[i].next;

            collisionCount++;

            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            int count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref Entry entry = ref entries![index];
        entry.id = hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = valueCreator() ?? default!;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        return true!;
    }

    public virtual bool TryGetValue(TKey key, out TValue val)
    {
        uint hashCode = (uint)_comparer.GetHashCode(key);
        int i = GetBucket(hashCode);
        var entries = _entries;
        uint collisionCount = 0;
        i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
        do
        {
            // Test in if to drop range check for following array access
            if ((uint)i >= (uint)entries!.Length)
            {
                val = default!;
                return false;
            }

            ref var entry = ref entries[i];
            if (entry.id == hashCode && _comparer.Equals(entry.Key, key))
            {
                val = entry.Value;
                return true;
            }

            i = entry.next;

            collisionCount++;
        } while (collisionCount <= (uint)entries.Length);

        // The chain of entries forms a loop; which means a concurrent update has happened.
        // Break out of the loop and throw, rather than looping forever.

        val = default!;
        return false;
    }

    public virtual bool Contains(TKey key)
    {
        uint hashCode = (uint)_comparer.GetHashCode(key);
        int i = GetBucket(hashCode);
        var entries = _entries;
        uint collisionCount = 0;
        i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
        do
        {
            // Test in if to drop range check for following array access
            if ((uint)i >= (uint)entries!.Length)
            {
                return false;
            }

            ref var entry = ref entries[i];
            if (entry.id == hashCode && _comparer.Equals(entry.Key, key))
            {
                return true;
            }

            i = entry.next;

            collisionCount++;
        } while (collisionCount <= (uint)entries.Length);

        // The chain of entries forms a loop; which means a concurrent update has happened.
        // Break out of the loop and throw, rather than looping forever.

        return false;
    }

    public bool TryAdd(TKey key, TValue value)
    {
        Entry[]? entries = _entries!;

        uint hashCode = (uint)_comparer.GetHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && _comparer.Equals(key, entries[i].Key))
            {
                return false;
            }

            i = entries[i].next;

            collisionCount++;
            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            int count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref Entry entry = ref entries![index];
        entry.id = hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = value;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        return true;
    }

    private void Resize() => Resize(ExpandPrime(_count), false);

    private void Resize(int newSize, bool forceNewHashCodes)
    {
        // Value types never rehash
        Debug.Assert(!forceNewHashCodes || !typeof(TKey).IsValueType);
        Debug.Assert(newSize >= _entries!.Length);

        var entries = new Entry[newSize];

        int count = _count;
        Array.Copy(_entries, entries, count);

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _buckets = new int[newSize];
#if TARGET_64BIT
        _fastModMultiplier = GetFastModMultiplier((uint)newSize);
#endif
        for (int i = 0; i < count; i++)
        {
            if (entries[i].next >= -1)
            {
                ref int bucket = ref GetBucket(entries[i].id);
                entries[i].next = bucket - 1; // Value in _buckets is 1-based
                bucket = i + 1;
            }
        }

        _entries = entries;
    }
    public static ulong GetFastModMultiplier(uint divisor) =>
            ulong.MaxValue / divisor + 1;

    public const int MaxPrimeArrayLength = 0x7FFFFFC3;
    public static int ExpandPrime(int oldSize)
    {
        int newSize = 2 * oldSize;

        // Allow the hashtables to grow to maximum possible size (~2G elements) before encountering capacity overflow.
        // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
        if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
        {
            Debug.Assert(MaxPrimeArrayLength == GetPrime(MaxPrimeArrayLength), "Invalid MaxPrimeArrayLength");
            return MaxPrimeArrayLength;
        }

        return GetPrime(newSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(uint hashCode)
    {
        int[] buckets = _buckets!;
#if TARGET_64BIT
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
#else
        return ref buckets[hashCode % buckets.Length];
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FastMod(uint value, uint divisor, ulong multiplier)
    {
        // We use modified Daniel Lemire's fastmod algorithm (https://github.com/dotnet/runtime/pull/406),
        // which allows to avoid the long multiplication if the divisor is less than 2**31.
        Debug.Assert(divisor <= int.MaxValue);

        // This is equivalent of (uint)Math.BigMul(multiplier * value, divisor, out _). This version
        // is faster than BigMul currently because we only need the high bits.
        uint highbits = (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

        Debug.Assert(highbits == value % divisor);
        return highbits;
    }

    public ValueCollection Values => new(_entries ?? [], _count);

    public readonly struct ValueCollection : IEnumerable<TValue>
    {
        private readonly Entry[] vals;
        readonly int count;

        internal ValueCollection(Entry[] vals, int count)
        {
            this.vals = vals;
            this.count = count;
        }

        public TValue this[int index] => vals![index].Value;
        public readonly IEnumerator<TValue> GetEnumerator() => new ValueEnumerator(vals ?? [], count);

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal struct ValueEnumerator(Entry[] vals, int count, int i = -1) : IEnumerator<TValue>
    {
        public readonly TValue Current => vals![i].Value;

        readonly object IEnumerator.Current => Current!;

        public void Dispose()
        {
            i = -1;
        }

        public readonly ValueEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            return ++i < count;
        }

        public void Reset()
        {
            i = -1;
        }
    }

    public KeyCollection Keys => new(_entries ?? [], _count);

    public readonly struct KeyCollection : IEnumerable<TKey>
    {
        private readonly Entry[] vals;
        readonly int count;

        internal KeyCollection(Entry[] vals, int count)
        {
            this.vals = vals;
            this.count = count;
        }

        public TKey this[int index] => vals![index].Key;
        public readonly IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(vals ?? [], count);

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal struct KeyEnumerator(Entry[] vals, int count, int i = -1) : IEnumerator<TKey>
    {
        public readonly TKey Current => vals[i].Key;

        readonly object IEnumerator.Current => Current!;

        public void Dispose() { Reset(); }

        public readonly KeyEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            return ++i < count;
        }

        public void Reset()
        {
            i = -1;
        }
    }

    public void Clear()
    {
        int count = _count;
        if (count > 0)
        {
            Array.Clear(_buckets!, 0, _buckets!.Length);

            _count = 0;
            _freeList = -1;
            _freeCount = 0;

            Array.Clear(_entries!, 0, count);
        }
    }

    public ref TValue GetValueOrInserter(TKey key, out Action<TValue> inserter)
    {
        Entry[]? entries = _entries!;

        uint hashCode = (uint)_comparer.GetHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && _comparer.Equals(key, entries[i].Key))
            {
                inserter = null!;
                return ref entries[i].Value;
            }

            i = entries[i].next;

            collisionCount++;
            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }
        }

        inserter = item =>
        {
            hashCode = (uint)_comparer.GetHashCode(key);
            var entries = _entries!;
            ref int bucket = ref GetBucket(hashCode);
            int index;

            if (_freeCount > 0)
            {
                index = _freeList;
                Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
                _freeList = StartOfFreeList - entries[_freeList].next;
                _freeCount--;
            }
            else
            {
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucket(hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            ref Entry entry = ref entries![index];
            entry.id = hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.Key = key;
            entry.Value = item;
            bucket = index + 1; // Value in _buckets is 1-based
            _version++;
        };

        return ref (new TValue[1])[0];
    }

    IEnumerator<(TKey, TValue)> IEnumerable<(TKey, TValue)>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(this);
    }

    struct Enumerator(Map<TKey, TValue> map) : IEnumerator<(TKey, TValue)>
    {
        int i = -1;
        readonly (TKey, TValue) IEnumerator<(TKey, TValue)>.Current => map._entries![i];

        readonly object IEnumerator.Current => map._entries![i];

        bool IEnumerator.MoveNext() => i++ < map._count;

        void IEnumerator.Reset() => i = 0;

        void IDisposable.Dispose()
        {
            map = null!;
            i = -1;
        }
    }

    public struct Entry
    {
        public TKey Key;
        public TValue Value;
        internal int next;
        internal uint id;

        public static implicit operator (TKey, TValue)(Entry entry) => (entry.Key, entry.Value);
    }
}

internal static class MapExtensions
{
    internal static Map<TKey, TValue> ToMap<TKey, TValue>(this IEnumerable<TValue> values, Func<TValue, TKey> selector, IEqualityComparer<TKey>? keyComparer = null) where TKey : notnull
    {
        Map<TKey, TValue> map = new(keyComparer ?? EqualityComparer<TKey>.Default);
        foreach (var value in values) map.TryAdd(selector(value), value);
        return map;
    }
}
