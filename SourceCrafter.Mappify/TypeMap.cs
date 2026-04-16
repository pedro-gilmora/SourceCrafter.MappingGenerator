using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SourceCrafter.Mappify;

internal delegate bool CacheCreator(string item, out string cachedItem);
internal delegate void MapperMethodCreator(StringBuilder code, Action<StringBuilder> itemMapper);
[Flags]
enum ConversionType
{
    None = 0,
    Implicit = 1,
    InheritsOrImplement = 2,
    Explicit = 4,
    Mapper = 8,
    ConversionMethod = 16
}

internal sealed class TypeMap
{
    internal readonly int Id, TargetTypeId, SourceTypeId;

    private readonly ConversionType _conversionType, _reverseConversionType;

    internal readonly bool IsValid = true;

    internal readonly string MethodName, ReverseMethodName;

    private readonly Action<string, StringBuilder>? _itemMapper, _reverseItemMapper;

    internal bool Generated = false;

#if DEBUG || DEBUGSGEN
    private readonly string debugString;
#endif

    public TypeMap(
        int mapperId,
        ref TypeMap? typeMap,
        TypeMeta targetType,
        TypeMeta sourceType,
        ApplyTo ignore,
        Mappers mappers,
        TypeSet types,
        List<Action<StringBuilder>> methods)
    {
        typeMap = this;

#if DEBUG || DEBUGSGEN
        debugString = $"{sourceType.ExportFullName} <=> {targetType.ExportFullName}";
#endif
        Id = mapperId;

        bool sameType = targetType.Id == sourceType.Id;

        TargetTypeId = targetType.Id;
        SourceTypeId = sourceType.Id;
        _conversionType = _reverseConversionType = ConversionType.None;

        if (sameType)
        {
            MethodName = ReverseMethodName = "Copy";
        }
        else
        {
            MethodName = "To" + targetType.SanitizedName;
            ReverseMethodName = "To" + sourceType.SanitizedName;
        }

        var allowLowerCase = sourceType.IsTupleType || targetType.IsTupleType /*, hasMatches = false*/;

        var canUseUnsafeAccessor = mappers.CanUseUnsafeAccessor;

        if (/*_isCollection = */sourceType.IsCollection && targetType.IsCollection)
        {
            if (IsCollectionMapping(ref _itemMapper, ref _reverseItemMapper))
            {
                _conversionType |= ConversionType.Mapper;
                _reverseConversionType |= ConversionType.Mapper;
            }

            else IsValid = false;

            return;
        }

        IsValid = types.Compilation.HasConversion(targetType, sourceType, ref _conversionType, ref _reverseConversionType);

        if (targetType.IsPrimitive || sourceType.IsPrimitive || sourceType.IsMemberless || targetType.IsMemberless) return;

        if (!targetType.IsInterface) _conversionType |= ConversionType.Mapper;

        if (!sourceType.IsInterface) _reverseConversionType |= ConversionType.Mapper;

        List<(int, Action<StringBuilder>)> members = [], reverseMembers = [];

        int i = -1;
        bool lastWasNullCheck = false;

        foreach (var targetMember in targetType.Members)
        {
            foreach (var sourceMember in sourceType.Members)
            {
                var map = this;

                int sourceTypeId = sourceMember.Type.Id, targetTypeId = targetMember.Type.Id;

                if (!targetMember.Matches(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var targetMatch, out var sourceMatch)
                    || mapperId != (targetTypeId, sourceTypeId).ComputeHashCode()
                    && !(map = mappers.GetOrAdd(targetMember.Type, sourceMember.Type, ignore)).IsValid)

                    continue;

                i++;

                map.GetMeta(targetTypeId, sourceTypeId, out var memberConversionType, out var memberTypeMethod, out var reverseMemberConversionType, out var reverseMemberTypeMethod, out var itemMapper, out var reverseItemMapper);

                if (targetMatch.IsAssignable)
                {
                    IsValid = true;
                    members.Add((i, BuildMemberAssignment(targetMember, sourceMember, memberConversionType, memberTypeMethod, targetMatch.AllowNull, itemMapper)));
                }

                if (sourceMatch.IsAssignable)
                {
                    IsValid = true;
                    reverseMembers.Add((i, BuildMemberAssignment(sourceMember, targetMember, reverseMemberConversionType, reverseMemberTypeMethod, sourceMatch.AllowNull, reverseItemMapper)));
                }

                break;
            }
        }

        Action<StringBuilder> BuildMemberAssignment(
                MemberMeta target,
                MemberMeta source,
                ConversionType conversionType,
                string methodName,
                bool allowSourceNull,
                Action<string, StringBuilder>? itemMapper)
        {
            bool isTargetValueType = target.Type.IsValueType,
                useUpdate = conversionType.HasFlag(ConversionType.Mapper) && (!target.Type.IsCollection || target.Type.Collection.CanUpdate),
                useUnsafeSetterAccessor = (!target.CanWrite && target.UseUnsafeAccessor) || (useUpdate && target.Type.IsValueType),
                useUnsafeGetterAccessor = target is { CanRead: false, UseUnsafeAccessor: true },
                isSourceValueType = source.Type.IsValueType,
                isSourceNullable = source.IsNullable,
                isTargetNullable = target.IsNullable,
                isTargetRecursive = target.Type.IsRecursive,
                isParentValueType = target.IsParentValueType;

            // For .Update() calls on reference types with readable properties, use the property directly
            string targetMemberUpdateExpression = (useUpdate && target.CanRead && !target.Type.IsValueType)
                    ? "target." + target.Name
                    : useUnsafeSetterAccessor
                        ? $"target.{target.UnsafeFieldAccesor}()"
                        : "target." + target.Name,
                targetMemberReadExpression = useUnsafeGetterAccessor
                    ? $"target.{target.UnsafeFieldAccesor}()"
                    : "target." + target.Name,
                targetMemberWriteExpression = useUnsafeSetterAccessor
                    ? $"target.{target.UnsafeFieldAccesor}()"
                    : "target." + target.Name,
                sourceMemberExpression = source is { CanRead: false, UseUnsafeAccessor: true }
                    ? $"{source.UnsafeFieldAccesor}(source)"
                    : "source." + source.Name,
                targetTypeFullName = target.Type.FullName,
                sourceTypeFullName = source.Type.FullName;

            short maxDepth = target.MaxDepth;

            return code =>
            {
                if (useUpdate)
                {
                    if (isSourceNullable || isTargetNullable)
                    {
                        code.AppendLine();
                    }

                    code.Append(@"
        ");

                    var cachedSourceMemberExpr = sourceMemberExpression;
                    var cachedTargetMemberExpr = targetMemberReadExpression;

                    string? indent = (useUnsafeGetterAccessor || useUnsafeSetterAccessor) && isSourceNullable && isTargetNullable ? "    " : null;

                    if (isSourceNullable)
                    {
                        lastWasNullCheck = true;

                        code.Append("if(").Append(cachedSourceMemberExpr).Append(" is ")
                            .Append(source.Type.IsInterface && (conversionType.HasFlag(ConversionType.Explicit) || conversionType.HasFlag(ConversionType.InheritsOrImplement))
                                ? targetTypeFullName
                                : sourceTypeFullName)
                            .Append(" _source")
                            .Append(source.Name).Append(@")
        {
            ");

                        cachedSourceMemberExpr = "_source" + source.Name;
                    }

                    if (isTargetNullable)
                    {
                        lastWasNullCheck = true;

                        if (useUnsafeSetterAccessor)
                        {
                            code.Append(@"ref var ").Append(cachedTargetMemberExpr = "_target" + target.Name).Append(" = ref ").Append(targetMemberWriteExpression).Append(@";
        ");
                        }

                        code.Append(indent).Append("if (").Append(cachedTargetMemberExpr).Append(" is not null").Append(@") ").Append(cachedTargetMemberExpr);
                    }
                    else
                    {
                        if (lastWasNullCheck && !isSourceNullable)
                        {
                            code.Append(@"
        ");
                            lastWasNullCheck = false;
                        }
                        code.Append(targetMemberUpdateExpression);
                    }

                    code.Append(isTargetNullable ? isTargetValueType ? ".UnNull()." : useUnsafeSetterAccessor ? "." : "?." : ".")
                        .Append("Update(").Append(cachedSourceMemberExpr).Append(isTargetRecursive ? ", maxDepth, depth + 1" : "").Append(");");

                    if (isSourceNullable && isTargetNullable)
                    {
                        code.Append(@"
        ").Append(indent).Append("else ").Append(cachedTargetMemberExpr).Append(" = ");

                        switch (conversionType)
                        {
                            case ConversionType.Mapper and not ConversionType.Implicit:
                                code.Append(UseMapper(methodName, cachedSourceMemberExpr, false, isTargetRecursive, true));
                                break;
                            case ConversionType.Explicit:
                                code.Append(Cast(targetTypeFullName, cachedSourceMemberExpr, false));
                                break;
                            default:
                                if (conversionType.HasFlag(ConversionType.Mapper) && isTargetRecursive)
                                    code.Append(UseMapper(methodName, cachedSourceMemberExpr, false, isTargetRecursive, true));
                                else
                                    code.Append(cachedSourceMemberExpr);
                                break;
                        }

                        code.Append(';');
                    }

                    if (isSourceNullable) code.Append(@"
        }");
                    if (isTargetNullable || allowSourceNull)
                    {
                        code.Append(@"
        else ");

                        //.Append(useUnsafeSetterAccessor ? targetMemberWriteExpression : cachedTargetMemberExpr)
                        if (useUnsafeSetterAccessor)
                        {
                            if (!isSourceNullable && isTargetNullable)
                            {
                                code.Append(cachedTargetMemberExpr);
                            }
                            else
                            {
                                code.Append(targetMemberWriteExpression);
                            }
                        }
                        else
                        {
                            code.Append(cachedTargetMemberExpr);
                        }

                        code.Append(@" = ");

                        if (useUnsafeSetterAccessor && !isSourceNullable && isTargetNullable)
                        {
                            if ((source.Type.IsCollection || target.Type.IsCollection))
                            {
                                itemMapper!(cachedSourceMemberExpr, code);
                                code.Append(";");
                            }
                            else
                            {
                                switch (conversionType)
                                {
                                    case ConversionType.Mapper and not ConversionType.Implicit:
                                        code.Append(UseMapper(methodName, cachedSourceMemberExpr, false, isTargetRecursive, true)).Append(";");
                                        break;
                                    case ConversionType.Explicit:
                                        code.Append(Cast(targetTypeFullName, cachedSourceMemberExpr, false)).Append(";");
                                        break;
                                    default:
                                        code.Append(cachedSourceMemberExpr).Append(";");
                                        break;
                                }
                            }
                        }
                        else

                            code.Append("default;");
                    }
                }
                else
                {
                    if (lastWasNullCheck)
                    {
                        code.AppendLine();
                        lastWasNullCheck = false;
                    }
                    code.Append(@"
        ");

                    code.Append(targetMemberWriteExpression).Append(" = ");

                    if (target.Type.IsCollection)
                    {
                        itemMapper!(sourceMemberExpression, code);
                    }
                    else
                    {
                        switch (conversionType)
                        {
                            case ConversionType.Explicit and not ConversionType.Implicit:
                                code.Append(Cast(targetTypeFullName, sourceMemberExpression, isSourceNullable));
                                break;
                            case ConversionType.Mapper:
                                code.Append(UseMapper(methodName, sourceMemberExpression, isSourceNullable, isTargetRecursive, true));
                                break;
                            default:
                                code.Append(sourceMemberExpression);
                                if (!isTargetNullable && isSourceNullable && isSourceValueType) code.Append(" ?? default!");
                                break;
                        }
                    }

                    code.Append(';');
                }

            };
        }

        if (IsValid && members.Count + reverseMembers.Count > 0) methods.Add(BuildFile);

        bool IsCollectionMapping(ref Action<string, StringBuilder>? itemMapper, ref Action<string, StringBuilder>? reverseItemMapper)
        {
            switch (targetType, sourceType)
            {
                case ({ Collection.Kind: CollectionKind.Dictionary }, { Collection.Kind: CollectionKind.Dictionary }):

                    var targetKeyMember = targetType.Collection.ItemType.Members[0]!;
                    var sourceKeyMember = sourceType.Collection.ItemType.Members[0]!;

                    if (mappers.GetOrAdd(targetKeyMember.Type, sourceKeyMember.Type) is not { IsValid: true } keyMapper) return false;

                    var targetValueMember = targetType.Collection.ItemType.Members[1]!;
                    var sourceValueMember = sourceType.Collection.ItemType.Members[1]!;

                    if (mappers.GetOrAdd(targetValueMember.Type, sourceValueMember.Type) is not { IsValid: true } valueMapper) return false;

                    itemMapper = BuildDictionaryItemMapper(targetType, sourceType, targetKeyMember, sourceKeyMember, keyMapper, targetValueMember, sourceValueMember, valueMapper);
                    reverseItemMapper = BuildDictionaryItemMapper(sourceType, targetType, sourceKeyMember, targetKeyMember, keyMapper, sourceValueMember, targetValueMember, valueMapper);

                    BuildDictionaryMapperFile(targetKeyMember, targetValueMember, sourceKeyMember, sourceValueMember, keyMapper, valueMapper);

                    return true;
                case ({ Collection.Kind: CollectionKind.Dictionary }, _):

                    return TryMapKeyValuePairLike(targetType, sourceType, ref itemMapper, ref reverseItemMapper);

                case (_, { Collection.Kind: CollectionKind.Dictionary }):

                    return TryMapKeyValuePairLike(sourceType, targetType, ref itemMapper, ref reverseItemMapper);

                default:

                    if (mappers.GetOrAdd(targetType.Collection.ItemType, sourceType.Collection.ItemType) is not { IsValid: true } itemTypeMap) return false;

                    itemTypeMap.GetMeta(targetType.Collection.ItemType.Id, sourceType.Collection.ItemType.Id, out var itemConversionType, out string itemTypeMethod, out var itemReverseConversionType, out string itemReverseTypeMethod);

                    methods.Add(BuildCollectionMapping(
                        targetType.Collection,
                        sourceType.Collection,
                        itemConversionType,
                        itemTypeMethod,
                        MethodName,
                        targetType.ExportFullName,
                        sourceType.ExportFullName));

                    if (targetType.Collection.CanUpdate)
                        methods.Add(BuildCollectionUpdateMapping(
                            targetType.Collection,
                            sourceType.Collection,
                            itemConversionType,
                            itemTypeMethod,
                            targetType.ExportFullName,
                            sourceType.ExportFullName));

                    itemMapper = BuildCollectionItemMapper(sourceType, targetType, itemTypeMap);

                    if (sourceType.Id == targetType.Id && targetType.Collection.ItemType.Id == sourceType.Collection.ItemType.Id)
                    {
                        reverseItemMapper = itemMapper;
                        return true;
                    }

                    methods.Add(BuildCollectionMapping(
                        sourceType.Collection,
                        targetType.Collection,
                        itemReverseConversionType,
                        itemReverseTypeMethod,
                        ReverseMethodName,
                        sourceType.ExportFullName,
                        targetType.ExportFullName));

                    if (sourceType.Collection.CanUpdate)
                        methods.Add(BuildCollectionUpdateMapping(
                            sourceType.Collection,
                            targetType.Collection,
                            itemReverseConversionType,
                            itemReverseTypeMethod,
                            sourceType.ExportFullName,
                            targetType.ExportFullName));

                    reverseItemMapper = BuildCollectionItemMapper(sourceType, targetType, itemTypeMap);

                    return true;
            }

            bool TryMapKeyValuePairLike(TypeMeta targetType, TypeMeta sourceType, ref Action<string, StringBuilder>? itemMapper, ref Action<string, StringBuilder>? reverseItemMapper)
            {
                var targetKeyMember = targetType.Collection.ItemType.Members[0]!;

                var targetTypeId = targetKeyMember.Type.Id;
                int sourceKeyTypeId = 0;
                TypeMap? keyMapper = null;
                MemberMeta? sourceKeyMember = null;

                foreach (var sourceMember in sourceType.Collection.ItemType.Members)
                {
                    sourceKeyTypeId = sourceMember.Type.Id;
                    keyMapper = mappers.GetOrAdd(targetKeyMember.Type, sourceMember.Type, ignore);

                    if (!targetKeyMember.Matches(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var targetKeyMatch, out var sourceKeyMatch)
                        && !keyMapper.IsValid)

                        continue;

                    sourceKeyMember = sourceMember;

                    break;
                }

                if (sourceKeyMember is null || keyMapper is null) return false;

                var targetValueMember = targetType.Collection.ItemType.Members[1]!;

                TypeMap? _valueMapper = null;
                var sourceValueTypeId = 0;
                MemberMeta? sourceValueMember = null;
                foreach (var sourceMember in sourceType.Collection.ItemType.Members)
                {
                    sourceValueTypeId = sourceMember.Type.Id;
                    _valueMapper = mappers.GetOrAdd(targetValueMember.Type, sourceMember.Type, ignore);

                    if (sourceMember.Id == sourceKeyMember.Id
                        || !targetValueMember.Matches(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var targeValueMatch, out var sourcValueMatch)
                        && !_valueMapper.IsValid)

                        continue;

                    sourceValueMember = sourceMember;
                    break;
                }

                var itemMap = mappers.GetOrAdd(targetType.Collection.ItemType, sourceType.Collection.ItemType);

                if (sourceValueMember is null || _valueMapper is null || !itemMap.IsValid) return false;

                itemMapper = targetType.Collection.IsDictionary
                    ? BuildDictionaryItemMapper(targetType, sourceType, targetKeyMember, sourceKeyMember, keyMapper, targetValueMember, sourceValueMember, _valueMapper)
                    : BuildCollectionItemMapper(targetType, sourceType, itemMap);

                reverseItemMapper = sourceType.Collection.IsDictionary
                    ? BuildDictionaryItemMapper(sourceType, targetType, sourceKeyMember, targetKeyMember, keyMapper, sourceValueMember, targetValueMember, _valueMapper)
                    : BuildCollectionItemMapper(sourceType, targetType, itemMap);

                BuildDictionaryMapperFile(
                    targetKeyMember,
                    targetValueMember,
                    sourceKeyMember,
                    sourceValueMember,
                    keyMapper,
                    _valueMapper);

                return true;

            }

            static Action<string, StringBuilder> BuildCollectionItemMapper(
                TypeMeta targetType,
                TypeMeta sourceType,
                TypeMap itemMapper)
           {
                bool discardNullItems = sourceType.Collection.IsItemNullable && !targetType.Collection.IsItemNullable;
                itemMapper!.GetMeta(targetType.Collection.ItemType.Id, sourceType.Collection.ItemType.Id, out var itemConversionType, out var itemMethodName, out var reverseItemConversionType, out var reverseItemMethodName);

                return (source, code) =>
                {
                    switch (targetType.Collection.Kind)
                    {
                        case CollectionKind.Queue:
                        case CollectionKind.Stack:
                        case CollectionKind.Collection
                        :
                            if (targetType.Name != "List")
                                code.Append("new ").Append(targetType.FullName)
                                     .Append("(");

                            code.Append(source);

                            if (discardNullItems) code.Append(".Where(item => item is not null).");

                            switch (itemConversionType)
                            {
                                case ConversionType.Mapper and not ConversionType.Implicit:
                                    code.Append(".Select(item => ").Append(UseMapper(reverseItemMethodName, "item", false, targetType.Collection.ItemType.IsRecursive)).Append(')');
                                    break;
                                case ConversionType.Explicit:
                                    code.Append(".Select(item => ").Append(Cast(targetType.Collection.ItemType.FullName, "item", false)).Append(')');
                                    break;
                            }

                            code.Append(".ToList()");

                            if (targetType.Name != "List")
                                code.Append(')');

                            break;
                        case CollectionKind.Enumerable:
                        case CollectionKind.ReadOnlySpan:
                        case CollectionKind.Array:
                        case CollectionKind.Span
                        :
                            code.Append(source);

                            if (sourceType.Collection.IsItemNullable && !targetType.Collection.IsItemNullable) code.Append(".Where(item => item is not null).");

                            switch (itemConversionType)
                            {
                                case ConversionType.Mapper and not ConversionType.Implicit:
                                    code.Append("Select(item => ").Append(UseMapper(itemMethodName, "item", false, targetType.Collection.ItemType.IsRecursive, true)).Append(')'); ;
                                    break;
                                case ConversionType.Explicit:
                                    code.Append("Select(item => ").Append(Cast(targetType.Collection.ItemType.FullName, "item", false)).Append(')');
                                    break;
                            }

                            code.Append(".ToArray()");

                            break;
                    }

                };
            }
            static Action<string, StringBuilder> BuildDictionaryItemMapper(
                TypeMeta targetType,
                TypeMeta sourceType,
                MemberMeta targetKeyMember,
                MemberMeta sourceKeyMember,
                TypeMap _keyMapper,
                MemberMeta targetValueMember,
                MemberMeta sourceValueMember,
                TypeMap _valueMapper)
            {
                string sourceKeyMemberName = sourceKeyMember.Name;
                string sourceValueMemberName = sourceValueMember.Name;
                bool isSourceNullable = targetType.Collection.IsItemNullable;
                bool isSourceKeyNullable = sourceKeyMember.IsNullable;
                bool isSourceValueNullable = sourceValueMember.IsNullable;
                bool useSourceKeyFieldUnsafeAccesor = sourceKeyMember is { CanRead: false, UseUnsafeAccessor: true };
                bool useSourceValueFieldUnsafeAccesor = sourceValueMember is { CanRead: false, UseUnsafeAccessor: true };
                bool discardNullItems = sourceType.Collection.IsItemNullable && !targetType.Collection.IsItemNullable;
                
                _keyMapper!.GetMeta(targetKeyMember.Type.Id, sourceKeyMember.Type.Id, out var keyConversionType, out string keyMethod);
                _valueMapper!.GetMeta(targetValueMember.Type.Id, sourceValueMember.Type.Id, out var valueConversionType, out string valueMethod);
                    string keySourceAccess = useSourceKeyFieldUnsafeAccesor
                        ? $"item.{sourceKeyMember.UnsafeFieldAccesor}()"
                        : $"item{(isSourceNullable ? "?." : ".")}{sourceKeyMemberName}";

                string keyTypeFullName = targetKeyMember.Type.FullName;
                string sourceTypeFullName = sourceType.Collection.ItemType.FullName;
                string valueTypeFullName = targetValueMember.Type.FullName;

                switch (keyConversionType)
                {
                    case ConversionType.Explicit:
                        keySourceAccess = Cast(keyTypeFullName, keySourceAccess, isSourceKeyNullable);
                        break;
                    case ConversionType.Mapper:
                        keySourceAccess = UseMapper(keyMethod, keySourceAccess, isSourceKeyNullable);
                        break;
                }

                string sourceValue = useSourceValueFieldUnsafeAccesor
                    ? $"item.{sourceValueMember.UnsafeFieldAccesor}()"
                    : $"item{(isSourceNullable ? "?." : ".")}{sourceValueMemberName}";

                switch (valueConversionType)
                {
                    case ConversionType.Explicit:
                        sourceValue = Cast(targetValueMember.Type.FullName, sourceValue, isSourceValueNullable);
                        break;
                    case ConversionType.Mapper:
                        sourceValue = UseMapper(valueMethod, sourceValue, isSourceValueNullable);
                        break;
                }

                return (source, code) =>
                {
                    code.Append(source);

                    if (discardNullItems)
                    {
                        code.Append(".Where(item => item").Append(isSourceKeyNullable ? "?." : ".").Append(sourceKeyMember.Name).Append(" is not null)");
                    }

                    code.Append(".ToDictionary<").Append(sourceTypeFullName).Append(", ").Append(keyTypeFullName).Append(", ").Append(valueTypeFullName).Append(">(item => ");

                    if (!keyConversionType.HasFlag(ConversionType.Implicit | ConversionType.InheritsOrImplement) && sourceKeyMember.Type.IsPrimitive)
                    {
                        code.Append("item").Append(isSourceNullable ? "!." : ".").Append(sourceKeyMember.Name);
                    }
                    else
                    {
                        code.Append(keySourceAccess);
                    }

                    code.Append(", item => ");

                    if (!valueConversionType.HasFlag(ConversionType.Implicit | ConversionType.InheritsOrImplement) && !sourceValueMember.Type.IsPrimitive)
                    {
                        code.Append("item").Append(isSourceNullable ? "!." : ".").Append(sourceValueMember.Name).Append(")");
                    }
                    else
                    {
                        code.Append(sourceValue).Append(")");
                    }
                };
            }
        }

        void BuildDictionaryMapperFile(
            MemberMeta targetKeyMember,
            MemberMeta targetValueMember,
            MemberMeta sourceKeyMember,
            MemberMeta sourceValueMember,
            TypeMap keyMapper,
            TypeMap valueMapper)
        {
            keyMapper.GetMeta(targetKeyMember.Type.Id, sourceKeyMember.Type.Id, out var keyConversionType, out string keyMethod, out var reverseKeyConversionType, out string reverseKeyMethod);
            valueMapper.GetMeta(targetValueMember.Type.Id, sourceValueMember.Type.Id, out var valueConversionType, out string valueMethod, out var reverseValueConversionType, out string reverseValueMethod);

            string targetExportFullName = targetType.ExportFullName;
            string sourceExportFullName = sourceType.ExportFullName;

            methods.Add(code =>
            {
                code.Append(@"
    public static ").Append(targetExportFullName).Append(" ").Append(MethodName).Append("(this ").Append(sourceExportFullName).Append(@" source)
    {
        ").Append(targetExportFullName).Append(@" target = new(source.").Append(targetType.Collection.CountProp).Append(@");

        foreach (").Append(sourceType.Collection.ItemType.ExportFullName).Append(@" sourceItem in source)
        {");

                GetDictionaryItemMapBuilder(
                    code,
                    sourceType.Collection.IsItemNullable,
                    sourceKeyMember,
                    keyConversionType,
                    keyMethod,
                    sourceValueMember,
                    valueConversionType,
                    valueMethod,
                    targetValueMember.IsNullable,
                    targetValueMember.Type.FullName);

                code.Append(@"
        }

        return target;
    }
");
            });

            methods.Add(code =>
            {
                code.Append(@"
    public static ").Append(targetExportFullName).Append(@" Update(this ").Append(targetExportFullName).Append(" target, ").Append(sourceExportFullName).Append(@" source)
    {
        target.Clear();
        target.EnsureCapacity(source.").Append(targetType.Collection.CountProp).Append(@");

        foreach (").Append(sourceType.Collection.ItemType.ExportFullName).Append(@" sourceItem in source)
        {");

                GetDictionaryItemMapBuilder(
                    code,
                    sourceType.Collection.IsItemNullable,
                    sourceKeyMember,
                    keyConversionType,
                    keyMethod,
                    sourceValueMember,
                    valueConversionType,
                    valueMethod,
                    targetValueMember.IsNullable,
                    targetValueMember.Type.FullName);

                code.Append(@"
        }

        return target;
    }
");
            });

            if (sameType)
            {
                return;
            }
            else if (sourceType.Collection.Kind is not CollectionKind.Dictionary)
            {
                var itemTypeMap = mappers.GetOrAdd(targetType.Collection.ItemType, sourceType.Collection.ItemType);

                itemTypeMap.GetMeta(sourceType.Collection.ItemType.Id, targetType.Collection.ItemType.Id, out var reverseConversionType, out string reverseTypeMethod, out var conversionType, out string typeMethod);

                methods.Add(BuildCollectionMapping(
                    sourceType.Collection,
                    targetType.Collection,
                    reverseConversionType,
                    reverseTypeMethod,
                    ReverseMethodName,
                    sourceType.FullName,
                    targetType.FullName));

                if (sourceType.Collection.CanUpdate)
                    methods.Add(BuildCollectionUpdateMapping(
                        sourceType.Collection,
                        targetType.Collection,
                        reverseConversionType,
                        reverseTypeMethod,
                        sourceType.FullName,
                        targetType.FullName));

                return;
            }
            methods.Add(code =>
            {
                code.Append(@"
    public static ").Append(sourceExportFullName).Append(" ").Append(ReverseMethodName).Append("(this ").Append(targetExportFullName).Append(@" source)
    {
        ").Append(sourceExportFullName).Append(@" target = new(source.").Append(targetType.Collection.CountProp).Append(@");

        foreach (").Append(targetType.Collection.ItemType.ExportFullName).Append(@" sourceItem in source)
        {");

                GetDictionaryItemMapBuilder(
                    code,
                    targetType.Collection.IsItemNullable,
                    targetKeyMember,
                    reverseKeyConversionType,
                    reverseKeyMethod,
                    targetValueMember,
                    reverseValueConversionType,
                    reverseValueMethod,
                    sourceValueMember.IsNullable,
                    sourceValueMember.Type.FullName);

                code.Append(@"
        }

        return target;
    }
");
            });

            methods.Add(code =>
            {
                code.Append(@"
    public static ").Append(sourceExportFullName).Append(@" Update(this ").Append(sourceExportFullName).Append(" target, ").Append(targetExportFullName).Append(@" source)
    {
        target.Clear();
        target.EnsureCapacity(source.").Append(targetType.Collection.CountProp).Append(@");

        foreach (").Append(targetType.Collection.ItemType.ExportFullName).Append(@" sourceItem in source)
        {");

                GetDictionaryItemMapBuilder(
                    code,
                    targetType.Collection.IsItemNullable,
                    targetKeyMember,
                    reverseKeyConversionType,
                    reverseKeyMethod,
                    targetValueMember,
                    reverseValueConversionType,
                    reverseValueMethod,
                    sourceValueMember.IsNullable,
                    sourceValueMember.Type.FullName);

                code.Append(@"
        }

        return target;
    }
");
            });

        }

        Action<StringBuilder> BuildCollectionMapping(
            CollectionMeta targetCollectionMeta,
            CollectionMeta sourceCollectionMeta,
            ConversionType itemConversionType,
            string itemMethod,
            string method,
            string targetFullTypeName,
            string sourceFullTypeName,
            short maxDepth = 0)
        {
            bool isTargetItemNullable = targetType.Collection.IsItemNullable,
                isSourceItemNullable = sourceType.Collection.IsItemNullable;

            TypeMeta targetItem = targetCollectionMeta.ItemType,
                     sourceItem = sourceCollectionMeta.ItemType;

            var createArray = targetCollectionMeta.ArrayBacked;
            var useLenInsteadOfIndex = targetCollectionMeta.ArrayBacked && !sourceCollectionMeta.Indexable;
            var useFor = sourceCollectionMeta.Countable && (sourceCollectionMeta.Indexable && targetCollectionMeta.ArrayBacked);
            var iterator = useFor ? "for" : "foreach";
            var redim = !sourceCollectionMeta.Countable && targetCollectionMeta.ArrayBacked;

            bool isRecursive = targetItem.IsRecursive, isSourceValueType = sourceItem.IsValueType;

            string
                countProp = targetCollectionMeta.CountProp,
                targetItemFullTypeName = targetItem.ExportFullName,
                targetExportFullXmlDocTypeName = targetFullTypeName.Replace('<', '{').Replace('>', '}'),
                sourceExportFullXmlDocTypeName = sourceType.ExportFullName.Replace('<', '{').Replace('>', '}'),
                underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

            var suffix = (sourceCollectionMeta.Kind, targetCollectionMeta.Kind) is (not CollectionKind.Array, CollectionKind.ReadOnlySpan) ? ".AsSpan()" : null;

            return code =>
            {
                code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based on a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Data source to be mapped</param>");

                if (isRecursive)
                    code.Append(@"
    /// <param name=""depth"">Depth index for recursion control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

                code.Append(@"
    public static ").Append(targetFullTypeName).AddSpace().Append(method).Append("(this ").Append(sourceFullTypeName).Append(@" source");

                if (isRecursive)
                {
                    code.Append(", int maxDepth = ").Append(maxDepth).Append(", int depth = 0").Append(@")
    {
        if (depth >= maxDepth) 
            return ");

                    if (targetCollectionMeta.Kind == CollectionKind.ReadOnlyCollection)
                    {
                        code.Append("System.Collections.ObjectModel.ReadOnlyCollection<").Append(targetItemFullTypeName).Append(">.Empty");
                    }
                    else
                    {
                        code.Append("[]");
                    }

                    code.Append(@";
");
                }
                else
                {
                    code.Append(@")
    {");
                }

                if (createArray)
                {
                    if (redim)
                    {
                        code.Append(@"
        int len = 0, aux = 16;
        var target = new ").Append(targetItemFullTypeName).Append(@"[aux];
");
                    }
                    else
                    {
                        code.Append(@"
        int len = ");

                        if (useFor)
                        {
                            code.Append("source.").Append(countProp);
                        }
                        else
                        {
                            code.Append("0");
                        }

                        code.Append(@";
        var target = new ").Append(targetItemFullTypeName).Append('[');

                        if (useFor)
                        {
                            code.Append("len");
                        }
                        else
                        {
                            code.Append("source.").Append(countProp);
                        }

                        code.Append(@"];
");
                    }
                }
                else
                {
                    if (useFor && targetCollectionMeta.Countable)
                    {
                        code.Append(@"
        int len = ");
                        code.Append("source.").Append(countProp).Append(';');
                    }

                    code.Append(@"
        var target = new ")
                        .Append(targetCollectionMeta.Kind switch
                        {
                            CollectionKind.ReadOnlyCollection => $"global::System.Collections.ObjectModel.ReadOnlyCollection<{targetItemFullTypeName}>",
                            _ => targetFullTypeName
                        })
                        .Append(@"(");

                    if (targetCollectionMeta.Kind == CollectionKind.ReadOnlyCollection)
                        code.Append("[]");

                    else if (targetType.Collection.Countable)
                        code.Append("source.").Append(targetType.Collection.CountProp);

                    code.Append(@");
");
                }

                if (useFor)
                {
                    code.Append(@"
        for (int i = 0; i < len; i++)
        {");

                    var sourceMemberExpression = "source[i]";

                    if (isSourceItemNullable)
                    {
                        code.Append(@"
            if (source[i] is not {} sourceItem) continue;
");

                        sourceMemberExpression = "sourceItem";
                    }

                    code.Append(@"
            target[i] = ");

                    switch (itemConversionType)
                    {
                        case ConversionType.Explicit:
                            code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceItemNullable));
                            break;
                        case ConversionType.Mapper:
                            code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceItemNullable, isRecursive));
                            break;
                        default:
                            code.Append(sourceMemberExpression);

                            if (!isTargetItemNullable && isSourceItemNullable && isSourceValueType) code.Append(" ?? default!");
                            break;
                    }


                    code.Append(@";
        }

        return target").Append(suffix).Append(@";
    }
");
                }
                else
                {
                    var sourceMemberExpression = "sourceItem";

                    code.Append(@"
        foreach (var sourceItem in source)
        {");

                    if (isSourceItemNullable)
                    {
                        code.Append(@"
            if (sourceItem is null) continue;
");
                    }

                    if (createArray)
                    {
                        code.Append(@"
            target[len");

                        if (!redim)
                        {
                            code.Append("++");
                        }

                        code.Append("] = ");

                        switch (itemConversionType)
                        {
                            case ConversionType.Explicit:
                                code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceItemNullable));
                                break;
                            case ConversionType.Mapper:
                                code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceItemNullable, isRecursive));
                                break;
                            default:
                                code.Append(sourceMemberExpression);

                                if (!isTargetItemNullable && isSourceItemNullable && isSourceValueType) code.Append(" ?? default!");
                                break;
                        }

                        if (redim)
                        {
                            //redim array
                            code.Append(@";

            if (aux == ++len)
                global::System.Array.Resize(ref target, aux *= 2);
        }
        
        return ").Append(suffix is null ? "len < aux ? target[..len] : target" : "(len < aux ? target[..len] : target)" + suffix).Append(@";
    }
");
                        }
                        //normal ending
                        else
                        {
                            code.Append(@";
        }

        return target").Append(suffix).Append(@";
    }
");
                        }
                    }
                    else
                    {
                        if (isSourceItemNullable)
                        {
                            code.Append(@"
            if (sourceItem is null) continue;
");
                        }

                        code.Append(@"
            target.").Append(targetCollectionMeta.Method).Append('(');

                        switch (itemConversionType)
                        {
                            case ConversionType.Explicit:
                                code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceItemNullable));
                                break;
                            case ConversionType.Mapper:
                                code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceItemNullable, isRecursive));
                                break;
                            default:
                                code.Append(sourceMemberExpression);

                                if (!isTargetItemNullable && isSourceItemNullable && isSourceValueType) code.Append(" ?? default!");
                                break;
                        }

                        code.Append(@");
        }

        return ").Append(targetCollectionMeta.Kind switch
                        {
                            CollectionKind.ReadOnlyCollection => $"new {targetFullTypeName}(target)",
                            _ => "target"
                        }).Append(@";
    }
");
                    }
                }
            };
        }

        Action<StringBuilder> BuildCollectionUpdateMapping(
            CollectionMeta targetMeta,
            CollectionMeta sourceMeta,
            ConversionType conversionType,
            string itemMethod,
            string targetFullTypeName,
            string sourceFullTypeName)
        {
            bool isSourceNullable = sourceMeta.IsItemNullable,
                isTargetNullable = targetMeta.IsItemNullable,
                isSourceValueType = sourceMeta.ItemType.IsValueType,
                isRecursive = targetMeta.ItemType.IsRecursive;

            string
                targetItemFullTypeName = targetMeta.ItemType.ExportFullName,
                targetXmlDocTypeName = targetFullTypeName.Replace('<', '{').Replace('>', '}'),
                sourceXmlDocTypeName = sourceFullTypeName.Replace('<', '{').Replace('>', '}');

            return code =>
            {
                code.Append(@"
    /// <summary>
    /// Clears and refills <see cref=""").Append(targetXmlDocTypeName).Append(@"""/> from a given <see cref=""").Append(sourceXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""target"">Target collection to update in place</param>
    /// <param name=""source"">Data source to be mapped</param>");

                if (isRecursive)
                    code.Append(@"
    /// <param name=""depth"">Depth index for recursion control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

                code.Append(@"
    public static ").Append(targetFullTypeName).Append(" Update(this ").Append(targetFullTypeName)
                    .Append(" target, ").Append(sourceFullTypeName).Append(@" source");

                if (isRecursive) code.Append(", int maxDepth = 0, int depth = 0");

                code.Append(@")
    {");

                if (isRecursive)
                    code.Append(@"
        if (depth >= maxDepth) return target;").AppendLine();

                code.Append(@"
        target.Clear();");

                if (targetType.Collection.Countable)
                    code.Append(@"
        target.EnsureCapacity(source.").Append(targetType.Collection.CountProp).Append(");");

                code.Append(@"

        foreach (var sourceItem in source)
        {");

                if (isSourceNullable)
                    code.Append(@"
            if (sourceItem is null) continue;
");

                code.Append(@"
            target.").Append(targetMeta.Method).Append('(');

                switch (conversionType)
                {
                    case ConversionType.Explicit:
                        code.Append(Cast(targetItemFullTypeName, "sourceItem", isSourceNullable));
                        break;
                    case ConversionType.Mapper:
                        code.Append(UseMapper(itemMethod, "sourceItem", isSourceNullable, isRecursive));
                        break;
                    default:
                        code.Append("sourceItem");
                        if (!isTargetNullable && isSourceNullable && isSourceValueType)
                            code.Append(" ?? default!");
                        break;
                }

                code.Append(@");
        }

        return target;
    }
");
            };
        }

        void GetDictionaryItemMapBuilder(
            StringBuilder code,
            bool isSourceNullable,
            MemberMeta sourceKeyMember,
            ConversionType keyConversionType,
            string keyMethod,
            MemberMeta sourceValueMember,
            ConversionType valueConversionType,
            string valueMethod,
            bool isTargetValueNullable,
            string targetValueTypeName)
        {
            string sourceKeyMemberName = sourceKeyMember.Name;
            string sourceValueMemberName = sourceValueMember.Name;
            bool isSourceKeyNullable = sourceKeyMember.IsNullable;
            bool isSourceValueNullable = sourceValueMember.IsNullable;
            bool useSourceKeyFieldUnsafeAccesor = sourceKeyMember is { CanRead: false, UseUnsafeAccessor: true };
            bool useSourceValueFieldUnsafeAccesor = sourceValueMember is { CanRead: false, UseUnsafeAccessor: true };

            // Build key access expression
            string keySourceAccess = useSourceKeyFieldUnsafeAccesor
                ? $"sourceItem.{sourceKeyMember.UnsafeFieldAccesor}()"
                : $"sourceItem{(isSourceNullable ? "?." : ".")}{sourceKeyMemberName}";

            switch (keyConversionType)
            {
                case ConversionType.Explicit:
                    keySourceAccess = Cast(targetValueTypeName, keySourceAccess, isSourceKeyNullable);
                    break;
                case ConversionType.Mapper:
                    keySourceAccess = UseMapper(keyMethod, keySourceAccess, isSourceKeyNullable);
                    break;
            }

            var checkedNull = false;

            if (isSourceNullable || isSourceKeyNullable)
            {
                checkedNull = true;

                code.Append(@"
            if(").Append(keySourceAccess).Append(" is not {} ").Append(keySourceAccess = "sourceItemKey)");
            }

            string sourceValue = useSourceValueFieldUnsafeAccesor
                ? $"sourceItem.{sourceValueMember.UnsafeFieldAccesor}()"
                : $"sourceItem{(!checkedNull && isSourceNullable ? "?." : ".")}{sourceValueMemberName}";


            switch (valueConversionType)
            {
                case ConversionType.Explicit:
                    sourceValue = Cast(targetValueTypeName, sourceValue, isSourceValueNullable);
                    break;
                case ConversionType.Mapper:
                    sourceValue = UseMapper(valueMethod, sourceValue, isSourceValueNullable);
                    break;
            }

            if (isSourceValueNullable)
            {
                code.Append(@"
            if(").Append(sourceValue).Append(" is not {} ").Append(sourceValue = "sourceItemValue")
                    .Append(@") { ");

                if (isTargetValueNullable) code.Append("target[").Append(keySourceAccess).Append("] = null; ");

                code.Append("continue; }");
            }

            code.Append(@"
            target[").Append(keySourceAccess).Append("] = ").Append(sourceValue).Append(";");

        }

        static void GetMethodBuilder(StringBuilder code, TypeMeta targetType, TypeMeta sourceType, string methodName, List<(int key, Action<StringBuilder> build)> members, bool needsDepth = false, short maxDepth = 1)
        {
            bool isInterface = targetType.IsInterface,
                isTargetValueType = targetType.IsValueType,
                isSourceValueType = sourceType.IsValueType;
            string
                targetFullTypeName = targetType.FullName,
                sourceFullTypeName = sourceType.FullName;

            if (!isInterface)
            {
                code.Append(@"
    public static ").Append(targetFullTypeName)
                    .Append(" ")
                    .Append(methodName).Append('(');

                if (isSourceValueType) code.Append("in ");

                code.Append("this ")
                    .Append(sourceFullTypeName)
                    .Append(@" source");

                if (needsDepth) code.Append(", int maxDepth = ").Append(maxDepth).Append(", int depth = 0");

                code.Append(@")
    {
        ");

                if (isTargetValueType)
                    code.Append(targetFullTypeName)
                        .Append(@" init = default;

        return Update(ref init, source").Append(needsDepth ? ", maxDepth, depth)" : ")");

                else
                    code.Append("return Update(new ")
                        .Append(targetFullTypeName)
                        .Append("(), source").Append(needsDepth ? ", maxDepth, depth)" : ")");

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

            if (needsDepth) code.Append(", int maxDepth = ").Append(maxDepth).Append(", int depth = 0");

            code.Append(@")
    {");
            if (needsDepth)
                code.Append(@"
        if (depth >= maxDepth) return target;").AppendLine();

            foreach (var (_, buildMember) in members.OrderBy(i => i.key)) buildMember(code);

            code.Append(@"

        return target;
    }
");

        }

        static (bool needsDepth, short maxDepth) ComputeDepthInfo(TypeMeta type)
        {
            bool needsDepth = type.IsRecursive;
            short maxDepth = 0;
            foreach (var m in type.Members)
            {
                if (m.Type.IsRecursive) needsDepth = true;
                if (m.Type.IsRecursive && m.MaxDepth > maxDepth) maxDepth = m.MaxDepth;
            }
            return (needsDepth, needsDepth && maxDepth == 0 ? (short)1 : maxDepth);
        }

        void BuildFile(StringBuilder code)
        {
            lastWasNullCheck = false;
            var (targetNeedsDepth, targetMaxDepth) = ComputeDepthInfo(targetType);
            GetMethodBuilder(code, targetType, sourceType, MethodName, members, targetNeedsDepth, targetMaxDepth);

            if (!sameType)
            {
                lastWasNullCheck = false;
                var (sourceNeedsDepth, sourceMaxDepth) = ComputeDepthInfo(sourceType);
                GetMethodBuilder(code, sourceType, targetType, ReverseMethodName, reverseMembers, sourceNeedsDepth, sourceMaxDepth);
            }
        }
    }

    internal void GetMeta(
        int targetTypeId,
        int sourceTypeId,
        out ConversionType conversionType,
        out string methodName)
    {
        (conversionType, methodName) =
            (TargetTypeId, SourceTypeId) == (targetTypeId, sourceTypeId)
                ? (_conversionType, MethodName)
                : (_reverseConversionType, ReverseMethodName);
    }

    internal void GetMeta(
        int targetTypeId,
        int sourceTypeId,
        out ConversionType conversionType,
        out string methodName,
        out ConversionType reverseConversionType,
        out string reverseMethodName)
    {
        (conversionType, methodName, reverseConversionType, reverseMethodName) =
            (TargetTypeId, SourceTypeId) == (targetTypeId, sourceTypeId)
                ? (_conversionType, MethodName, _reverseConversionType, ReverseMethodName)
                : (_reverseConversionType, ReverseMethodName, _conversionType, MethodName);
    }
    
    void GetMeta(
        int targetTypeId,
        int sourceTypeId,
        out ConversionType conversionType,
        out string methodName,
        out ConversionType reverseConversionType,
        out string reverseMethodName,
        out Action<string, StringBuilder>? itemMapper,
        out Action<string, StringBuilder>? reverseItemMapper)
    {
        (conversionType, methodName, reverseConversionType, reverseMethodName, itemMapper, reverseItemMapper) =
            (TargetTypeId, SourceTypeId) == (targetTypeId, sourceTypeId)
                ? (_conversionType, MethodName, _reverseConversionType, ReverseMethodName, _itemMapper, _reverseItemMapper)
                : (_reverseConversionType, ReverseMethodName, _conversionType, MethodName, _reverseItemMapper, _itemMapper);
    }

#if DEBUG || DEBUGSGEN
    public override string ToString() => debugString;
#endif
    static string Cast(string targetTypeFullName, string sourceMemberName, bool addNullSafety)
    {
        return $"({targetTypeFullName}){(addNullSafety ? $"({sourceMemberName} ?? default!)" : sourceMemberName)}";
    }

    static string UseMapper(string methodName, string sourceMemberName, bool addNullSafety = false, bool isRecursive = false, bool incrementDepth = false)
    {
        return isRecursive
            ? $"{sourceMemberName}{(addNullSafety ? "?." : ".")}{methodName}(maxDepth, depth{(incrementDepth ? " + 1" : null)})"
            : $"{sourceMemberName}{(addNullSafety ? "?." : ".")}{methodName}()";
    }
}

record struct MemberMatch(bool IsAssignable, bool AllowNull);

internal readonly record struct CollectionMeta(
    TypeMeta ItemType,
    CollectionKind Kind,
    bool IsItemNullable,
    bool Indexable,
    bool Countable,
    bool ArrayBacked,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Kind == CollectionKind.Dictionary;
    internal readonly bool CanUpdate = !ArrayBacked && Method is not null && Kind is CollectionKind.Dictionary or CollectionKind.Stack or CollectionKind.Queue or CollectionKind.Collection;
};
