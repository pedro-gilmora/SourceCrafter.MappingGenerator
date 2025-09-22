using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Collections;


// ReSharper disable once CheckNamespace
namespace SourceCrafter;

internal abstract class Set<TValue> : IEnumerable<TValue>
{

    public static Set<TValue> Create<TKey>(Func<TValue, TKey> keySelector) => new Set<TKey, TValue>(keySelector);

    public abstract int Count { get; }

    public abstract bool TryAdd(TValue value);

    public ref TValue GetOrAddDefault<TKey>(TKey key, out bool exists)
    {
        return ref ((Set<TKey, TValue>)this).GetOrAddDefault(key, out exists);
    }

    public ref TValue GetValueOrInserter<TKey>(TKey key, out Action<TValue> insertor)
    {
        return ref ((Set<TKey, TValue>)this).GetValueOrInserter(key, out insertor);
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

internal class Set<TKey, TValue> : Set<TValue>
{
    private readonly Func<TKey, int> _getHashCode;
    private readonly Func<int, TKey, bool> _equals;
    
    private int[]? _buckets;
    private Entry[]? _entries;
#if TARGET_64BIT
    private ulong _fastModMultiplier;
#endif
    private int _count;
    private int _freeList;
    private int _freeCount;
    private int _version;
    private readonly Func<TValue, TKey> _keySelector;

    public Set(Func<TValue, TKey> keySelector)
    {
        _keySelector = keySelector;
        
        _getHashCode = default(TKey) is int or short or byte or uint or ushort
            ? e => Convert.ToInt32(e)
            : e => EqualityComparer<TKey>.Default.GetHashCode(e);

        _equals = (a, b) => a == _getHashCode(b);
    }

    private const int StartOfFreeList = -3;
    private const int HashPrime = 101;
    private const int MaxPrimeArrayLength = 0x7FFFFFC3;

    public override int Count => _count;

    private int Initialize(int capacity)
    {
        var size = GetPrime(capacity);
        var buckets = new int[size];
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
            throw new ArgumentException("Hashtable capacity overflowed and went negative. Check load factor, capacity and the current size of the table");

        foreach (var prime in SourceCrafter.Helpers.Primes)
        {
            if (prime >= min)
                return prime;
        }

        // Outside our predefined table. Compute the hard way.
        for (var i = min | 1; i < int.MaxValue; i += 2)
        {
            if (IsPrime(i) && (i - 1) % HashPrime != 0)
                return i;
        }
        return min;
    }

    private static bool IsPrime(int candidate)
    {
        if ((candidate & 1) != 0)
        {
            var limit = (int)Math.Sqrt(candidate);
            for (var divisor = 3; divisor <= limit; divisor += 2)
            {
                if (candidate % divisor == 0)
                    return false;
            }
            return true;
        }
        return candidate == 2;
    }

    public override bool TryAdd(TValue value)
    {
        if (_buckets is null) Initialize(0);

        var entries = _entries!;

        var key = _keySelector(value);

        var hashCode = _getHashCode(key);

        uint collisionCount = 0;
        ref var bucket = ref GetBucket((uint)hashCode);
        var i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode)
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
            var count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket((uint)hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref var entry = ref entries![index];
        entry.id = (uint)hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = value;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        return true;
    }

    public ref TValue GetOrAddDefault(TKey key, out bool exists)
    {
        if (_buckets == null)
        {
            Initialize(0);
        }

        var entries = _entries!;

        var hashCode = (uint)_getHashCode(key);

        uint collisionCount = 0;
        ref var bucket = ref GetBucket(hashCode);
        var i = bucket - 1; // Value in _buckets is 1-based
        
        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode)
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
            Debug.Assert((StartOfFreeList - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            var count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = count;
            _count = count + 1;
            entries = _entries;
        }

        ref var entry = ref entries![index];
        entry.id = hashCode;
        entry.next = bucket - 1; // Value in _buckets is 1-based
        entry.Key = key;
        entry.Value = default!;
        bucket = index + 1; // Value in _buckets is 1-based
        _version++;

        exists = false;

        return ref entry.Value!;
        // if (_buckets is null) Initialize(0);
        //
        // var entries = _entries!;
        //
        // var hashCode = _getHashCode(key!);
        //
        // var collisionCount = 0u;
        // ref var bucket = ref GetBucket((uint)hashCode);
        // var i = bucket - 1; // Value in _buckets is 1-based
        //
        //
        // while ((uint)i < (uint)entries.Length)
        // {
        //     if (entries[i].id == hashCode && _equals(key, entries[i].Key))
        //     {
        //         exists = true;
        //
        //         return ref entries[i].Value!;
        //     }
        //
        //     i = entries[i].next;
        //
        //     collisionCount++;
        //     if (collisionCount > (uint)entries.Length)
        //     {
        //         // The chain of entries forms a loop; which means a concurrent update has happened.
        //         // Break out of the loop and throw, rather than looping forever.
        //         throw new NotSupportedException("Concurrent operations are not allowed");
        //     }
        // }
        //
        // int index;
        // if (_freeCount > 0)
        // {
        //     index = _freeList;
        //     Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
        //     _freeList = StartOfFreeList - entries[_freeList].next;
        //     _freeCount--;
        // }
        // else
        // {
        //     var count = _count;
        //     if (count == entries.Length)
        //     {
        //         Resize();
        //         bucket = ref GetBucket((uint)hashCode);
        //     }
        //     index = count;
        //     _count = count + 1;
        //     entries = _entries;
        // }
        //
        // ref var entry = ref entries![index];
        // entry.id = (uint)hashCode;
        // entry.next = bucket - 1; // Value in _buckets is 1-based
        // entry.Key = key;
        // bucket = index + 1; // Value in _buckets is 1-based
        // _version++;
        //
        // exists = false;
        //
        // return ref entry.Value!;
    }

    public ref TValue GetValueOrInserter(TKey key, out Action<TValue> insertor)
    {
        if (_buckets is null) Initialize(0);

        var entries = _entries!;

        var hashCode = _getHashCode(key);

        uint collisionCount = 0;
        ref var bucket = ref GetBucket((uint)hashCode);
        var i = bucket - 1; // Value in _buckets is 1-based


        while ((uint)i < (uint)entries.Length)
        {
            if (entries[i].id == hashCode)
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
            hashCode = _getHashCode(key = _keySelector(item)!);
            var entries = _entries!;
            ref var bucket = ref GetBucket((uint)hashCode);
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
                var count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucket((uint)hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            ref var entry = ref entries![index];
            entry.id = (uint)hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.Key = key;
            entry.Value = item;
            bucket = index + 1; // Value in _buckets is 1-based
            _version++;
        };

        return ref Unsafe.NullRef<TValue>();
    }

    public bool TryGetValue(TKey key, out TValue val)
    {
        if (_buckets is null) Initialize(0);

        var hashCode = _getHashCode(key);
        var i = GetBucket((uint)hashCode);
        var entries = _entries;
        uint collisionCount = 0;
        i--; // Value in _buckets is 1-based; subtract 1 from [i]. We do it here so it fuses with the following conditional.
        do
        {
            // Test in if to drop range check for following array access
            if ((uint)i >= (uint)entries!.Length)
            {
                val = default!;
                return false;
            }

            ref var entry = ref entries[i];
            if (entry.id == hashCode)
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

        var count = _count;

        Array.Copy(_entries!, entries, count);

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _buckets = new int[newSize];

#if TARGET_64BIT
        _fastModMultiplier = GetFastModMultiplier((uint)newSize);
#endif

        for (var i = 0; i < count; i++)
        {
            if (entries[i].next >= -1)
            {
                ref var bucket = ref GetBucket(entries[i].id);
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
        var newSize = 2 * oldSize;

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
        var buckets = _buckets!;
#if TARGET_64BIT
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
#else
        return ref buckets[hashCode % buckets.Length];
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

        var count = _count;

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
