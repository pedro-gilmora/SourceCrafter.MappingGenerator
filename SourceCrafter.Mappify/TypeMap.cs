using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
            if (IsCollectionMapping())
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

                map.GetMeta(targetTypeId, sourceTypeId, out var memberConversionType, out string memberTypeMethod, out var reverseMemberConversionType, out string reverseMemberTypeMethod);

                if (targetMatch.IsAssignable)
                {
                    IsValid = true;
                    members.Add((i, BuildMemberAssignment(targetMember, sourceMember, memberConversionType, memberTypeMethod, targetMatch.AllowNull)));
                }

                if (sourceMatch.IsAssignable)
                {
                    IsValid = true;
                    reverseMembers.Add((i, BuildMemberAssignment(sourceMember, targetMember, reverseMemberConversionType, reverseMemberTypeMethod, sourceMatch.AllowNull)));
                }

                break;
            }
        }

        if (IsValid && members.Count + reverseMembers.Count > 0) methods.Add(code => BuildFile(code, MethodName, ReverseMethodName));

        bool IsCollectionMapping()
        {
            switch (targetType, sourceType)
            {
                case ({ Collection.Type: EnumerableType.Dictionary }, { Collection.Type: EnumerableType.Dictionary }):

                    var targetKeyMember = targetType.Collection.ItemType.Members[0]!;
                    var sourceKeyMember = sourceType.Collection.ItemType.Members[0]!;

                    if (mappers.GetOrAdd(targetKeyMember.Type, sourceKeyMember.Type) is not { IsValid: true } keyMapper) return false;

                    var targetValueMember = targetType.Collection.ItemType.Members[1]!;
                    var sourceValueMember = sourceType.Collection.ItemType.Members[1]!;

                    if (mappers.GetOrAdd(targetValueMember.Type, sourceValueMember.Type) is not { IsValid: true } valueMapper) return false;

                    BuildDictionaryMapperFile(
                        targetKeyMember,
                        targetValueMember,
                        sourceKeyMember,
                        sourceValueMember,
                        keyMapper,
                        valueMapper);

                    return true;
                case ({ Collection.Type: EnumerableType.Dictionary }, _):

                    return TryMapKeyValuePairLike(targetType, sourceType);

                case (_, { Collection.Type: EnumerableType.Dictionary }):

                    return TryMapKeyValuePairLike(sourceType, targetType);

                default:

                    var itemTypeMap = mappers.GetOrAdd(targetType.Collection.ItemType, sourceType.Collection.ItemType);

                    if (!itemTypeMap.IsValid) return false;

                    itemTypeMap.GetMeta(targetType.Collection.ItemType.Id, sourceType.Collection.ItemType.Id, out var conversionType, out string typeMethod, out var reverseConversionType, out string reverseTypeMethod);

                    methods.Add(code => BuildCollectionMapping(
                        code,
                        targetType.Collection,
                        sourceType.Collection,
                        conversionType,
                        typeMethod,
                        MethodName,
                        targetType.ExportFullName,
                        sourceType.ExportFullName));

                    if (sourceType.Id == targetType.Id && targetType.Collection.ItemType.Id == sourceType.Collection.ItemType.Id) return true;

                    methods.Add(code => BuildCollectionMapping(
                        code,
                        sourceType.Collection,
                        targetType.Collection,
                        reverseConversionType,
                        reverseTypeMethod,
                        ReverseMethodName,
                        sourceType.ExportFullName,
                        targetType.ExportFullName));

                    return true;
            }

            bool TryMapKeyValuePairLike(TypeMeta targetType, TypeMeta sourceType)
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

                TypeMap? valueMapper = null;
                var sourceValueTypeId = 0;
                MemberMeta? sourceValueMember = null;
                foreach (var sourceMember in sourceType.Collection.ItemType.Members)
                {
                    sourceValueTypeId = sourceMember.Type.Id;
                    valueMapper = mappers.GetOrAdd(targetValueMember.Type, sourceMember.Type, ignore);

                    if (sourceMember.Id == sourceKeyMember.Id
                        || !targetValueMember.Matches(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var targeValueMatch, out var sourcValueMatch)
                        && !valueMapper.IsValid)

                        continue;

                    sourceValueMember = sourceMember;
                    break;
                }

                if (sourceValueMember is null || valueMapper is null) return false;

                BuildDictionaryMapperFile(
                    targetKeyMember,
                    targetValueMember,
                    sourceKeyMember,
                    sourceValueMember,
                    keyMapper,
                    valueMapper);

                return true;
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
        ").Append(targetExportFullName).Append(@" target = new();
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
            else if (sourceType.Collection.Type is not EnumerableType.Dictionary)
            {
                var itemTypeMap = mappers.GetOrAdd(sourceType.Collection.ItemType, targetType.Collection.ItemType);

                itemTypeMap.GetMeta(sourceType.Collection.ItemType.Id, targetType.Collection.ItemType.Id, out var reverseConversionType, out string reverseTypeMethod, out var conversionType, out string typeMethod);

                methods.Add(code => BuildCollectionMapping(
                    code,
                    sourceType.Collection,
                    targetType.Collection,
                    reverseConversionType,
                    reverseTypeMethod,
                    ReverseMethodName,
                    sourceType.FullName,
                    targetType.FullName));

                return;
            }

            methods.Add(code =>
            {
                code.Append(@"
    public static ").Append(sourceExportFullName).Append(" ").Append(ReverseMethodName).Append("(this ").Append(targetExportFullName).Append(@" source)
    {
        ").Append(sourceExportFullName).Append(@" target = new();
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

        void BuildCollectionMapping(
            StringBuilder code,
            CollectionMeta targetMeta,
            CollectionMeta sourceMeta,
            ConversionType conversionType,
            string itemMethod,
            string method,
            string targetFullTypeName,
            string sourceFullTypeName,
            short maxDepth = 0)
        {
            bool isTargetNullable = targetType.Collection.IsItemNullable,
                isSourceNullable = sourceType.Collection.IsItemNullable;

            TypeMeta targetItem = targetMeta.ItemType,
                     sourceItem = sourceMeta.ItemType;

            var createArray = targetMeta.ArrayBacked;
            var useLenInsteadOfIndex = targetMeta.ArrayBacked && !sourceMeta.Indexable;
            var useFor = sourceMeta.Countable && (sourceMeta.Indexable || targetMeta.ArrayBacked);
            var iterator = useFor ? "for" : "foreach";
            var redim = !sourceMeta.Countable && targetMeta.ArrayBacked;

            bool isRecursive = targetItem.IsRecursive, isSourceValueType = sourceItem.IsValueType;

            string
                countProp = targetMeta.CountProp,
                targetItemFullTypeName = targetItem.ExportFullName,
                targetExportFullXmlDocTypeName = targetFullTypeName.Replace('<', '{').Replace('>', '}'),
                sourceExportFullXmlDocTypeName = sourceType.ExportFullName.Replace('<', '{').Replace('>', '}'),
                underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

            var suffix = (sourceMeta.Type, targetMeta.Type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null;

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
                code.Append(@", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ");

                if (targetMeta.Type == EnumerableType.ReadOnlyCollection)
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
                if (useFor && targetMeta.Countable)
                {
                    code.Append(@"
        int len = ");
                    code.Append("source.").Append(countProp).Append(';');
                }

                code.Append(@"
        var target = new ")
                    .Append(targetMeta.Type switch
                    {
                        EnumerableType.ReadOnlyCollection => "System.Collections.ObjectModel.ReadOnlyCollection<" + targetItemFullTypeName + '>',
                        _ => targetFullTypeName
                    })
                    .Append(@"();
");
            }

            if (useFor)
            {
                code.Append(@"
        for (int i = 0; i < len; i++)
        {");

                var sourceMemberExpression = "source[i]";

                if (isSourceNullable)
                {
                    code.Append(@"
            if (source[i] is not {} sourceItem) continue;
");

                    sourceMemberExpression = "sourceItem";
                }

                code.Append(@"
            target[i] = ");

                switch (conversionType)
                {
                    case ConversionType.Explicit:
                        code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceNullable));
                        break;
                    case ConversionType.Mapper:
                        code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceNullable));
                        break;
                    default:
                        code.Append(sourceMemberExpression);

                        if (!isTargetNullable && isSourceNullable && isSourceValueType) code.Append(" ?? default!");
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

                if (isSourceNullable)
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

                    switch (conversionType)
                    {
                        case ConversionType.Explicit:
                            code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceNullable));
                            break;
                        case ConversionType.Mapper:
                            code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceNullable));
                            break;
                        default:
                            code.Append(sourceMemberExpression);

                            if (!isTargetNullable && isSourceNullable && isSourceValueType) code.Append(" ?? default!");
                            break;
                    }

                    code.Append(";");

                    if (redim)
                    {
                        //redim array
                        code.Append(@"

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
                        code.Append(@"
        }

        return target").Append(suffix).Append(@";
    }
");
                    }
                }
                else
                {
                    if (isSourceNullable)
                    {
                        code.Append(@"
            if (sourceItem is null) continue;
");
                    }

                    code.Append(@"
            target.").Append(targetMeta.Method).Append('(');

                    switch (conversionType)
                    {
                        case ConversionType.Explicit:
                            code.Append(Cast(targetItemFullTypeName, sourceMemberExpression, isSourceNullable));
                            break;
                        case ConversionType.Mapper:
                            code.Append(UseMapper(itemMethod, sourceMemberExpression, isSourceNullable));
                            break;
                        default:
                            code.Append(sourceMemberExpression);

                            if (!isTargetNullable && isSourceNullable && isSourceValueType) code.Append(" ?? default!");
                            break;
                    }

                    code.Append(@");
        }

        return ").Append(targetMeta.Type switch
                    {
                        EnumerableType.ReadOnlyCollection => $"new {targetFullTypeName}(target)",
                        _ => "target"
                    }).Append(@";
    }
");
                }
            }
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

        static void GetMethodBuilder(StringBuilder code, TypeMeta targetType, TypeMeta sourceType, string methodName, List<(int, Action<StringBuilder>)> members)
        {
            bool isInterface = targetType.IsInterface,
                isTargetTypeRecursive = targetType.IsRecursive,
                isTargetValueType = targetType.IsValueType,
                isSourceValueType = sourceType.IsValueType;
            string
                targetFullTypeName = targetType.FullName,
                sourceFullTypeName = sourceType.FullName;

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
            foreach (var addMemberMapping in members.OrderBy(i => i.Item1)) addMemberMapping.Item2(code);

            code.Append(@"

        return target;
    }
");

        }

        void BuildFile(StringBuilder code, string methodName, string reverseMethodName)
        {
            lastWasNullCheck = false;
            GetMethodBuilder(code, targetType, sourceType, methodName, members);

            if (!sameType)
            {
                lastWasNullCheck = false;
                GetMethodBuilder(code, sourceType, targetType, reverseMethodName, reverseMembers);
            }
        }
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

#if DEBUG || DEBUGSGEN
    public override string ToString() => debugString;
#endif

    bool lastWasNullCheck;

    private Action<StringBuilder> BuildMemberAssignment(
            MemberMeta target,
            MemberMeta source,
            ConversionType conversionType,
            string methodName,
            bool allowSourceNull)
    {
        bool isTargetValueType = target.Type.IsValueType,
            useUpdate = conversionType.HasFlag(ConversionType.Mapper) && target is not { Type: { IsCollection: true } },
            useUnsafeSetterAccessor = (!target.CanWrite && target.UseUnsafeAccessor) || (useUpdate && target.Type.IsValueType),
            useUnsafeGetterAccessor = target is { CanRead: false, UseUnsafeAccessor: true },
            isSourceValueType = source.Type.IsValueType,
            isSourceNullable = source.IsNullable,
            isTargetNullable = target.IsNullable,
            isTargetRecursive = target.Type.IsRecursive,
            isParentValueType = target.IsParentValueType;

        string targetMemberReadExpression = useUnsafeGetterAccessor
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
                    code.Append(targetMemberWriteExpression);
                }

                code.Append(isTargetNullable ? isTargetValueType ? ".UnNull()." : useUnsafeSetterAccessor ? "." : "?." : ".")
                    .Append("Update(").Append(cachedSourceMemberExpr).Append(");");

                if (isSourceNullable && isTargetNullable)
                {
                    code.Append(@"
        ").Append(indent).Append("else ").Append(cachedTargetMemberExpr).Append(" = ");

                    switch (conversionType)
                    {
                        case ConversionType.Mapper and not ConversionType.Implicit:
                            code.Append(UseMapper(methodName, cachedSourceMemberExpr, false));
                            break;
                        case ConversionType.Explicit:
                            code.Append(Cast(targetTypeFullName, cachedSourceMemberExpr, false));
                            break;
                        default:
                            code.Append(cachedSourceMemberExpr);
                            break;
                    }

                    code.Append(';');
                }

                if (isSourceNullable) code.Append(@"
        }");
                if (isTargetNullable || allowSourceNull) code.Append(@"
        else
        {
            ").Append(useUnsafeSetterAccessor ? targetMemberWriteExpression : cachedTargetMemberExpr).Append(@" = default;
        }");

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

                switch (conversionType)
                {
                    case ConversionType.Explicit and not ConversionType.Implicit:
                        code.Append(Cast(targetTypeFullName, sourceMemberExpression, isSourceNullable));
                        break;
                    case ConversionType.Mapper:
                        code.Append(UseMapper(methodName, sourceMemberExpression, isSourceNullable));
                        break;
                    default:
                        code.Append(sourceMemberExpression);

                        if (!isTargetNullable && isSourceNullable && isSourceValueType) code.Append(" ?? default!");
                        break;
                }

                code.Append(';');
            }
        };
    }

    static string Cast(string targetTypeFullName, string sourceMemberName, bool addNullSafety)
    {
        return $"({targetTypeFullName}){(addNullSafety ? $"({sourceMemberName} ?? default!)" : sourceMemberName)}";
    }

    static string UseMapper(string methodName, string sourceMemberName, bool addNullSafety = false)
    {
        return $"{sourceMemberName}{(addNullSafety ? "?." : ".")}{methodName}()";
    }
}

record struct MemberMatch(bool IsAssignable, bool AllowNull);

internal readonly record struct CollectionMeta(
    TypeMeta ItemType,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool Countable,
    bool ArrayBacked,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Type == EnumerableType.Dictionary;
};

internal record struct CollectionMappingMeta(
    bool CreateArray,
    bool UseLenInsteadOfIndex,
    string Iterator,
    bool Redim,
    string? Method,
    string MethodName);
