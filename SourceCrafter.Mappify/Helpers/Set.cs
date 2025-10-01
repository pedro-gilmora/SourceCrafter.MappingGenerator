using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Collections;

public abstract class Set<TValue> : IEnumerable<TValue>
{
    public static Set<TValue> Create<TKey>(
        Func<TValue, TKey> _keyGenerator,
        IEqualityComparer<TKey>? _comparer = null) => new Set<TKey, TValue>(_keyGenerator, _comparer);

    public abstract int Count { get; }

    public abstract bool TryAdd(TValue value);

    internal abstract TValue? this[uint i] { get; }

    public ref TValue? GetValueRefOrAddDefault<TKey>(TKey key, out bool exists)
    {
        return ref ((Set<TKey, TValue>)this).GetValueRefOrAddDefault(key, out exists);
    }

    public ref TValue GetValueOrInsertor<TKey>(TKey key, out Action<TValue> insertor)
    {
        return ref ((Set<TKey, TValue>)this).GetValueOrInsertor(key, out insertor);
    }

    public bool TryGetValue<TKey>(TKey key, out TValue val)
    {
        return ((Set<TKey, TValue>)this).TryGetValue(key, out val);
    }

    public abstract void Clear();

    public abstract IEnumerator<TValue> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class Set<TKey, TValue>(
        Func<TValue, TKey> _keyGenerator,
        IEqualityComparer<TKey>? _comparer = null) : Set<TValue>
{
    Func<TKey, int> getHashCode = _comparer is null
            ? default(TKey) is int or short or byte or uint or ushort
                ? e => Convert.ToInt32(e)
                : e => Convert.ToInt32(EqualityComparer<TKey>.Default.GetHashCode(e))
            : _comparer.GetHashCode;

    Func<TKey, TKey, bool> equals = _comparer is null
        ? default(TKey) is int or short or long or byte or uint or ushort or ulong or double or float
            ? ((TKey a, TKey b) => a!.Equals(b))
            : EqualityComparer<TKey>.Default.Equals
        : _comparer.Equals;
    private int[]? _buckets;
    private Entry[]? _entries;
#if TARGET_64BIT
    private ulong _fastModMultiplier;
#endif
    private int _count;
    private int _freeList;
    private int _freeCount;
    private int _version;
    private const int StartOfFreeList = -3;
    private const int HashPrime = 101;
    private const int MaxPrimeArrayLength = 0x7FFFFFC3;
    internal override TValue? this[uint i] => _count > i ? _entries![i].Value : default;

    internal static ReadOnlySpan<int> Primes => _primes;

    static int[] _primes = [
        3,7,11,17,23,29,37,47,59,71,89,107,131,163,197,239,293,353,431,521,631,761,919,
        1103,1327,1597,1931,2333,2801,3371,4049,4861,5839,7013,8419,10103,12143,14591,
        17519,21023,25229,30293,36353,43627,52361,62851,75431,90523,108631,130363,156437,
        187751,225307,270371,324449,389357,467237,560689,672827,807403,968897,1162687,
        1395263,1674319,2009191,2411033,2893249,3471899,4166287,4999559,5999471,7199369
    ];

    public override int Count => _count;

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

    private static int GetPrime(int min)
    {
        if (min < 0)
            throw new ArgumentException("Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table");

        foreach (int prime in Primes)
        {
            if (prime >= min)
                return prime;
        }

        // Outside of our predefined table. Compute the hard way.
        for (int i = (min | 1); i < int.MaxValue; i += 2)
        {
            if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                return i;
        }
        return min;
    }

    private static bool IsPrime(int candidate)
    {
        if ((candidate & 1) != 0)
        {
            int limit = (int)Math.Sqrt(candidate);
            for (int divisor = 3; divisor <= limit; divisor += 2)
            {
                if ((candidate % divisor) == 0)
                    return false;
            }
            return true;
        }
        return candidate == 2;
    }

    public override bool TryAdd(TValue value)
    {
        if (_buckets is null) Initialize(0);

        Entry[]? entries = _entries!;

        TKey key = _keyGenerator(value);

        var hashCode = getHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket((uint)hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && equals(key, entries[i].Key))
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
            Debug.Assert((StartOfFreeList - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            int count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket((uint)hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref Entry entry = ref entries![index];
        entry.id = (uint)hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = value;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        return true;
    }

    public ref TValue GetValueRefOrAddDefault(TKey key, out bool exists)
    {
        if (_buckets == null)
        {
            Initialize(0);
        }

        Debug.Assert(_buckets != null);

        Entry[] entries = _entries!;

        uint hashCode = (uint)getHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && equals(entries[i].Key, key))
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
                throw new InvalidOperationException("Collision count exceeded length. Concurrent update happened");
            }
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert((StartOfFreeList - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
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
            entries = _entries!;
        }

        ref Entry entry = ref entries![index];
        entry.id = hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = default!;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;


        exists = false;

        return ref entry.Value!;
    }

    public ref TValue GetValueOrInsertor(TKey key, out Action<TValue> insertor)
    {
        if (_buckets is null) Initialize(0);

        Entry[]? entries = _entries!;

        var hashCode = getHashCode(key);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket((uint)hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode && equals(key, entries[i].Key))
            {
                insertor = null!;
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

        insertor = item =>
        {
            hashCode = getHashCode(key = _keyGenerator(item)!);
            var entries = _entries!;
            ref int bucket = ref GetBucket((uint)hashCode);
            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                Debug.Assert((StartOfFreeList - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
                _freeList = StartOfFreeList - entries[_freeList].next;
                _freeCount--;
            }
            else
            {
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucket((uint)hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            ref Entry entry = ref entries![index];
            entry.id = (uint)hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.Key = key;
            entry.Value = item;
            bucket = index + 1; // Value in _buckets is 1-based
            _version++;
        };

        return ref (new TValue[1] { default! })[0];
    }

    public bool TryGetValue(TKey key, out TValue val)
    {
        if (_buckets is null) Initialize(0);

        var hashCode = getHashCode(key);
        int i = GetBucket((uint)hashCode);
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
            if (entry.id == hashCode && equals(entry.Key, key))
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

    private void Resize() => Resize(ExpandPrime(_count), false);

    private void Resize(int newSize, bool forceNewHashCodes)
    {
        // Value types never rehash
        var entries = new Entry[newSize];

        int count = _count;

        Array.Copy(_entries!, entries, count);

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

    private static ulong GetFastModMultiplier(uint divisor) =>
            ulong.MaxValue / divisor + 1;

    private static int ExpandPrime(int oldSize)
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
        return ref buckets[(uint)hashCode % buckets.Length];
#endif
    }

#if TARGET_64BIT
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier)
    {
        // We use modified Daniel Lemire's fastmod algorithm (https://github.com/dotnet/runtime/pull/406),
        // which allows to avoid the long multiplication if the divisor is less than 2**31.
        Debug.Assert(divisor <= int.MaxValue);

        // This is equivalent of (uint)Math.BigMul(multiplier * value, divisor, out _). This version
        // is faster than BigMul currently because we only need the high bits.
        uint highbits = (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

        Debug.Assert(highbits == value % divisor);
        return highbits;
    }
#endif

    public override void Clear()
    {
        if (_buckets is null) Initialize(0);

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

    public TValue this[int i] => i < _count ? _entries![i].Value : throw new ArgumentOutOfRangeException(nameof(i));

    public override IEnumerator<TValue> GetEnumerator()
    {
        return new Enumerator(this);
    }

    public sealed class Enumerator(Set<TKey, TValue> set) : IEnumerator<TValue>
    {
        int i = -1;

        private readonly Set<TKey, TValue> set = set;

        public TValue Current => set._entries![i].Value;

        object IEnumerator.Current => Current!;

        public void Dispose() { }

        public bool MoveNext() => ++i < set._count;

        public void Reset() => i = -1;
    }

    public struct Entry
    {
        public TKey Key;
        public TValue Value;
        internal int next;
        internal uint id;

        public readonly void Deconstruct(out TKey outKey, out TValue outValue) => (outKey, outValue) = (Key, Value);

        public override readonly string ToString() => $"Key: {Key}, Value: {Value}";
    }
}
