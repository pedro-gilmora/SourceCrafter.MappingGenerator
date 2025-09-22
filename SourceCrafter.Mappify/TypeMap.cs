using System;
using System.Collections.Generic;
using System.Text;

namespace SourceCrafter.Mappify;

internal delegate bool CacheCreator(string item, out string cachedItem);
internal delegate void MapperMethodCreator(StringBuilder code, Action<StringBuilder> itemMapper);
enum ConversionType { None, Cast, Mapper }
internal sealed class TypeMap(
    int mapperId,
    TypeMeta targetType,
    TypeMeta sourceType,
    BuildValue value,
    BuildValue reverseValue,
    string methodName,
    string reverseMethodName)
{
    internal readonly int Id = mapperId, TargetTypeId = targetType.Id, SourceTypeId = sourceType.Id;
    
    private readonly bool targetHasMember = !targetType.IsMemberless, sourceHasMembers = !sourceType.IsMemberless;
    
    internal bool IsValid = true;

    internal readonly string MethodName = methodName, ReverseMethodName = reverseMethodName;
  
    internal void GetMeta(
        int targetTypeId,
        int sourceTypeId,
        out bool useMethod,
        out bool useReverseMethod,
        out BuildValue asValue,
        out BuildValue asReverseValue)
    {
        (useMethod, useReverseMethod, asValue, asReverseValue) =
            (TargetTypeId, SourceTypeId) == (targetTypeId, sourceTypeId)
                ? (targetHasMember, sourceHasMembers, value, reverseValue)
                : (sourceHasMembers, targetHasMember, reverseValue, value);
    }

    public override string ToString()
    {
        return $"{sourceType.ExportFullName} <=> {targetType.ExportFullName}";
    }
}

internal readonly record struct CollectionMeta(
    TypeMeta ItemType,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool Countable,
    bool BackingArray,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Type is EnumerableType.Dictionary;

    public void Deconstruct(
        out TypeMeta itemType,
        out EnumerableType type,
        out bool isItemNullable,
        out bool indexable,
        out bool countable,
        out bool backingArray,
        out string? method,
        out string countProp,
        out bool isDictionary)
    {
        itemType = ItemType;
        type = Type;
        isItemNullable = IsItemNullable;
        indexable = Indexable;
        countable = Countable;
        backingArray = BackingArray;
        method = Method;
        countProp = CountProp;
        isDictionary = IsDictionary;
    }
};

internal delegate void ValueBuilder(StringBuilder code, bool preventNullCheck = false);

internal record struct CollectionMapping(
    bool CreateArray,
    bool UseLenInsteadOfIndex,
    string Iterator,
    bool Redim,
    string? Method,
    string MethodName);
