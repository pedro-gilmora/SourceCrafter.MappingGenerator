using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SourceCrafter.Mappify;

internal delegate bool CacheCreator(string item, out string cachedItem);

internal sealed class TypeMap
{
    //private MappingKind _mappingKind;
    // private readonly Mappers _mappers;
    internal readonly int Id;
    //private readonly bool _ignoreTargetType, _ignoreSourceType;
    //private readonly int _targetMemberCount, _sourceMemberCount;
    // internal bool AddTargetTryGet, AddSourceTryGet;
    //private readonly bool isTargetRecursive, isSourceRecursive, hasComplexSTTMembers;
    private readonly Action<StringBuilder, Action<StringBuilder>>? _mapper = null, _reverseMapper = null;
    private readonly Action<StringBuilder>? _members = null, _reverseMembers = null;
    private readonly ValueBuilder _value, _reverseValue;

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
                    ? Assignment.AsCast
                    : Assignment.AsValue;

                _isValid = true;
            }

            if (reverseScalarConversion.Exists)
            {
                _reverseValue = (!sourceType.IsInterface && reverseScalarConversion.IsExplicit)
                    ? Assignment.AsCast
                    : Assignment.AsValue;

                _isValid = true;
            }
        }

        if (_targetType.IsPrimitive || _sourceType.IsPrimitive || sourceType.IsMemberless || targetType.IsMemberless)
        {
            return;
        }

        _requiresMethod = _requiresReverseMethod = true;

        if (!scalarConversion.Exists) _value = Assignment.AsMapper;

        if (!reverseScalarConversion.Exists) _reverseValue = Assignment.AsMapper;

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

                    if (isTargetAssignable
                        && TryBuildMemberAssignment(
                            targetMember,
                            sourceMember,
                            requiresMethod,
                            copyMethod,
                            updateMethod,
                            appendValue!,
                            out var memberAssignment))
                    {
                        _isValid = true;
                        _members += memberAssignment;
                    }

                    if (isSourceAssignable
                        && TryBuildMemberAssignment(
                            sourceMember,
                            targetMember,
                            requiresReverseMethod,
                            reverseMethod,
                            reverseUpdateMethod,
                            reverseAppendValue!,                           
                            out memberAssignment))
                    {
                        _isValid = true;
                        _reverseMembers += memberAssignment;
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

    //   public bool CreateCollectionMapBuilders(
    //       MemberMeta source,
    //       MemberMeta target,
    //       MemberMeta sourceItem,
    //       MemberMeta targetItem,
    //       in CollectionMeta sourceCollInfo,
    //       in CollectionMeta targetCollInfo,
    //       in CollectionMapping collMapInfo,
    //       ValueBuilder buildItemValue,
    //       out ValueBuilder valueBuilder,
    //       out ObjectMapper methodBuilder)
    //   {
    //       var (itemType, type, isItemNullable, indexable, countable, backingArray, method, countProp, isSourceDictionary) = sourceCollInfo;
    //       string
    //           targetFullTypeName = target.Type.ExportFullName,
    //           sourceFullTypeName = source.Type.ExportFullName,
    //           targetItemFullTypeName = itemType.FullName,
    //           copyMethodName = collMapInfo.MethodName,
    //           updateMethodName = collMapInfo.MethodName;

    //       var addMethod = collMapInfo.Method;

    //       bool createArray = collMapInfo.CreateArray, redim = collMapInfo.Redim;

    //       bool IsRecursive(out int maxDepth)
    //       {
    //           var isRecursive = itemType.IsRecursive;

    //           maxDepth = target.MaxDepth;

    //           if (itemType.IsRecursive)
    //               maxDepth = target.MaxDepth;

    //           return isRecursive;
    //       }

    //       bool isFor = collMapInfo.Iterator == "for";

    //       void buildCopy(StringBuilder code)
    //       {
    //           string
    //               targetExportFullXmlDocTypeName = targetFullTypeName.Replace("<", "{").Replace(">", "}"),
    //               sourceExportFullXmlDocTypeName = sourceFullTypeName.Replace("<", "{").Replace(">", "}"),
    //               underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

    //           (string defaultType, string initType, Action<StringBuilder, string> returnExpr) = (type, target.Type.IsInterface) switch
    //           {
    //               (EnumerableType.ReadOnlyCollection, true) =>
    //                   ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyReadOnlyCollection",
    //                    underlyingCollectionType,
    //                    (code, v) => code.Append("new global::System.Collections.ObjectModel.ReadOnlyCollection<").Append(targetItemFullTypeName).Append(">(").Append(v).Append(")")),
    //               (EnumerableType.Collection, true) =>
    //                   ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyCollection",
    //                    underlyingCollectionType,
    //                    (code, v) => code.Append(v)),
    //               _ =>
    //                   ("new " + targetFullTypeName + "()",
    //                    targetFullTypeName + "()",
    //                    new Action<StringBuilder, string>((code, v) => code.Append(v)))
    //           };

    //           //User? <== UserDto?
    //           var checkNull = (!targetItem.IsNullable || !itemType.IsValueType) && sourceItem.IsNullable;

    //           string? suffix = (type, type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null;

    //           if (isSourceDictionary)
    //           {
    //               code.Append(@"
    //    /// <summary>
    //    /// Creates a new instance of <see cref=""")
    //                   .Append(targetExportFullXmlDocTypeName)
    //                   .Append(@"""/> based from a given <see cref=""")
    //                   .Append(sourceExportFullXmlDocTypeName)
    //                   .Append(@"""/>
    //    /// </summary>
    //    /// <param name=""source"">Data source to be mapped</param>");

    //               if (itemType.IsRecursive)
    //               {
    //                   code.Append(@"
    //    /// <param name=""depth"">Depth index for recursion control</param>
    //    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
    //               }

    //               code.Append(@"
    //    public static ")
    //                   .Append(targetFullTypeName)
    //                   .AddSpace()
    //                   .Append(copyMethodName)
    //                   .Append('(') ;

    //               if (target.Type.IsValueType) code.Append("ref ");

    //               code.Append("this ")
    //                   .Append(sourceFullTypeName)
    //                   .Append(" source");

    //               if (itemType.IsRecursive)
    //               {
    //                   code.Append(", int depth = 0, int maxDepth = ")
    //                       .Append(target.MaxDepth)
    //                       .Append(@")
    //    {
    //        if (depth >= maxDepth) 
    //            return ").Append(defaultType).Append(@";
    //");
    //               }
    //               else
    //               {
    //                   code.Append(@")
    //    {");
    //               }

    //               code.Append(@"
    //        var target = ").Append(defaultType).Append(@";

    //        foreach (var item in source)
    //        {
    //            target[");

    //               //keyValueMapping!.Key.Invoke(code, "item");

    //               code.Append("] = ");

    //               //keyValueMapping!.Value(code, "item");

    //               code.Append(@";
    //        }

    //        return target;
    //    }
    //");
    //               return;
    //           }

    //           code.Append(@"
    //    /// <summary>
    //    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    //    /// </summary>
    //    /// <param name=""source"">Data source to be mapped</param>");

    //           if (itemType.IsRecursive)
    //               code.Append(@"
    //    /// <param name=""depth"">Depth index for recursion control</param>
    //    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

    //           code.Append(@"
    //    public static ").Append(targetFullTypeName).AddSpace().Append("Update(");

    //           if (target.Type.IsValueType) code.Append("ref ");

    //           code.Append("this ").Append(sourceFullTypeName).Append(@" target");

    //           if (itemType.IsRecursive)
    //           {
    //               code.Append(", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    //    {
    //        if (depth >= maxDepth) 
    //            return ");

    //               if (createArray)
    //               {
    //                   code.Append("global::System.Array.Empty<").Append(targetItemFullTypeName).Append(">()");
    //               }
    //               else
    //               {
    //                   code.Append(defaultType);
    //               }

    //               code.Append(@";
    //");
    //           }
    //           else
    //           {
    //               code.Append(@")
    //    {");
    //           }

    //           if (createArray)
    //           {
    //               if (redim)
    //               {
    //                   code.Append(@"
    //        int len = 0, aux = 16;
    //        var target = new ").Append(targetItemFullTypeName).Append(@"[aux];
    //");
    //               }
    //               else
    //               {
    //                   code.Append(@"
    //        int len = ");

    //                   if (isFor)
    //                   {
    //                       code.Append("source.").Append(countProp);
    //                   }
    //                   else
    //                   {
    //                       code.Append(0);
    //                   }

    //                   code.Append(@";
    //        var target = new ").Append(targetItemFullTypeName).Append('[');

    //                   if (isFor)
    //                   {
    //                       code.Append("len");
    //                   }
    //                   else
    //                   {
    //                       code.Append("source.").Append(countProp);
    //                   }

    //                   code.Append(@"];
    //");
    //               }
    //           }
    //           else
    //           {
    //               code.Append(@"
    //        var target = new ").Append(initType).Append(';').Append(@"
    //");
    //           }

    //           if (isFor)
    //           {
    //               code.Append(@"
    //        for (int i = 0; i < len; i++)
    //        {
    //            target[i] = ");

    //               buildItemValue(code, target, source);

    //               code.Append(@";
    //        }

    //        return target").Append(suffix).Append(@";
    //    }
    //");
    //           }
    //           else
    //           {
    //               code.Append(@"
    //        foreach (var item in source)
    //        {");

    //               if (createArray)
    //               {
    //                   code.Append(@"
    //            target[len");

    //                   if (!redim)
    //                   {
    //                       code.Append("++");
    //                   }

    //                   code.Append("] = ");

    //                   buildItemValue(code, target, source);

    //                   code.Append(";");

    //                   if (redim)
    //                   {
    //                       //redim array
    //                       code.Append(@"

    //            if (aux == ++len)
    //                global::System.Array.Resize(ref target, aux *= 2);
    //        }

    //        return (len < aux ? target[..len] : target)").Append(suffix).Append(@";
    //    }
    //");
    //                   }
    //                   //normal ending
    //                   else
    //                   {
    //                       code.Append(@"
    //        }

    //        return target").Append(suffix).Append(@";
    //    }
    //");
    //                   }
    //               }
    //               else
    //               {
    //                   code.Append(@"
    //            target.").Append(addMethod);


    //                   buildItemValue(code, target, source);

    //                   code.Append(@");
    //        }

    //        return ");

    //                   returnExpr(code, "target");

    //                   code.Append(@";
    //    }
    //");
    //               }
    //           }
    //       }

    //       valueBuilder = (code, target, source, checkNull) =>
    //       {
    //           code.Append(copyMethodName).Append("(").Append(value);

    //           if (IsRecursive(out var maxDepth))
    //           {
    //               code.Append(", __l - 1");
    //           }

    //           code.Append(")");
    //       };

    //       methodBuilder = buildCopy;

    //       return true;
    //   }

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

    private static bool TryBuildMemberAssignment(
        MemberMeta target,
        MemberMeta source,
        bool useFillMethod,
        string updateMethodName,
        string copyMethod,
        ValueBuilder appendValue,
        out Action<StringBuilder> assigner)
    {
        // Consolidamos todos los datos en una estructura inmutable
        Assignment state = new(
            target.Type.FullName,
            "." + target.Name,
            source.Type.FullName,
            "." + source.Name,
            updateMethodName,
            copyMethod,
            target.UnsafeFieldAccesor,
            target.UseUnsafeAccessor,
            target.Type.IsValueType,
            target.IsNullable,
            source.Type.IsValueType,
            source.IsNullable,
            target.IsParentTypeRecursive,
            target.IsParentValueType,
            target.CanWrite,
            target.MaxDepth);

        // Selección de la asignación según los casos
        assigner = target.Type.IsMemberless && target.Type.IsPrimitive
            ? code => state.DefaultAssignment(code, appendValue)
            : !source.Type.IsMemberless && (target.IsNullable || source.IsNullable)
                ? code => state.NullableAssignment(code, appendValue)
                : useFillMethod
                    ? code => state.UpdateMethodAssignment(code, appendValue)
                    : code => state.DefaultAssignment(code, appendValue);

        return true;
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

internal delegate void ValueBuilder(in Assignment state, StringBuilder code, bool preventNullCheck = false);

internal record struct CollectionMapping(
    bool CreateArray,
    bool UseLenInsteadOfIndex,
    string Iterator,
    bool Redim,
    string? Method,
    string MethodName);

internal readonly struct Assignment(
    string targetTypeFullName,
    string targetMemberName,
    string sourceTypeFullName,
    string sourceMemberName,
    string copyMethodName,
    string updateMethodName,
    string unsafeFieldAccesor,
    bool useUnsafeAccessor,
    bool isTargetValueType,
    bool isTargetNullable,
    bool isSourceValueType,
    bool isSourceNullable,
    bool recursive,
    bool isParentValueType,
    bool canWrite,
    int maxDepth)
{
    private readonly string 
        sourceMemberName = sourceMemberName, 
        copyMethodName = copyMethodName, 
        targetTypeFullName = targetTypeFullName, 
        sourceTypeFullName = sourceTypeFullName;

    private readonly bool 
        isTargetValueType = isTargetValueType,
        isSourceValueType = isSourceValueType,
        isSourceNullable = isSourceNullable,
        isTargetNullable = isTargetNullable;

    internal void DirectAssignment(StringBuilder code, ValueBuilder appendValue)
    {
        code.Append(@"
        target")
            .Append(targetMemberName)
            .Append(" = ");

        appendValue(this, code);
        
        code.Append(";");
    }

    internal void NullableAssignment(StringBuilder code, ValueBuilder appendValue)
    {
        if (ShouldAppendNewLine(code))
            code.AppendLine();

        if (isSourceNullable)
        {
            code.Append(@"
        if (source")
                .Append(sourceMemberName)
                .Append(AppendNullCheck(isSourceValueType, isSourceNullable));

            if (recursive)
                code.Append(" && __l <= ").Append(maxDepth);

            code.Append(") ");
        }
        if (isTargetNullable)
        {

            code.Append(@"
");
            if(isSourceNullable) code.Append("    ");
            
            code.Append("        if(target")
                .Append(targetMemberName)
                .Append(AppendNullCheck(isTargetValueType, isTargetNullable))
                .Append(") ");

            AppendAssignmentCall(code);

            code.Append(@";
");
            if(isSourceNullable) code.Append("    ");
            
            code.Append("        else ");

            if (!canWrite && useUnsafeAccessor)
                AppendTargetValue(code);
            else
                code.Append("target").Append(targetMemberName);

            code.Append(" = ");

            appendValue(this, code, true);
        }
        else
        {
            AppendAssignmentCall(code);
        }

        code.Append(";");

        if (isSourceNullable)
        {
            code.Append(@"
        else ");

            if (useUnsafeAccessor)
                AppendTargetValue(code);
            else
                code.Append("target").Append(targetMemberName);

            code.Append(" = default;");
        }

        code.AppendLine();
    }

    internal void UpdateMethodAssignment(StringBuilder code, ValueBuilder _)
    {
        code.Append(@"
        ");

        AppendAssignmentCall(code);

        code.Append(@";");
    }

    internal void DefaultAssignment(StringBuilder code, ValueBuilder appendValue)
    {
        code.Append(@"
        ");

        if (useUnsafeAccessor)
            AppendTargetValue(code);
        else
            code.Append("target").Append(targetMemberName);
        
        code.Append(" = ");

        appendValue(this, code);
        
        code.Append(";");
    }

    internal void AppendTargetValue(StringBuilder code)
    {
        code.Append(unsafeFieldAccesor).Append("(");

        if (isParentValueType) code.Append("ref ");

        code.Append("target)");
    }

    private void AppendAssignmentCall(StringBuilder code)
    {
        code.Append(updateMethodName).Append('(');

        if (useUnsafeAccessor)
        {
            if (isTargetValueType)
            {
                code.Append("ref ");

                if (isTargetNullable)
                {
                    code.Append("UnNull(ref ");

                    AppendTargetValue(code);

                    code.Append(")");
                }
                else
                {
                    AppendTargetValue(code);
                }
            }
            else
            {
                AppendTargetValue(code);

                if (isTargetNullable)
                    code.Append('!');
            }
        }
        else
        {
            if (isTargetValueType)
                code.Append("ref ");

            code.Append("target").Append(targetMemberName);
        }

        code.Append(", source").Append(sourceMemberName);

        if (isSourceNullable)
            code.Append(isSourceValueType ? ".Value" : "!");

        if (recursive)
            code.Append(", __l");

        code.Append(")");
    }

    internal static void AsValue(in Assignment state, StringBuilder code, bool dontChecknull = false)
    {
        //code.AppendLine("/*").AppendLine(state.ToString()).Append("*/");
        code.Append("source").Append(state.sourceMemberName);

        if (dontChecknull || !state.isSourceNullable || state.isTargetNullable) return;

        code.Append(" ?? default!");
    }

    internal static void AsCast(in Assignment state, StringBuilder code, bool dontCheckNull = false)
    {
        code.Append('(')
            .Append(state.targetTypeFullName)
            .Append(")");

        //code.Append("/*").Append(state).Append("*/");

        if (!dontCheckNull && !state.isTargetNullable && state.isSourceNullable && state.isSourceValueType)
            code.Append("(source")
                .Append(state.sourceMemberName)
                .Append(" ?? default!)");
        /*(").Append(state.sourceTypeFullName).Append(")*/
        else
            code.Append("source")
                .Append(state.sourceMemberName);
    }

    internal static void AsMapper(in Assignment state, StringBuilder code, bool dontCheckNull = false)
    {
        code.Append("source").Append(state.sourceMemberName);

        if (!dontCheckNull && state.isSourceNullable) code.Append("?");

        code.Append(".").Append(state.copyMethodName).Append("()");
    }

    private static bool ShouldAppendNewLine(StringBuilder code) => code[^1] is ';' or '{';

    private static string AppendNullCheck(bool isValueType, bool isNullable) =>
        isValueType && isNullable ? ".HasValue" : " is not null";

    public override string ToString()
    {
        return $@"targetTypeFullName={targetTypeFullName},
targetMemberName={targetMemberName},
sourceTypeFullName={sourceTypeFullName},
sourceMemberName={sourceMemberName},
useUnsafeAccessor={useUnsafeAccessor},
isTargetValueType={isTargetValueType},
isTargetNullable={isTargetNullable},
isSourceNullable={isSourceNullable},
isSourceValueType={isSourceValueType},
recursive={recursive},
isParentValueType={isParentValueType},
copyMethodName={copyMethodName},
updateMethodName={updateMethodName},
unsafeFieldAccesor={unsafeFieldAccesor},
maxDepth={maxDepth}";
    }
}