using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SourceCrafter.Mappify;

internal sealed class MemberMeta(
    int id,
    string name,
    TypeMeta type,
    TypeMeta? owningType = default,
    bool isNullable = false,
    Dictionary<int, bool?>? manualMatches = null,
    HashSet<int>? ignoreFor = null,
    bool canRead = true,
    bool canWrite = true,
    bool useUnsafeAccessor = false,
    short maxDepth = 1,
    string privateFieldMethodName = "",
    bool isKey = false,
    bool isValue = false)
{
    private readonly int _id = id;
    private readonly string debugString = owningType is { ExportFullName: var fullName } ? $"{fullName}.{name}" : name;
    private readonly HashSet<int> _ignores = ignoreFor ?? [];
    private readonly Dictionary<int, bool?> _matches = manualMatches ?? [];

    internal readonly bool
        CanRead = canRead,
        CanWrite = canWrite,
        IsNullable = isNullable,
        UseUnsafeAccessor = useUnsafeAccessor,
        IsParentValueType = owningType?.IsValueType ?? false,
        IsKey = isKey,
        IsValue = isValue;

    internal readonly TypeMeta Type = type;

    internal readonly int HashCode = (type.Id, name).GetHashCode();

    internal readonly string Name = name, UnsafeFieldAccesor = privateFieldMethodName;

    internal readonly short MaxDepth = maxDepth;

    internal readonly bool IsParentTypeRecursive = owningType?.IsRecursive ?? false;

    internal bool Matches(MemberMeta source, bool ignoreCase, bool canUseUnsafeAccessor, out MemberMatch targetMatch, out MemberMatch sourceMatch)
    {
        var targetMatchesSource = _matches.TryGetValue(source._id, out var targetAllowsNullSource);
        var sourceMatchesTarget = source._matches.TryGetValue(_id, out var sourceAllowsNullTarget);

        targetMatch = sourceMatch = default;

        if (_id != source._id
            && !Name.Equals(source.Name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !source.Name.Equals(Type.Name + Name)
            && !Name.Equals(source.Type.Name + source.Name)
            && !sourceMatchesTarget
            && !targetMatchesSource)
        {
            targetMatch = sourceMatch = default;
            return false;
        }

        bool matchedByAttribute = targetMatchesSource || sourceMatchesTarget,
            targetIgnoresSource = _ignores.Contains(source._id),
            sourceIgnoresTarget = source._ignores.Contains(_id),
            targetCanBeAssigned = source.CanRead && (CanWrite || (UseUnsafeAccessor && canUseUnsafeAccessor)),
            sourceCanBeAssigned = CanRead && (source.CanWrite || (source.UseUnsafeAccessor && canUseUnsafeAccessor));

        var result = matchedByAttribute;
        
        if ((matchedByAttribute || !targetIgnoresSource) && targetCanBeAssigned)
        {
            targetMatch.IsAssignable = true;
            targetMatch.AllowNull = targetAllowsNullSource ?? false;
            result = true;
        }

        if ((matchedByAttribute || !sourceIgnoresTarget) && sourceCanBeAssigned)
        {
            sourceMatch.IsAssignable = true;
            sourceMatch.AllowNull = sourceAllowsNullTarget ?? false;
            result = true;
        }
        
        return result;
    }

    public override string ToString() => debugString;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(in MemberMeta a, in MemberMeta b)
    {
        return a.Equals(b);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(in MemberMeta a, in MemberMeta b)
    {
        return !a.Equals(b);
    }

    public override bool Equals(object? obj)
    {
        return obj is MemberMeta meta && meta.Equals(this);
    }


    public override int GetHashCode()
    {
        return (Type.Id, Name).GetHashCode();
    }

    public bool Equals(MemberMeta b)
    {
        return Type.Symbol.InheritsOrImplements(b.Type.Symbol)
            && Name.Equals(b.Name, Type.IsTupleType || b.Type.Symbol.IsTupleType ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

internal readonly struct MemberContext(
    in MemberMeta target,
    in MemberMeta source,
    bool ignore,
    string? defaultBang,
    string? bang)
{
    private readonly MemberMeta _target = target, _source = source;
    internal readonly bool IsValid = ignore;

    public MemberContext SetupNullability(out bool checkNull, out string? outBang)
    {
        outBang = (checkNull = _target.IsNullable && !_source.IsNullable)
            ? defaultBang
            : bang;
        return this;
    }
}