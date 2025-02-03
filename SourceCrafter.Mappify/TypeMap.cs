using System;
using System.Collections.Generic;
using System.Text;

namespace SourceCrafter.Mappify;

internal delegate bool CacheCreator(string item, out string cachedItem);
internal delegate void MapperMethodCreator(StringBuilder code, Action<StringBuilder> itemMapper);
enum ConversionType { None, Cast, Mapper }
internal sealed class TypeMap
{
    internal readonly int Id;
    private readonly MapperMethodCreator? _mapper = null, _reverseMapper = null;
    private readonly Action<StringBuilder>? _members = null, _reverseMembers = null;
    private readonly ConversionType _value, _reverseValue;

    private readonly TypeMeta _targetType, _sourceType;
    // private readonly CollectionMapping _collectionMap, _collectionReverseMap;
    private readonly bool
        _areSameType,
        // _isCollection,
        // _isScalar,
        // _hasMapping, 
        // _hasReverseMapping,
        _requiresMethod,
        _requiresReverseMethod,
        _isValid = true;

    private bool _codeCreated, _isExtraCode;

    private readonly string _methodName, _reverseMethodName, _updateMethod, _reverseUpdateMethodName;
    private readonly HashSet<Action<StringBuilder>> _extraMappers = [];
    private readonly bool _isSameType;

    // private readonly bool _useValueCast, _useReverseValueCast;

    public TypeMap(
        Mappers mappers,
        // ReSharper disable once RedundantAssignment
        ref TypeMap @this,
        int id,
        TypeMeta source,
        TypeMeta target,
        GenerateOn ignore,
        bool sourceIsNullable,
        bool targetIsNullable,
        bool dictionaryContext
    ) :

