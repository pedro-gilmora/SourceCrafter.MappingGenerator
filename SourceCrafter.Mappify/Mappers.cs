using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceCrafter.Mappify;

internal delegate void BuildValue(StringBuilder code, string sourceMemberName, bool notNull);

internal sealed partial class Mappers(Compilation compilation, Action<string, string> addSource) : Set<int, TypeMap>(m => m.Id)
{
    internal readonly TypeSet Types = new(compilation);

    internal readonly bool CanUseUnsafeAccessor =
        compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    internal TypeMap GetOrAdd(
        TypeMeta targetType,
        TypeMeta sourceType,
        GenerateOn ignore,
        ref int index, 
        bool dictionaryContext = false)
    {
        bool sameType = targetType.Id == sourceType.Id, isValid = true;
     
        var mapperId = GetId(sourceType.Id, (sameType ? sourceType : targetType).Id);

        ref var typeMap = ref GetOrAddDefault(mapperId, out var exists);

        if (exists)
        {
            return typeMap;
        }

        BuildValue value = AsValue, reverseValue = AsValue;
        Action<StringBuilder>? method = null, reverseMethod = null;

        string methodName, reverseMethodName;

        if (sameType)
        {
            methodName = reverseMethodName = "Copy";
        }
        else
        {
            methodName = "To" + targetType.SanitizedName;
            reverseMethodName = "To" + sourceType.SanitizedName;
        }

        if (/*_isCollection = */sourceType.IsCollection || targetType.IsCollection)
        {
            return typeMap = CreateMap(false);  
        }

        if (targetType.HasConversion(sourceType, out var scalarConversion, out var reverseScalarConversion))
        {
            if (scalarConversion.Exists)
            {
                value = (!targetType.IsInterface && scalarConversion.IsExplicit)
                    ? BuildValueCast(targetType.FullName)
                    : AsValue;

                isValid = true;
            }

            if (reverseScalarConversion.Exists)
            {
                reverseValue = (!sourceType.IsInterface && reverseScalarConversion.IsExplicit)
                    ? BuildValueCast(sourceType.FullName)
                    : AsValue;

                isValid = true;
            }
        }

        if (targetType.IsPrimitive || sourceType.IsPrimitive || sourceType.IsMemberless || targetType.IsMemberless)
        {
            return typeMap = CreateMap(isValid);
        }

        if (!scalarConversion.Exists) value = BuildMapperValue(methodName);

        if (!reverseScalarConversion.Exists) reverseValue = BuildMapperValue(reverseMethodName);

        List<Action<StringBuilder>> members = [], reverseMembers = [];

        method = BuildMethodBuilder(targetType, sourceType, methodName, members);

        reverseMethod = BuildMethodBuilder(sourceType, targetType, reverseMethodName, reverseMembers);

        var allowLowerCase = sourceType.IsTupleType || targetType.IsTupleType /*, hasMatches = false*/;

        var canUseUnsafeAccessor = CanUseUnsafeAccessor;

        typeMap = CreateMap();

        foreach (var targetMember in targetType.Members)
        {
            foreach (var sourceMember in sourceType.Members)
            {
                var map = typeMap;

                int sourceTypeId = sourceMember.Type.Id, targetTypeId = targetMember.Type.Id;

                if (!targetMember.Matches(sourceMember, allowLowerCase, canUseUnsafeAccessor, out var isTargetAssignable, out var isSourceAssignable)
                    || mapperId != GetId(targetTypeId, sourceTypeId)
                        && !(map = GetOrAdd(targetMember.Type, sourceMember.Type, ignore, ref index)).IsValid)
                {
                    continue;
                }

                map.GetMeta(targetTypeId, sourceTypeId, out var useMethod, out var useReverseMethod, out var buildValue, out var buildReverseValue);

                if (isTargetAssignable)
                {
                    typeMap.IsValid = true;
                    members.Add(BuildMemberAssignment(targetMember, sourceMember, useMethod, buildValue!));
                }

                if (isSourceAssignable)
                {
                    typeMap.IsValid = true;
                    reverseMembers.Add(BuildMemberAssignment(sourceMember, targetMember, useReverseMethod, buildReverseValue!));
                }

                break;
            }
        }

        BuildFile(typeMap, ref index);

        return typeMap;

        TypeMap CreateMap(bool isValid = true)
        {
            return new(mapperId, targetType, sourceType, value, reverseValue, methodName, reverseMethodName) { IsValid = isValid };
        }

        void BuildFile(TypeMap typeMap, ref int index)
        {
            StringBuilder code = new();

            if (!typeMap.IsValid) return;

            method?.Invoke(code);

            if (!sameType) reverseMethod?.Invoke(code);

            if (code.Length == 0) return;

            code.Insert(0, @"namespace SourceCrafter.Mappify;

public static partial class Mappings
{");
            addSource(
                $"{index++.ToString().PadLeft(3,'0')}_{sourceType.SanitizedName}_{targetType.SanitizedName}.map.g",
                code.Append("}").ToString());
        }
    }

    private static Action<StringBuilder> BuildMethodBuilder(TypeMeta targetType, TypeMeta sourceType, string methodName, List<Action<StringBuilder>> members)
    {
        bool isInterface = targetType.IsInterface,
            isTargetTypeRecursive = targetType.IsRecursive,
            isTargetValueType = targetType.IsValueType,
            isSourceValueType = sourceType.IsValueType;
        string
            targetFullTypeName = targetType.FullName,
            sourceFullTypeName = sourceType.FullName;

        return BuildMethods;

        void BuildMethods(StringBuilder code)
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

            foreach (var addMemberMapping in members) addMemberMapping(code);

            code.Append(@"

        return target;
    }
");
        }
    }


    private static void AsValue(StringBuilder code, string sourceMemberName, bool addNullSafety = false)
    {
        //code.AppendLine("/*").AppendLine(state.ToString()).Append("*/");
        code.Append("source").Append(sourceMemberName);

        if (addNullSafety) code.Append(" ?? default!");
    }

    private static BuildValue BuildMapperValue(string copyMethodName)
    {
        return AsMapper;

        void AsMapper(StringBuilder code, string sourceMemberName, bool addNullSafety = false)
        {
            code.Append("source").Append(sourceMemberName);

            if (addNullSafety) code.Append("?");

            code.Append(".").Append(copyMethodName).Append("()");
        }
    }

    private static BuildValue BuildValueCast(string targetTypeFullName)
    {
        return Cast;

        void Cast(StringBuilder code, string sourceMemberName, bool addNullSafety)
        {

            code.Append('(')
                .Append(targetTypeFullName)
                .Append(")");

            //code.Append("/*").Append(state).Append("*/");

            if (addNullSafety)
                code.Append("(source")
                    .Append(sourceMemberName)
                    .Append(" ?? default!)");
            /*(").Append(state.sourceTypeFullName).Append(")*/
            else
                code.Append("source")
                    .Append(sourceMemberName);
        }
    }

    internal MemberMeta CreateMember(ITypeSymbol item, string name)
    {
        var sourceType = Types.GetOrAdd(item);
        
        return new(
            sourceType.Id,
            name,
            sourceType,
            isNullable: item.IsNullable);
    }
    internal static int GetId(int typeAId, int typeBId) =>
        (Math.Min(typeAId, typeBId), Math.Max(typeAId, typeBId)).GetHashCode();

    internal void RenderExtra(Action<string, string> addSource)
    {
        StringBuilder code = new(@"namespace SourceCrafter.Mappify;

public static partial class Mappings
{");
        var len = code.Length;

        foreach (var item in Types.UnsafeAccessors)
        {
            item.Render(code);
        }

        if (len == code.Length) return;

        addSource("MappingExtras", code.Append("\n}").ToString());
    }


    private Action<StringBuilder> BuildMemberAssignment(
            MemberMeta target,
            MemberMeta source,
            bool useFillMethod,
            BuildValue buildSourceValue)
    {
        string targetMemberName = '.' + target.Name,
            sourceMemberName = '.' + source.Name,
            targetTypeFullName = target.Type.FullName,
            unsafeFieldAccesor = target.UnsafeFieldAccesor;

        bool isTargetValueType = target.Type.IsValueType,
            isSourceValueType = source.Type.IsValueType,
            isSourceNullable = source.IsNullable,
            isTargetNullable = target.IsNullable,
            isTargetRecursive = target.Type.IsRecursive,
            canWrite = target.CanWrite,
            useUnsafeAccessor = target.UseUnsafeAccessor,
            isParentValueType = target.IsParentValueType;

        short maxDepth = target.MaxDepth;


        var addNullSafety = buildSourceValue.Method.Name switch
        {
            "AsCast" => !isTargetNullable || isSourceNullable || isSourceValueType,
            "AsValue" => isSourceNullable && !isTargetNullable,
            _ => isSourceNullable
        };

        return !source.Type.IsMemberless && (target.IsNullable || source.IsNullable)
            ? NullableAssignment
            : useFillMethod
                ? UpdateMethodAssignment
                : DefaultAssignment;

        void NullableAssignment(StringBuilder code)
        {
            if (ShouldAppendNewLine(code))
                code.AppendLine();

            if (isSourceNullable)
            {
                code.Append(@"
        if (source")
                    .Append(sourceMemberName)
                    .Append(AppendNullCheck(isSourceValueType, isSourceNullable));

                if (isTargetRecursive)
                    code.Append(" && __l <= ").Append(maxDepth);

                code.Append(") ");
            }

            if (isTargetNullable)
            {

                code.Append(@"
");
                if (isSourceNullable) code.Append("    ");

                code.Append("        if(target")
                    .Append(targetMemberName)
                    .Append(AppendNullCheck(isTargetValueType, isTargetNullable))
                    .Append(") ");

                AppendAssignmentCall(code);

                code.Append(@";
");
                if (isSourceNullable) code.Append("    ");

                code.Append("        else ");

                if (!canWrite && useUnsafeAccessor)
                    AppendTargetValue(code);
                else
                    code.Append("target").Append(targetMemberName);

                code.Append(" = ");

                buildSourceValue(code, sourceMemberName, false);
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

        void UpdateMethodAssignment(StringBuilder code)
        {
            code.Append(@"
        ");

            AppendAssignmentCall(code);

            code.Append(@";");
        }

        void DefaultAssignment(StringBuilder code)
        {
            code.Append(@"
        ");

            if (useUnsafeAccessor)
                AppendTargetValue(code);
            else
                code.Append("target").Append(targetMemberName);

            code.Append(" = ");

            buildSourceValue(code, sourceMemberName, addNullSafety);

            code.Append(";");
        }

        void AppendTargetValue(StringBuilder code)
        {
            code.Append(unsafeFieldAccesor).Append("(");

            if (isParentValueType) code.Append("ref ");

            code.Append("target)");
        }

        void AppendAssignmentCall(StringBuilder code)
        {
            code.Append("Update(");

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

            if (isTargetRecursive)
                code.Append(", __l");

            code.Append(")");
        }
    }

    private static bool ShouldAppendNewLine(StringBuilder code) => code[^1] is ';' or '{';

    private static string AppendNullCheck(bool isValueType, bool isNullable) =>
        isValueType && isNullable ? ".HasValue" : " is not null";


}