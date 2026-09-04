using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace SourceCrafter.Mapifier;

internal readonly record struct EnumFieldMeta(string FullName, string Description, string CategoryStr, string ConstantValue);

/// <param name="ContainerNames">
/// Names of the enclosing types and finally the assembly, innermost first. Used to disambiguate
/// two enums that sanitize to the same name without needing their symbols back.
/// </param>
internal readonly record struct EnumMeta(
    string FullName,
    string NotNullFullName,
    ImmutableArray<EnumFieldMeta> Members,
    string SanitizedName,
    ImmutableArray<string> ContainerNames)
{
    private const string
        MemberIndent = @"
        ",
        CaseIndent = @"
                    ",
        MethodCaseIndent = @"
                ";

    /// <summary>
    /// Renders the enum helpers as a single C# 14 extension block on the enum type, plus the
    /// lazily populated caches that back its collection properties.
    /// </summary>
    internal readonly void BuildEnumMethods(StringBuilder code)
    {
        if (Members.Length == 0) return;

        string?
            collectionsComma = null,
            categoriesComma = null,
            values = null,
            descriptions = null,
            categories = null,
            names = null,
            nameCases = null,
            descriptionCases = null,
            definedByName = null,
            definedByInt = "",
            tryGetValueCases = null,
            tryGetNameCases = null,
            tryGetDescCases = null;

        // Members inherit the category of the last member that declared one, so a [Category]
        // attribute acts as a section header over the members that follow it.
        Dictionary<string, HashSet<string>> categoriesSet = new(StringComparer.Ordinal);
        string? lastCategory = null;

        foreach (var m in Members)
        {
            values += collectionsComma + m.FullName;

            if (m.CategoryStr.Length > 2)
            {
                if (categoriesSet.TryGetValue(m.CategoryStr, out var set))
                {
                    set.Add(m.FullName);
                }
                else
                {
                    categories += categoriesComma + m.CategoryStr;

                    categoriesSet.Add(m.CategoryStr, new(StringComparer.Ordinal) { m.FullName });

                    categoriesComma ??= "," + CaseIndent;
                }

                lastCategory = m.CategoryStr;
            }
            else if (lastCategory is { })
            {
                categoriesSet[lastCategory].Add(m.FullName);
            }

            names += collectionsComma + "nameof(" + m.FullName + ")";

            descriptions += collectionsComma + m.Description;

            nameCases += CaseIndent + "case " + m.FullName + ": return nameof(" + m.FullName + ");";

            descriptionCases += CaseIndent + "case " + m.FullName + ": return " + m.Description + ";";

            var distinctIntCase = "case " + m.ConstantValue + ":";

            if (!definedByInt.Contains(distinctIntCase))
                definedByInt += CaseIndent + distinctIntCase;

            definedByName += CaseIndent + "case nameof(" + m.FullName + "):";

            tryGetValueCases += CaseIndent + "case nameof(" + m.FullName + "): result = " + m.FullName + "; return true;";

            tryGetNameCases += CaseIndent + "case " + m.FullName + ": result = nameof(" + m.FullName + "); return true;";

            tryGetDescCases += CaseIndent + "case " + m.FullName + ": result = " + m.Description + "; return true;";

            collectionsComma ??= "," + CaseIndent;
        }

        var hasCategories = categoriesSet.Count > 0;

        AppendCacheFields(code, hasCategories);

        code.Append(@"
    extension(")
            .Append(NotNullFullName)
            .Append(@" value)
    {");

        AppendLookupProperty(code, "Description", descriptionCases);

        if (hasCategories) AppendLookupProperty(code, "Category", BuildCategoryCases());

        AppendLookupProperty(code, "Name", nameCases);

        AppendTryGet(code, "TryGetDescription", tryGetDescCases);

        if (hasCategories) AppendTryGet(code, "TryGetCategory", BuildTryGetCategoryCases());

        AppendTryGet(code, "TryGetName", tryGetNameCases);

        AppendCachedCollection(code, "Descriptions", "string", descriptions);

        if (hasCategories) AppendCachedCollection(code, "Categories", "string", categories);

        AppendCachedCollection(code, "Names", "string", names);

        AppendCachedCollection(code, "Values", NotNullFullName, values);

        AppendIsDefined(code, "string", "name", definedByName);

        AppendIsDefined(code, "int", "number", definedByInt);

        AppendTryGetValue(code, tryGetValueCases);

        code.Append(@"
    }");

        string BuildCategoryCases()
        {
            var result = "";

            foreach (var item in categoriesSet)
            {
                foreach (var member in item.Value)
                    result += CaseIndent + "case " + member + ":";

                result += " return " + item.Key + ";";
            }

            return result;
        }

        string BuildTryGetCategoryCases()
        {
            var result = "";

            foreach (var item in categoriesSet)
            {
                foreach (var member in item.Value)
                    result += CaseIndent + "case " + member + ":";

                result += " result = " + item.Key + "; return true;";
            }

            return result;
        }
    }

    private readonly void AppendCacheFields(StringBuilder code, bool hasCategories)
    {
        code.Append(@"
    private static ")
            .Append(NotNullFullName)
            .Append("[]? _cached")
            .Append(SanitizedName)
            .Append(@"Values;

    private static string[]?
        _cached")
            .Append(SanitizedName)
            .Append("Descriptions,");

        if (hasCategories)
            code.Append(@"
        _cached")
                .Append(SanitizedName)
                .Append("Categories,");

        code.Append(@"
        _cached")
            .Append(SanitizedName)
            .Append(@"Names;
");
    }

    private static void AppendLookupProperty(StringBuilder code, string propertyName, string? cases)
    {
        code.Append(MemberIndent)
            .Append("public string? ")
            .Append(propertyName)
            .Append(@"
        {
            get
            {
                switch(value)
                {")
            .Append(cases)
            .Append(@"
                    default: return null;
                }
            }
        }
");
    }

    private static void AppendTryGet(StringBuilder code, string methodName, string? cases)
    {
        code.Append(MemberIndent)
            .Append("public bool ")
            .Append(methodName)
            .Append(@"(out string? result)
        {
            switch(value)
            {")
            .Append(Reindent(cases))
            .Append(MethodCaseIndent)
            .Append(@"default: result = null; return false;
            }
        }
");
    }

    private readonly void AppendCachedCollection(StringBuilder code, string propertyName, string elementType, string? items)
    {
        var field = "_cached" + SanitizedName + propertyName;

        code.Append(MemberIndent)
            .Append("public static ")
            .Append(elementType)
            .Append("[] ")
            .Append(propertyName)
            .Append(@"
        {
            get
            {
                if (")
            .Append(field)
            .Append(" is not null) return ")
            .Append(field)
            .Append(@";

                lock (__lock) return ")
            .Append(field)
            .Append(" ??= new ")
            .Append(elementType)
            .Append(@"[]
                {
                    ")
            .Append(items)
            .Append(@"
                };
            }
        }
");
    }

    private static void AppendIsDefined(StringBuilder code, string parameterType, string parameterName, string? cases)
    {
        code.Append(MemberIndent)
            .Append("public static bool IsDefined(")
            .Append(parameterType)
            .Append(' ')
            .Append(parameterName)
            .Append(@")
        {
            switch(")
            .Append(parameterName)
            .Append(@")
            {")
            .Append(Reindent(cases))
            .Append(MethodCaseIndent)
            .Append(@"    return true;")
            .Append(MethodCaseIndent)
            .Append(@"default:")
            .Append(MethodCaseIndent)
            .Append(@"    return false;
            }
        }
");
    }

    private readonly void AppendTryGetValue(StringBuilder code, string? cases)
    {
        code.Append(MemberIndent)
            .Append("public static bool TryGetValue(string name, out ")
            .Append(NotNullFullName)
            .Append(@" result)
        {
            switch(name)
            {")
            .Append(Reindent(cases))
            .Append(MethodCaseIndent)
            .Append(@"default: result = default; return false;
            }
        }
");
    }

    /// <summary>
    /// Case bodies are built at the depth of a property accessor; switches that sit directly in a
    /// method body are one level shallower.
    /// </summary>
    private static string? Reindent(string? cases) => cases?.Replace(CaseIndent, MethodCaseIndent);
}