        this(
            mappers,
            ref @this,
            id,
            new(target.Id, "target", target, isNullable: targetIsNullable),
            new(source.Id, "source", source, isNullable: sourceIsNullable),
            ignore, dictionaryContext)
    {
    }

#pragma warning disable CS8618, CS9264
    internal TypeMap(
#pragma warning restore CS8618, CS9264
        Mappers mappers,
        // ReSharper disable once RedundantAssignment
        ref TypeMap @this,
        int id,
        MemberMeta target,
        MemberMeta source,
        GenerateOn ignore,
        bool dictionaryContext
    )
    {
        @this = this;

        var sourceType = source.Type;
        var targetType = target.Type;

        var sameType = _areSameType = targetType.Id == sourceType.Id;

        _updateMethod = _reverseUpdateMethodName = "Update";

        if (_isSameType = sameType)
        {
            _methodName = _reverseMethodName = "Copy";
        }
        else
        {
            _methodName = "To" + targetType.SanitizedName;
            _reverseMethodName = "To" + sourceType.SanitizedName;
        }

        Id = id;
        _targetType = targetType;
        _sourceType = sourceType;

        if (/*_isCollection = */sourceType.IsCollection || targetType.IsCollection)
        {
            _isValid = false;
            return;
            //     var itemMap = mappers.GetOrAdd(
            //         sourceType.Collection.ItemType, 
            //         targetType.Collection.ItemType, 
            //         ignore,
            //         sourceType.Collection.IsItemNullable, 
            //         targetType.Collection.IsItemNullable);
            //
            //     if (!itemMap._isValid ||
            //         !(sourceType.Collection.ItemType.HasZeroArgsCtor && targetType.Collection.ItemType.HasZeroArgsCtor))
            //     {
            //         _isValid = false;
            //
            //         return;
            //     }
            //
            //     itemMap._targetType.IsRecursive |= itemMap._targetType.IsRecursive;
            //     itemMap._sourceType.IsRecursive |= itemMap._sourceType.IsRecursive;
            //
            //     // var collectionMap = BuildCollectionMapping(sourceType.Collection, targetType.Collection, _methodName);
            //     // var collectionReverseMap = BuildCollectionMapping(targetType.Collection, sourceType.Collection, _reverseMethodName);
            //
            //     MemberMeta  
            //         sourceItemMember = new(id, "sourceItem", itemMap._sourceType),
            //         targetItemMember = new(id, "targetItem", itemMap._targetType);
            //
            //     if (sourceItemMember.Discard(targetItemMember, true, out var sourceCtx, out var targetCtx))
            //     {
            //         _isValid = false;
            //         return;
            //     }
            //
            //     if (_isValid = !targetCtx.Ignore)
            //     {
            //         _requiresMapperMethod = true;
            //         
            //         _value = (code, sourceItem, targetItem) => 
            //             code.Append(@"
            // ").Append(_methodName).Append('(').Append(targetItem).Append(", ").Append(sourceItem).Append(')');
            //     };
            //
            //     if (!(_isValid |= !sameType && !sourceCtx.Ignore)) return;
            //
            //     _requiresReverseMapperMethod = true;
            //     
            //         _reverseValue = (code, sourceItem, targetItem) => 
            //             AppendMethodCall(code, sourceType.IsValueType, _reverseMethodName, sourceItem).Append(", ").Append(targetItem).Append(')');
            //         
            //
            //     return;
        }

        if (targetType.HasConversion(sourceType, out var scalarConversion, out var reverseScalarConversion))
        {
            if (scalarConversion.Exists)
            {
                _value = (!targetType.IsInterface && scalarConversion.IsExplicit)
                    ? ConversionType.Cast
                    : ConversionType.None;

                _isValid = true;
            }

            if (reverseScalarConversion.Exists)
            {
                _reverseValue = (!sourceType.IsInterface && reverseScalarConversion.IsExplicit)
                    ? ConversionType.Cast
                    : ConversionType.None;

                _isValid = true;
            }
        }

        if (_targetType.IsPrimitive || _sourceType.IsPrimitive || sourceType.IsMemberless || targetType.IsMemberless)
        {
            return;
        }

        _requiresMethod = _requiresReverseMethod = true;

        if (!scalarConversion.Exists) _value = ConversionType.Mapper;

        if (!reverseScalarConversion.Exists) _reverseValue = ConversionType.Mapper;

        _mapper = new MapperMethod(targetType, sourceType, _methodName).BuildMethods;

        _reverseMapper = new MapperMethod(sourceType, targetType, _reverseMethodName).BuildMethods;

        var allowLowerCase = sourceType.IsTupleType || targetType.IsTupleType /*, hasMatches = false*/;

        var canUseUnsafeAccessor = mappers.CanUseUnsafeAccessor;

        foreach (var targetMember in targetType.Members)
        {
            foreach (var sourceMember in sourceType.Members)
            {
                var map = this;

                if (targetMember.IsMatchingContext(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var isTargetAssignable, out var isSourceAssignable) 
                    && (Id == GetId(sourceMember.Type.Id, sourceMember.Type.Id)
                        || (map = mappers.GetOrAdd(targetMember, sourceMember, ignore))._isValid))
                {
                    var (requiresMethod, copyMethod, updateMethod, requiresReverseMethod, reverseMethod, reverseUpdateMethod, appendValue, reverseAppendValue) =
                        (map._targetType.Id, map._sourceType.Id) == (targetMember.Type.Id, sourceMember.Type.Id)
                            ? (map._requiresMethod, map._methodName, map._updateMethod, map._requiresReverseMethod, map._reverseMethodName, map._reverseUpdateMethodName, map._value, map._reverseValue)
                            : (map._requiresReverseMethod, map._reverseMethodName, map._reverseUpdateMethodName, map._requiresMethod, map._methodName, map._updateMethod, map._reverseValue, map._value);

                    if (isTargetAssignable)
                    {
                        _isValid = true;
                        _members += new Assignment(targetMember,sourceMember,copyMethod, updateMethod, requiresMethod, appendValue!).Assign;
                    }

                    if (isSourceAssignable)
                    {
                        _isValid = true;
                        _reverseMembers += new Assignment(sourceMember, targetMember, reverseMethod, reverseUpdateMethod, requiresReverseMethod, reverseAppendValue!).Assign;
                    }

                    if (map.IsExtraCodeFor(this))
                    {
                        map._isExtraCode = true;
                        mappers.Types.UnsafeAccessors.Add(new(map.ToString(), map.BuildMethods));
                    }

                    break;
                }
            }
        }

    }

    private bool IsExtraCodeFor(TypeMap other)
    {
        return other != this &&
               !_sourceType.IsMemberless && !_targetType.IsMemberless &&
               (_requiresMethod || _requiresReverseMethod) &&
               !_isExtraCode;
    }

