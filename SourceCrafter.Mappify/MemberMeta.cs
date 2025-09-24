using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SourceCrafter.Mappify;

internal class MemberMeta(
    int id,
    string name,
    TypeMeta type,
    TypeMeta? owningType = null,
    bool isNullable = false,
    HashSet<int>? manualMatches = null,
    HashSet<int>? ignoreFor = null,
    bool canRead = true,
    bool canWrite = true,
    bool useUnsafeAccessor = false,
    short maxDepth = 1,
    string privateFieldMethodName = "") : IEquatable<MemberMeta>
{
    private readonly int _id = id;

    private readonly HashSet<int> _ignores = ignoreFor ?? [];
    HashSet<int> _matches = manualMatches ?? [];

    internal readonly bool 
        CanRead = canRead, 
        CanWrite = canWrite,
        IsNullable = isNullable,
        UseUnsafeAccessor = useUnsafeAccessor, 
        IsParentValueType = owningType?.IsKeyValueType is true;

    internal readonly TypeMeta Type = type;

    internal readonly int HashCode = (type.Id, name).GetHashCode();

    internal readonly string Name = name, UnsafeFieldAccesor = privateFieldMethodName;

    internal readonly short MaxDepth = maxDepth;
    
    internal bool IsParentTypeRecursive => owningType?.IsRecursive is true;

    internal bool Matches(MemberMeta source, bool ignoreCase, bool canUseUnsafeAccessor, out bool isTargetAssignable, out bool isSourceAssignable)
    {
        if (_id != source._id
            && !Name.Equals(source.Name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !source.Name.Equals(Type.Name + Name)
            && !Name.Equals(source.Type.Name + source.Name)
            && !source._matches.Contains(_id)
            && !_matches.Contains(source._id))
        {
            return isTargetAssignable = isSourceAssignable = false;
        }

        bool matchedByAttribute = source._matches.Contains(_id) || _matches.Contains(source._id),
            targetIgnoresSource = _ignores.Contains(source._id),
            sourceIgnoresTarget = source._ignores.Contains(_id),
            targetCanBeAssigned = source.CanRead && (CanWrite || (UseUnsafeAccessor && canUseUnsafeAccessor)),
            sourceCanBeAssigned = CanRead && (source.CanWrite || (source.UseUnsafeAccessor && canUseUnsafeAccessor));        

        return (isTargetAssignable = (matchedByAttribute || !targetIgnoresSource) && targetCanBeAssigned)
            | (isSourceAssignable = (matchedByAttribute || !sourceIgnoresTarget) && sourceCanBeAssigned);
    }

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
        return (obj as MemberMeta)?.Equals(this) ?? false;
    }


    public override int GetHashCode()
    {
        return (Type.Id, Name).GetHashCode();
    }

    public bool Equals(MemberMeta b)
    {
        return Type.Symbol.IsRelatedTo(b.Type.Symbol)
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