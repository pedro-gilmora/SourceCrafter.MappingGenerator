using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace SourceCrafter.Mappify;

internal delegate bool CacheCreator(string item, out string cachedItem);
internal delegate void MapperMethodCreator(StringBuilder code, Action<StringBuilder> itemMapper);
enum ConversionType { None, Cast, Mapper }
internal sealed class TypeMap
{
    internal readonly int Id, TargetTypeId, SourceTypeId;

    private readonly ConversionType _conversionType, _reverseConversionType;

    internal readonly bool IsValid = true;

    internal readonly string MethodName, ReverseMethodName;

    private readonly string debugString;

    private readonly bool _rendered;

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

        if (/*_isCollection = */sourceType.IsCollection && targetType.IsCollection)
        {
            if (IsDictionaryMapping()) _conversionType = _reverseConversionType = ConversionType.Mapper;

            IsValid = false;

            return;
        }

        IsValid = targetType.HasConversion(types.Compilation, sourceType, out _conversionType, out _reverseConversionType);

        if (targetType.IsPrimitive || sourceType.IsPrimitive || sourceType.IsMemberless || targetType.IsMemberless) return;

        if (!targetType.IsInterface) _conversionType = ConversionType.Mapper;

        if (!sourceType.IsInterface) _reverseConversionType = ConversionType.Mapper;

        List<(int, Action<StringBuilder>)> members = [], reverseMembers = [];

        var allowLowerCase = sourceType.IsTupleType || targetType.IsTupleType /*, hasMatches = false*/;

        var canUseUnsafeAccessor = mappers.CanUseUnsafeAccessor;

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

        bool IsDictionaryMapping()
        {
            switch (targetType, sourceType)
            {
                case ({ Collection.Type: EnumerableType.Dictionary }, { Collection.Type: EnumerableType.Dictionary }):

                    var targetKeyMember = targetType.Collection.ItemType.Members[0]!;
                    var sourceKeyMember = sourceType.Collection.ItemType.Members[0]!;

                    if (mappers.GetOrAdd(targetKeyMember.Type, sourceKeyMember.Type, ApplyTo.None) is not { IsValid: true } keyMapper) return false;

                    var targetValueMember = targetType.Collection.ItemType.Members[1]!;
                    var sourceValueMember = sourceType.Collection.ItemType.Members[1]!;

                    if (mappers.GetOrAdd(targetValueMember.Type, sourceValueMember.Type, ApplyTo.None) is not { IsValid: true } valueMapper) return false;

                    BuildDictionaryMapperFile(
                        targetKeyMember,
                        targetValueMember,
                        sourceKeyMember,
                        sourceValueMember,
                        keyMapper,
                        valueMapper);

                    return true;
                case ({ Collection.Type: EnumerableType.Dictionary }, _):
                    return false;
                case (_, { Collection.Type: EnumerableType.Dictionary }):
                    return false;
                default:
                    return false;
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

            if (sameType) return;

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
            bool useSourceKeyFieldUnsafeAccesor = sourceKeyMember.UseUnsafeAccessor;
            bool useSourceValueFieldUnsafeAccesor = sourceValueMember.UseUnsafeAccessor;

            // Build key access expression
            string keySourceAccess = useSourceKeyFieldUnsafeAccesor
                ? $"{sourceKeyMember.UnsafeFieldAccesor}(sourceItem)"
                : $"sourceItem{(isSourceNullable ? "?." : ".")}{sourceKeyMemberName}";

            switch (keyConversionType)
            {
                case ConversionType.Cast:
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
                ? $"{sourceValueMember.UnsafeFieldAccesor}(sourceItem)"
                : $"sourceItem{(!checkedNull && isSourceNullable ? "?." : ".")}{sourceValueMemberName}";


            switch (valueConversionType)
            {
                case ConversionType.Cast:
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
            useUpdate = conversionType is ConversionType.Mapper && target is not { Type: { IsCollection: true, Collection.Type: EnumerableType.Dictionary } },
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
            targetTypeFullName = target.Type.FullName;;

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
                    code.Append("if(").Append(cachedSourceMemberExpr).Append(" is {} _source").Append(source.Name).Append(@")
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
                        case ConversionType.Cast:
                            code.Append(Cast(targetTypeFullName, cachedSourceMemberExpr, false));
                            break;
                        case ConversionType.Mapper:
                            code.Append(UseMapper(methodName, cachedSourceMemberExpr, false));
                            break;
                        default:
                            code.Append(cachedSourceMemberExpr);
                            break;
                    }

                    code.Append(';');
                }

                if (isSourceNullable) code.Append(@"
        }");
                if(isTargetNullable || allowSourceNull) code.Append(@"
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
                    case ConversionType.Cast:
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
    bool BackingArray,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Type == EnumerableType.Dictionary;
};

internal record struct CollectionMapping(
    bool CreateArray,
    bool UseLenInsteadOfIndex,
    string Iterator,
    bool Redim,
    string? Method,
    string MethodName);