    //public bool CreateCollectionMapBuilders(
    //    MemberMeta source,
    //    MemberMeta target,
    //    MemberMeta sourceItem,
    //    MemberMeta targetItem,
    //    in CollectionMeta sourceCollInfo,
    //    in CollectionMapping collMapInfo,
    //    ValueBuilder buildItemValue,
    //    out MapperMethodCreator methodCreator,
    //    out ValueBuilder valueBuilder)
    //{
    //    var (itemType, type, isItemNullable, indexable, countable, backingArray, method, countProp, isSourceDictionary) = sourceCollInfo;

    //    string
    //        targetFullTypeName = target.Type.FullName,
    //        sourceFullTypeName = source.Type.FullName,
    //        targetItemFullTypeName = itemType.FullName,
    //        copyMethodName = collMapInfo.MethodName,
    //        updateMethodName = collMapInfo.MethodName;

    //    var addMethod = collMapInfo.Method;

    //    bool createArray = collMapInfo.CreateArray,
    //         redim = collMapInfo.Redim,
    //         isTargetValueType = target.Type.IsValueType;

    //    bool isItemTypeRecursive = itemType.IsRecursive;

    //    bool isFor = collMapInfo.Iterator == "for";
    //    // Consolidamos todos los datos en una estructura inmutable

    //    Assignment state = new(target, source, copyMethodName, updateMethodName);

    //    string
    //        targetExportFullXmlDocTypeName = targetFullTypeName.Replace("<", "{").Replace(">", "}"),
    //        sourceExportFullXmlDocTypeName = sourceFullTypeName.Replace("<", "{").Replace(">", "}"),
    //        underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

    //    (string defaultType, string initType, Action<StringBuilder, string> returnExpr) = (type, target.Type.IsInterface) switch
    //    {
    //        (EnumerableType.ReadOnlyCollection, true) =>
    //             ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyReadOnlyCollection",
    //              underlyingCollectionType,
    //              (code, v) => code.Append("new global::System.Collections.ObjectModel.ReadOnlyCollection<").Append(targetItemFullTypeName).Append(">(").Append(v).Append(")")),
    //        (EnumerableType.Collection, true) =>
    //             ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyCollection",
    //              underlyingCollectionType,
    //              (code, v) => code.Append(v)),
    //        _ => ("new " + targetFullTypeName + "()", 
    //              targetFullTypeName + "()", 
    //              new Action<StringBuilder, string>((code, v) => code.Append(v)))
    //    };

    //    //User? <== UserDto?
    //    var checkNull = (!targetItem.IsNullable || !itemType.IsValueType) && sourceItem.IsNullable;

    //    string? suffix = (type, type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null;

    //    void buildCopy(StringBuilder code)
    //    {

    //        if (isSourceDictionary)
    //        {
    //            code.Append(@"
    //    /// <summary>
    //    /// Creates a new instance of <see cref=""")
    //                .Append(targetExportFullXmlDocTypeName)
    //                .Append(@"""/> based from a given <see cref=""")
    //                .Append(sourceExportFullXmlDocTypeName)
    //                .Append(@"""/>
    //    /// </summary>
    //    /// <param name=""source"">Data source to be mapped</param>");

    //            if (isItemTypeRecursive)
    //            {
    //                code.Append(@"
    //    /// <param name=""depth"">Depth index for recursion control</param>
    //    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
    //            }

    //            code.Append(@"
    //    public static ")
    //                .Append(targetFullTypeName)
    //                .AddSpace()
    //                .Append(copyMethodName)
    //                .Append('(');

    //            if (isTargetValueType) code.Append("ref ");

    //            code.Append("this ")
    //                .Append(sourceFullTypeName)
    //                .Append(" source");

    //            if (isItemTypeRecursive)
    //            {
    //                code.Append(", int depth = 0, int maxDepth = ")
    //                    .Append(target.MaxDepth)
    //                    .Append(@")
    //    {
    //        if (depth >= maxDepth) 
    //            return ").Append(defaultType).Append(@";
    //");
    //            }
    //            else
    //            {
    //                code.Append(@")
    //    {");
    //            }

    //            code.Append(@"
    //        var target = ").Append(defaultType).Append(@";

    //        foreach (var item in source)
    //        {
    //            target[");

