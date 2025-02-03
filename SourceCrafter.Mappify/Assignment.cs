using System;
using System.Text;

namespace SourceCrafter.Mappify;

internal readonly struct Assignment
{
    internal bool IsCollectionItem { get; init; } = false;

    private readonly ValueBuilder buildSourceValue = null!;
    internal readonly Action<StringBuilder> Assign;

    private readonly string 
        _targetMemberName,
        _sourceMemberName,
        _copyMethodName,
        _targetTypeFullName,
        _unsafeFieldAccesor;

    private readonly bool 
        _isTargetValueType,
        _isSourceValueType,
        _isSourceNullable,
        _isTargetNullable,
        _isTargetRecursive,
        _canWrite,
        _useUnsafeAccessor,
        _isParentValueType;

    private readonly int _maxDepth;
    private readonly string _updateMethodName;

    public Assignment(
        MemberMeta target,
        MemberMeta source,
        string copyMethodName,
        string updateMethodName,
        bool useFillMethod,
        ConversionType sourceValueBuilder,
        bool isCollectionItem = false)
    {
        _updateMethodName = updateMethodName;
        _targetMemberName = '.' + target.Name;
        _sourceMemberName = '.' + source.Name;
        _copyMethodName = copyMethodName;
        _targetTypeFullName = target.Type.FullName;
        _unsafeFieldAccesor = target.UnsafeFieldAccesor;
        _isTargetValueType = target.Type.IsValueType;
        _isSourceValueType = source.Type.IsValueType;
        _isSourceNullable = source.IsNullable;
        _isTargetNullable = target.IsNullable;
        _isTargetRecursive = target.Type.IsRecursive;
        _canWrite = target.CanWrite;
        _useUnsafeAccessor = target.UseUnsafeAccessor;
        _isParentValueType = target.IsParentValueType;
        _maxDepth = target.MaxDepth;

        buildSourceValue = sourceValueBuilder switch { 
            ConversionType.Cast => AsCast,
            ConversionType.Mapper => AsMapper,
            _ => AsValue
        };

        Assign = !source.Type.IsMemberless && (target.IsNullable || source.IsNullable)
                ? NullableAssignment
                : useFillMethod
                    ? UpdateMethodAssignment
                    : DefaultAssignment;
    }

    internal void NullableAssignment(StringBuilder code)
    {
        if (ShouldAppendNewLine(code))
            code.AppendLine();

        if (_isSourceNullable)
        {
            code.Append(@"
        if (source")
                .Append(_sourceMemberName)
                .Append(AppendNullCheck(_isSourceValueType, _isSourceNullable));

            if (_isTargetRecursive)
                code.Append(" && __l <= ").Append(_maxDepth);

            code.Append(") ");
        }
        if (_isTargetNullable)
        {

            code.Append(@"
");
            if(_isSourceNullable) code.Append("    ");
            
            code.Append("        if(target")
                .Append(_targetMemberName)
                .Append(AppendNullCheck(_isTargetValueType, _isTargetNullable))
                .Append(") ");

            AppendAssignmentCall(code);

            code.Append(@";
");
            if(_isSourceNullable) code.Append("    ");
            
            code.Append("        else ");

            if (!_canWrite && _useUnsafeAccessor)
                AppendTargetValue(code);
            else
                code.Append("target").Append(_targetMemberName);

            code.Append(" = ");

            buildSourceValue(code, true);
        }
        else
        {
            AppendAssignmentCall(code);
        }

        code.Append(";");

        if (_isSourceNullable)
        {
            code.Append(@"
        else ");

            if (_useUnsafeAccessor)
                AppendTargetValue(code);
            else
                code.Append("target").Append(_targetMemberName);

            code.Append(" = default;");
        }

        code.AppendLine();
    }

    internal void UpdateMethodAssignment(StringBuilder code)
    {
        code.Append(@"
        ");

        AppendAssignmentCall(code);

        code.Append(@";");
    }

    internal void DefaultAssignment(StringBuilder code)
    {
        code.Append(@"
        ");

        if (_useUnsafeAccessor)
            AppendTargetValue(code);
        else
            code.Append("target").Append(_targetMemberName);
        
        code.Append(" = ");

        buildSourceValue(code);
        
        code.Append(";");
    }

    internal void AppendTargetValue(StringBuilder code)
    {
        code.Append(_unsafeFieldAccesor).Append("(");

        if (_isParentValueType) code.Append("ref ");

        code.Append("target)");
    }

    private void AppendAssignmentCall(StringBuilder code)
    {
        code.Append(_updateMethodName).Append('(');

        if (_useUnsafeAccessor)
        {
            if (_isTargetValueType)
            {
                code.Append("ref ");

                if (_isTargetNullable)
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

                if (_isTargetNullable)
                    code.Append('!');
            }
        }
        else
        {
            if (_isTargetValueType)
                code.Append("ref ");

            code.Append("target").Append(_targetMemberName);
        }

        code.Append(", source").Append(_sourceMemberName);

        if (_isSourceNullable)
            code.Append(_isSourceValueType ? ".Value" : "!");

        if (_isTargetRecursive)
            code.Append(", __l");

        code.Append(")");
    }

    internal void AsValue(StringBuilder code, bool dontChecknull = false)
    {
        //code.AppendLine("/*").AppendLine(state.ToString()).Append("*/");
        code.Append("source").Append(_sourceMemberName);

        if (dontChecknull || !_isSourceNullable || _isTargetNullable) return;

        code.Append(" ?? default!");
    }

    internal void AsCast(StringBuilder code, bool dontCheckNull = false)
    {
        code.Append('(')
            .Append(_targetTypeFullName)
            .Append(")");

        //code.Append("/*").Append(state).Append("*/");

        if (!dontCheckNull && !_isTargetNullable && _isSourceNullable && _isSourceValueType)
            code.Append("(source")
                .Append(_sourceMemberName)
                .Append(" ?? default!)");
        /*(").Append(state.sourceTypeFullName).Append(")*/
        else
            code.Append("source")
                .Append(_sourceMemberName);
    }

    internal void AsMapper(StringBuilder code, bool dontCheckNull = false)
    {
        code.Append("source").Append(_sourceMemberName);

        if (!dontCheckNull && _isSourceNullable) code.Append("?");

        code.Append(".").Append(_copyMethodName).Append("()");
    }

    private static bool ShouldAppendNewLine(StringBuilder code) => code[^1] is ';' or '{';

    private static string AppendNullCheck(bool isValueType, bool isNullable) =>
        isValueType && isNullable ? ".HasValue" : " is not null";

//    public override string ToString()
//    {
//        return $@"targetTypeFullName={targetTypeFullName},
//targetMemberName={targetMemberName},
//sourceTypeFullName={sourceTypeFullName},
//sourceMemberName={sourceMemberName},
//useUnsafeAccessor={useUnsafeAccessor},
//isTargetValueType={isTargetValueType},
//isTargetNullable={isTargetNullable},
//isSourceNullable={isSourceNullable},
//isSourceValueType={isSourceValueType},
//recursive={recursive},
//isParentValueType={isParentValueType},
//copyMethodName={copyMethodName},
//updateMethodName={updateMethodName},
//unsafeFieldAccesor={unsafeFieldAccesor},
//maxDepth={maxDepth}";
//    }
}