    //            //keyValueMapping!.Key.Invoke(code, "item");

    //            code.Append("] = ");

    //            //keyValueMapping!.Value(code, "item");

    //            code.Append(@";
    //        }

    //        return target;
    //    }
    //");
    //            return;
    //        }

    //        code.Append(@"
    //    /// <summary>
    //    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    //    /// </summary>
    //    /// <param name=""source"">Data source to be mapped</param>");

    //        if (isItemTypeRecursive)
    //            code.Append(@"
    //    /// <param name=""depth"">Depth index for recursion control</param>
    //    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

    //        code.Append(@"
    //    public static ").Append(targetFullTypeName).AddSpace().Append("Update(");

    //        if (target.Type.IsValueType) code.Append("ref ");

    //        code.Append("this ").Append(sourceFullTypeName).Append(@" target");

    //        if (isItemTypeRecursive)
    //        {
    //            code.Append(", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    //    {
    //        if (depth >= maxDepth) 
    //            return ");

    //            if (createArray)
    //            {
    //                code.Append("global::System.Array.Empty<").Append(targetItemFullTypeName).Append(">()");
    //            }
    //            else
    //            {
    //                code.Append(defaultType);
    //            }

    //            code.Append(@";
    //");
    //        }
    //        else
    //        {
    //            code.Append(@")
    //    {");
    //        }

    //        if (createArray)
    //        {
    //            if (redim)
    //            {
    //                code.Append(@"
    //        int len = 0, aux = 16;
    //        var target = new ").Append(targetItemFullTypeName).Append(@"[aux];
    //");
    //            }
    //            else
    //            {
    //                code.Append(@"
    //        int len = ");

    //                if (isFor)
    //                {
    //                    code.Append("source.").Append(countProp);
    //                }
    //                else
    //                {
    //                    code.Append(0);
    //                }

    //                code.Append(@";
    //        var target = new ").Append(targetItemFullTypeName).Append('[');

    //                if (isFor)
    //                {
    //                    code.Append("len");
    //                }
    //                else
    //                {
    //                    code.Append("source.").Append(countProp);
    //                }

    //                code.Append(@"];
    //");
    //            }
    //        }
    //        else
    //        {
    //            code.Append(@"
    //        var target = new ").Append(initType).Append(';').Append(@"
    //");
    //        }

    //        if (isFor)
    //        {
    //            code.Append(@"
    //        for (int i = 0; i < len; i++)
    //        {
    //            target[i] = ");

    //            buildItemValue(code);

    //            code.Append(@";
    //        }

    //        return target").Append(suffix).Append(@";
    //    }
    //");
    //        }
    //        else
    //        {
    //            code.Append(@"
    //        foreach (var item in source)
    //        {");

    //            if (createArray)
    //            {
    //                code.Append(@"
    //            target[len");

    //                if (!redim)
    //                {
    //                    code.Append("++");
    //                }

    //                code.Append("] = ");

    //                buildItemValue(code);

    //                code.Append(";");

    //                if (redim)
    //                {
    //                    //redim array
    //                    code.Append(@"

    //            if (aux == ++len)
    //                global::System.Array.Resize(ref target, aux *= 2);
    //        }

    //        return (len < aux ? target[..len] : target)").Append(suffix).Append(@";
    //    }
    //");
    //                }
    //                //normal ending
    //                else
    //                {
    //                    code.Append(@"
    //        }

    //        return target").Append(suffix).Append(@";
    //    }
    //");
    //                }
    //            }
    //            else
    //            {
    //                code.Append(@"
    //            target.").Append(addMethod);


    //                buildItemValue(code);

    //                code.Append(@");
    //        }

    //        return ");

    //                returnExpr(code, "target");

    //                code.Append(@";
    //    }
    //");
    //            }
    //        }
    //    }

    //    void value(in Assignment state, StringBuilder code, bool checkNull = false)
    //    {
    //        code.Append(copyMethodName).Append("(");

    //        if (isItemTypeRecursive)
    //        {
    //            code.Append("__l - 1");
    //        }

    //        code.Append(")");
    //    };

    //    valueBuilder = value;

    //    methodCreator = buildCopy;

    //    return true;
    //}

    readonly struct MapperMethod(
        TypeMeta targetType,
        TypeMeta sourceType,
        string methodName)
    {


        readonly bool 
            isInterface = targetType.IsInterface,
            isTargetTypeRecursive = targetType.IsRecursive,
            isSourceValueType = sourceType.IsValueType,
            isTargetValueType = targetType.IsValueType;
        readonly string
            targetFullTypeName = targetType.FullName, 
            sourceFullTypeName = sourceType.FullName;

        internal void BuildMethods(StringBuilder code, Action<StringBuilder> targetMembers)
        {
            if (!isInterface)
            {
                code.Append(@"
    public static ")
                    .Append(targetFullTypeName)
                .Append(" ")
                    .Append(methodName).Append('(');

                if (isSourceValueType) code.Append("in ");

                code.Append("this ")
                    .Append(sourceFullTypeName)
                    .Append(@" source)
    {
        ");

                if (isTargetValueType)
                    code.Append(targetFullTypeName)
                        .Append(@" init = default;

        return Update(ref init, source)");

                else
                    code.Append("return Update(new ")
                        .Append(targetFullTypeName)
                        .Append("(), source)");

                code.Append(@";
    }
");
            }

            code.Append(@"
    public static ")
                .Append(targetFullTypeName)
                .Append(" Update(");

            if (isTargetValueType) code.Append("ref ");

            code.Append("this ")
                .Append(targetFullTypeName)
                .Append(" target, ");

            if (isSourceValueType) code.Append("in ");
            
            code.Append(sourceFullTypeName)
                .Append(@" source");

            if (isTargetTypeRecursive) code.Append(", int __l = 0");

            code.Append(@")
    {");

            targetMembers(code);

            code.Append(@"

        return target;
    }
");
        }
    }

    //readonly struct ValueState(
    //    string )
    //{

    //}


    private static bool CacheMemberItem(string item, out string cachedItem)
    {
        cachedItem = item.Replace(".", "");

        return cachedItem.Length < item.Length;
    }

    private static bool CacheIndexedItem(string item, out string cachedItem)
    {
        if (item.IndexOf('[') is > -1 and var idx)
        {
            cachedItem = '_' + item[..idx] + "Item";
            return true;
        }

        cachedItem = null!;
        return false;
    }

    //private static CollectionMapping BuildCollectionMapping(CollectionMeta source, CollectionMeta target,
    //    string copyMethodName)
    //{
    //    var isDictionary = (target.IsDictionary && source.IsDictionary)
    //                       || (target.IsDictionary && isTupleEnumerator(source.ItemType))
    //                       || (source.IsDictionary && isTupleEnumerator(target.ItemType));

    //    var iterator = !isDictionary && source.Indexable && target.BackingArray ? "for" : "foreach";

    //    return new(
    //        target.BackingArray,
    //        target.BackingArray && !source.Indexable,
    //        iterator,
    //        !source.Countable && target.BackingArray,
    //        target.Method,
    //        copyMethodName);

    //    static bool isTupleEnumerator(TypeMeta itemType) =>
    //        itemType is { Symbol: INamedTypeSymbol { IsTupleType: true, TupleElements.Length: 2 } };
    //}

    internal static int GetId(int typeAId, int typeBId) =>
        (Math.Min(typeAId, typeBId), Math.Max(typeAId, typeBId)).GetHashCode();

    public void BuildFile(Action<string, string> addSource, int i)
    {
        StringBuilder code = new();

        BuildMethods(code);

        if (code.Length == 0) return;

        code.Insert(0, @"namespace SourceCrafter.Mappify;

public static partial class Mappings
{");

        addSource(
            $"{i}_{_sourceType.Symbol.MetadataName}_{_targetType.Symbol.MetadataName}",
            code.Append("}").ToString());
    }

    private void BuildMethods(StringBuilder code)
    {
        if (!_isValid || _codeCreated) return;

        _codeCreated = true;

        if(_members is not null) _mapper?.Invoke(code, _members);

        if (!_areSameType && _reverseMembers is not null) _reverseMapper?.Invoke(code, _reverseMembers);

        foreach (var extraMapper in _extraMappers)
        {
            extraMapper(code);
        }
    }

    public override string ToString()
    {
        return $"{_sourceType.ExportFullName} <=> {_targetType.ExportFullName}";
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
