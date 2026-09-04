using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Mapifier.Constants;
using SourceCrafter.Mapifier.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace SourceCrafter.Mapifier;

/// <summary>
/// Turns Roslyn symbols into the pure-data mapping model. This is the only place in the generator
/// that touches a <see cref="Compilation"/> or an <see cref="ISymbol"/>; everything downstream
/// works from the model alone.
/// <para>
/// A parser is created and discarded within a single generation pass, so nothing it reads can
/// outlive the pass or be captured by the incremental pipeline's caches.
/// </para>
/// </summary>
internal sealed class MappingParser
{
    private readonly Compilation compilation;
    private readonly CancellationToken cancelToken;
    private readonly bool canUseUnsafeAccessor;

    private readonly Dictionary<string, TypeMappingMeta> _mappingSet = [];
    private readonly Dictionary<string, TypeMeta> _typesSet = [];

    // Symbols live only for the duration of the discovery phase. Keying by TypeMeta.Id is exact:
    // _typesSet is itself keyed by Id, so there is precisely one TypeMeta (and one symbol) per Id.
    private readonly Dictionary<string, ITypeSymbol> _typeSymbols = [];

    private int targetScopeId = 0, sourceScopeId = 0;

    private const string
        TupleStart = "(",
        TupleEnd = " )",
        TypeStart = @"new {0}
        {{",
        TypeEnd = @"
        }";

    internal MappingParser(Compilation compilation, CancellationToken cancelToken)
    {
        this.compilation = compilation;
        this.cancelToken = cancelToken;
        canUseUnsafeAccessor =
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;
    }

    bool TryGetMapping(ITypeSymbol source, ITypeSymbol target, MappingKind mapKind, IgnoreBind ignore, out TypeMappingMeta mapping)
    {
        TypeMeta targetType = GetOrAddType(target), sourceType = GetOrAddType(source);

        mapping = GetOrAddMapping(targetType, sourceType, mapKind, ignore);

        //mapping = BuildMap(targetType, sourceType);

        if (mapping.CanMap is false)
        {
            mapping = null!;
            return false;
        }

        if (mapping.AreSameType)
        {
            return true;
        }

        //BuildMap(targetType, targetType);
        //BuildMap(sourceType, sourceType);

        return true;

        //TypeMappingMeta BuildMap(TypeMeta targetTypeData, TypeMeta sourceTypeData)
        //{
        //    MemberMeta
        //        targetMember = new(++targetScopeId, "to", targetTypeData.Symbol.IsNullable()) { Symbol = targetTypeData },
        //        sourceMember = new(--sourceScopeId, "source", sourceTypeData.Symbol.IsNullable()) { Symbol = sourceTypeData };

        //    var mapping = GetOrAddMapping(targetTypeData, sourceTypeData, mapKind);

        //    return DiscoverTypeMappings(
        //        mapping,
        //        sourceMember,
        //        targetMember,
        //        ignore);
        //}

        TypeMappingMeta GetOrAddMapping(TypeMeta target, TypeMeta source, MappingKind mapKind, IgnoreBind ignore)
        {
            var hashCode = GetId(target.Id, source.Id);

            if (_mappingSet.TryGetValue(hashCode, out var item)) return item!;

            // Reserve the slot before recursing so a cyclic mapping resolves to the in-flight
            // instance instead of looping forever.
            _mappingSet[hashCode] = null!;

            MemberMeta
                targetMember = new(++targetScopeId, "to", target.IsNullable) { Type = target },
                sourceMember = new(--sourceScopeId, "source", source.IsNullable) { Type = source };

            _mappingSet[hashCode] = item = new(_mappingSet, hashCode, target, source, GetOrAddMapping) { MappingsKind = mapKind };

            return DiscoverTypeMappings(
                item,
                sourceMember,
                targetMember,
                ignore);
        }

        TypeMappingMeta DiscoverTypeMappings
        (
            TypeMappingMeta map,
            MemberMeta source,
            MemberMeta target,
            IgnoreBind ignore,
            string sourceMappingPath = "+",
            string targetMappingPath = "+"
        )
        {
            map.EnsureDirection(ref target, ref source);

            GetNullability(target, map.TargetType.AllowsNull, source, map.SourceType.AllowsNull);

            if (map.CanMap is not null)
            {
                if (map.TargetType is { IsRecursive: false, IsStruct: false, IsPrimitive: false })
                    map.TargetType.IsRecursive = IsRecursive(targetMappingPath + map.TargetType.Id + "+", map.TargetType.Id);

                if (map.SourceType is { IsRecursive: false, IsStruct: false, IsPrimitive: false })
                    map.SourceType.IsRecursive = (map.AreSameType && map.TargetType.IsRecursive) || IsRecursive(sourceMappingPath + map.SourceType.Id + "+", map.SourceType.Id);

                return map;
            }

            if (map.IsCollection)
            {
                MemberMeta
                    targetItem = new(target.Id, target.Name + "Item", target.Type.CollectionInfo.IsItemNullable),
                    sourceItem = new(source.Id, source.Name + "Item", source.Type.CollectionInfo.IsItemNullable);

                if (map.ItemMap.CanMap is false || !(source.Type.CollectionInfo.ItemDataType.HasZeroArgsCtor && target.Type.CollectionInfo.ItemDataType.HasZeroArgsCtor))
                {
                    map.CanMap = map.IsCollection = false;

                    return map;
                }

                if (targetItem.IsNullable)
                    map.ItemMap.AddSourceTryGet = true;

                if (sourceItem.IsNullable)
                    map.ItemMap.AddTargetTryGet = true;

                var itemMap = DiscoverTypeMappings(
                    map.ItemMap,
                    sourceItem,
                    targetItem,
                    IgnoreBind.None,
                    sourceMappingPath,
                    targetMappingPath);

                if (map.CanMap is null && true ==
                    (map.CanMap =
                        ignore is not (IgnoreBind.Target or IgnoreBind.Both) &&
                        (!itemMap.TargetType.IsInterface && (map.TargetRequiresMapper = CreateCollectionMapBuilders(
                            source,
                            target,
                            sourceItem,
                            targetItem,
                            map.TargetRequiresMapper,
                            source.Type.CollectionInfo,
                            target.Type.CollectionInfo,
                            map.TargetCollectionMap,
                            itemMap.BuildTargetValue,
                            itemMap.TargetKeyValueMap,
                            ref map.BuildTargetValue,
                            ref map.BuildTargetMethod)))
                        |
                        (ignore is not (IgnoreBind.Source or IgnoreBind.Both) &&
                        !itemMap.SourceType.IsInterface && (map.SourceRequiresMapper = CreateCollectionMapBuilders(
                            target,
                            source,
                            targetItem,
                            sourceItem,
                            map.SourceRequiresMapper,
                            target.Type.CollectionInfo,
                            source.Type.CollectionInfo,
                            map.SourceCollectionMap,
                            itemMap.BuildSourceValue,
                            itemMap.SourceKeyValueMap,
                            ref map.BuildSourceValue,
                            ref map.BuildSourceMethod))))
                )
                {
                    map.ExtraMappings.Add(new NestedMappingMethodsRenderer(itemMap));
                }
                else
                {
                    map.CanMap = false;
                }

                return map;
            }

            var canMap = false;

            if (HasConversionTo(map.SourceType, map.TargetType, out var targetScalarConversion, out var sourceScalarConversion))
            {
                if (ignore is IgnoreBind.Both or not IgnoreBind.Target && targetScalarConversion.exists)
                {
                    var scalar = targetScalarConversion.isExplicit ? $"({map.TargetType.FullName}){{0}}" : "{0}";

                    map.BuildTargetValue = new ScalarConversionRenderer(scalar);

                    canMap = map.TargetHasScalarConversion = true;
                }

                if (ignore is IgnoreBind.Both or not IgnoreBind.Source && sourceScalarConversion.exists)
                {
                    var scalar = sourceScalarConversion.isExplicit
                        ? $@"({map.SourceType.FullName}){{0}}"
                        : "{0}";

                    map.BuildSourceValue = new ScalarConversionRenderer(scalar);

                    canMap = map.SourceHasScalarConversion = true;
                }
            }

            if (map.TargetType.IsPrimitive || map.SourceType.IsPrimitive)
            {
                map.CanMap = canMap;
                return map;
            }

            switch (map)
            {
                case { TargetType: { DictionaryOwned: true, IsKeyValueType: true } } or { SourceType: { DictionaryOwned: true, IsKeyValueType: true } }:
                    CreateKeyValueMapBuilder(map);
                    return map;
                default:
                    CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.Members);
                    return map;
            }

            void CreateKeyValueMapBuilder(TypeMappingMeta map)
            {
                MemberMeta sourceKeyMember, sourceValueMember, targetKeyMember, targetValueMember;

                switch ((
                    map.SourceType is { IsTupleType: true, Members.Length: 2 },
                    map.TargetType is { IsTupleType: true, Members.Length: 2 }
                ))
                {
                    case (false, false):

                        GetKeyValueProps(map.SourceType.Members, out sourceKeyMember, out sourceValueMember);
                        GetKeyValueProps(map.TargetType.Members, out targetKeyMember, out targetValueMember);

                        break;

                    case (false, true):

                        GetKeyValueProps(map.SourceType.Members, out sourceKeyMember, out sourceValueMember);

                        (targetKeyMember, targetValueMember) = (map.TargetType.Members[0], map.TargetType.Members[1]);

                        if (targetValueMember.Name.Contains("key", StringComparison.OrdinalIgnoreCase))
                            (targetValueMember, targetKeyMember) = (targetKeyMember, targetValueMember);

                        break;

                    case (true, false):

                        GetKeyValueProps(map.TargetType.Members, out sourceKeyMember, out sourceValueMember);

                        (targetKeyMember, targetValueMember) = (map.SourceType.Members[0], map.SourceType.Members[1]);

                        if (targetValueMember.Name.Contains("key", StringComparison.OrdinalIgnoreCase))
                            (targetValueMember, targetKeyMember) = (targetKeyMember, targetValueMember);

                        break;

                    default:

                        sourceKeyMember = sourceValueMember = targetKeyMember = targetValueMember = null!;
                        map.CanMap = false;

                        return;
                }

                TypeMeta
                    sourceKeyType = sourceKeyMember.Type,
                    sourceValueType = sourceValueMember.Type,
                    targetKeyType = targetKeyMember.Type,
                    targetValueType = targetValueMember.Type;

                TypeMappingMeta
                    keyMapping = GetOrAddMapping(sourceKeyType, targetKeyType, mapKind, ignore),
                    valueMapping = GetOrAddMapping(sourceValueType, targetValueType, mapKind, ignore);

                keyMapping = DiscoverTypeMappings(keyMapping, sourceValueMember, sourceKeyMember, IgnoreBind.None, sourceMappingPath, targetMappingPath);
                valueMapping = DiscoverTypeMappings(valueMapping, targetValueMember, targetKeyMember, IgnoreBind.None, sourceMappingPath, targetMappingPath);

                if (keyMapping.CanMap is false && valueMapping.CanMap is false) map.CanMap = false;

                var (checkKeyPropNull, checkValuePropNull, checkKeyFieldNull, checkValueFieldNull) =
                (
                    !(sourceKeyMember.IsNullable && sourceKeyMember.Type.IsStruct || !sourceValueMember.IsNullable),
                    !(targetKeyMember.IsNullable && targetKeyMember.Type.IsStruct || !targetValueMember.IsNullable),
                    !(sourceValueMember.IsNullable && sourceValueMember.Type.IsStruct || !sourceKeyMember.IsNullable),
                    !(targetValueMember.IsNullable && targetValueMember.Type.IsStruct || !targetKeyMember.IsNullable)
                );

                var (keyPropName, valuePropName, targetKeyFieldName, targetValueFieldName) =
                (
                     sourceKeyMember.Name,
                     targetKeyMember.Name,
                     sourceValueMember.Name,
                     targetValueMember.Name
                );

                KeyValueMappings
                    stt = map.TargetKeyValueMap = new(
                        new KeyValueMemberRenderer(
                            keyMapping,
                            true,
                            targetKeyMember.Name,
                            checkKeyFieldNull,
                            targetKeyMember.Type.IsValueType,
                            sourceKeyMember.Bang,
                            sourceKeyMember.DefaultBang),
                        new KeyValueMemberRenderer(
                            valueMapping,
                            true,
                            targetValueMember.Name,
                            checkValueFieldNull,
                            targetValueMember.Type.IsValueType,
                            targetKeyMember.Bang,
                            targetKeyMember.DefaultBang)),
                    tts = map.SourceKeyValueMap = new(
                        new KeyValueMemberRenderer(
                            keyMapping,
                            false,
                            sourceKeyMember.Name,
                            checkKeyPropNull,
                            sourceKeyMember.Type.IsValueType,
                            sourceValueMember.Bang,
                            sourceValueMember.DefaultBang),
                        new KeyValueMemberRenderer(
                            valueMapping,
                            false,
                            sourceValueMember.Name,
                            checkValuePropNull,
                            sourceValueMember.Type.IsValueType,
                            targetValueMember.Bang,
                            targetValueMember.DefaultBang));

                var buildTargetValue = map.BuildTargetValue = new KeyValueObjectRenderer(map.TargetType.NotNullFullName, stt);

                map.BuildTargetMethod = new KeyValueMappingMethodRenderer(
                    map.TargetType.NotNullFullName,
                    map.ToTargetMethodName,
                    map.SourceType.NotNullFullName,
                    buildTargetValue);

                var buildSourceValue = map.BuildSourceValue = new KeyValueTupleRenderer(tts);

                map.BuildSourceMethod = new KeyValueMappingMethodRenderer(
                    map.SourceType.NotNullFullName,
                    map.ToSourceMethodName,
                    map.TargetType.NotNullFullName,
                    buildSourceValue);

                map.CanMap = true;

            }

            static bool IsRecursive(string s, string id)
            {
                string n = $"+{id}+", ss;
                var t = 1;

                for (int nL = n.Length, start = Math.Abs(s.Length - n.Length), end = s.Length; start > -1 && end - start >= nL;)
                {
                    if ((ss = s[start..end]) == n && t-- == 0)
                    {
                        return true;
                    }
                    else if (ss[0] == '+' && ss[^1] == '+')
                    {
                        end = start + 1;
                        start = end - nL;
                    }
                    else
                    {
                        end--;
                        start--;
                    }
                }
                return false;
            }

            bool CreateCollectionMapBuilders
            (
                MemberMeta source,
                MemberMeta target,
                MemberMeta sourceItem,
                MemberMeta targetItem,
                bool call,
                CollectionInfo sourceCollInfo,
                CollectionInfo targetCollInfo,
                CollectionMapping collMapInfo,
                ValueRenderer? itemValueCreator,
                KeyValueMappings keyValueMapping,
                ref ValueRenderer? valueCreator,
                ref MappingMethodsRenderer? methodCreator
            )
            {
                valueCreator = new CollectionCopyValueRenderer(collMapInfo.MethodName, targetCollInfo.ItemDataType, target);

                methodCreator = new CollectionMappingMethodRenderer(
                    source,
                    target,
                    sourceItem,
                    targetItem,
                    call,
                    sourceCollInfo,
                    targetCollInfo,
                    collMapInfo,
                    itemValueCreator,
                    keyValueMapping);
                return true;
            }
        }

        void CreateTypeMapBuilders
        (
            TypeMappingMeta map,
            IgnoreBind ignore,
            string sourceMappingPath,
            string targetMappingPath,
            MemberMeta source,
            MemberMeta target,
            ImmutableArray<MemberMeta> sourceMembers,
            ImmutableArray<MemberMeta> targetMembers
        )
        {
            if (map is { IsCollection: not true, TargetType.IsInterface: true, SourceType.IsInterface: true })
                return;

            string
                sourceExportFullTypeName = map.SourceType.ExportNotNullFullName,
                targetExportFullTypeName = map.TargetType.ExportNotNullFullName,
                ttsTypeStart = map.SourceType.IsTupleType ? TupleStart : string.Format(TypeStart, map.SourceType.NotNullFullName),
                ttsTypeEnd = map.SourceType.IsTupleType ? TupleEnd : TypeEnd,
                sttTypeStart = map.TargetType.IsTupleType ? TupleStart : string.Format(TypeStart, map.TargetType.NotNullFullName),
                sttTypeEnd = map.TargetType.IsTupleType ? TupleEnd : TypeEnd,
                ttsSpacing = map.SourceType.IsTupleType ? " " : @"
                ",
                sttSpacing = map.TargetType.IsTupleType ? " " : @"
                ";

            MemberRendererList
                sourceMemberMappers = new(),
                targetMemberMappers = new();

            CommaState
                sttComma = new(),
                ttsComma = new();

            var mapId = map.MappingId;

            MutableFlag
                isTargetRecursive = new(),
                isSourceRecursive = new();

            bool toSameType = map.AreSameType,
                hasComplexTtsMembers = false,
                hasComplexSttMembers = false,
                parentIgnoreTarget = ignore is IgnoreBind.Target or IgnoreBind.Both,
                parentIgnoreSource = ignore is IgnoreBind.Source or IgnoreBind.Both;

            map.BuildTargetValue ??= map.TargetHasScalarConversion || !source.Type.HasMembers
                ? LiteralValueRenderer.Instance
                : new MapCallValueRenderer(map.ToTargetMethodName, isTargetRecursive, source);

            map.BuildTargetMethod = new ObjectMappingMethodsRenderer(
                map,
                towardsTarget: true,
                destination: target,
                origin: source,
                isTargetRecursive,
                sttTypeStart,
                sttTypeEnd,
                targetExportFullTypeName,
                sourceExportFullTypeName,
                targetMemberMappers);

            map.BuildSourceValue ??= map.TargetHasScalarConversion || !target.Type.HasMembers
                ? LiteralValueRenderer.Instance
                : new MapCallValueRenderer(map.ToSourceMethodName, isSourceRecursive, target);

            map.BuildSourceMethod = new ObjectMappingMethodsRenderer(
                map,
                towardsTarget: false,
                destination: source,
                origin: target,
                isSourceRecursive,
                ttsTypeStart,
                ttsTypeEnd,
                sourceExportFullTypeName,
                targetExportFullTypeName,
                sourceMemberMappers);

            var allowLowerCase = map.TargetType.IsTupleType || map.SourceType.IsTupleType;

            foreach (var targetMember in targetMembers)
            {
                foreach (var sourceMember in sourceMembers)
                {
                    if (DiscardMapping(allowLowerCase, sourceMember, targetMember, out var ignoreSource, out var ignoreTarget))
                    {
                        if ((targetMember.Type.Id, targetMember.Name) == (sourceMember.Type.Id, sourceMember.Name)) break;

                        continue;
                    }
                    else if (mapId == GetId(targetMember.Type.ExportFullName, sourceMember.Type.ExportFullName))
                    {
                        targetMember.Type = targetMember.OwningType = map.TargetType;
                        sourceMember.Type = sourceMember.OwningType = map.SourceType;

                        if (!(parentIgnoreTarget || ignoreTarget || map.TargetType.IsInterface))
                        {
                            map.TargetMemberCount++;

                            if (targetMember.IsNullable)
                                map.AddTargetTryGet = true;

                            map.TargetType.IsRecursive = true;
                            isTargetRecursive.Set();

                            hasComplexSttMembers = true;

                            targetMemberMappers.Add(new MappedMemberRenderer(
                                map,
                                true,
                                sourceMember,
                                targetMember,
                                sttComma,
                                sttSpacing,
                                map,
                                canUseUnsafeAccessor));
                        }

                        if (!(toSameType && parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                        {
                            map.SourceMemberCount++;

                            map.SourceType.IsRecursive = true;
                            isSourceRecursive.Set();

                            hasComplexTtsMembers = true;

                            if (sourceMember.IsNullable)
                                map.AddSourceTryGet = true;

                            sourceMemberMappers.Add(new MappedMemberRenderer(
                                map,
                                false,
                                targetMember,
                                sourceMember,
                                ttsComma,
                                ttsSpacing,
                                map,
                                canUseUnsafeAccessor));
                        }

                        map.CanMap |= map.HasTargetToSourceMap || map.HasTargetToSourceMap;

                        break;
                    }
                    else if (DiscoverTypeMappings(
                            GetOrAddMapping(targetMember.Type, sourceMember.Type, mapKind, ignore),
                            sourceMember,
                            targetMember,
                            IgnoreBind.None,
                            sourceMappingPath,
                            targetMappingPath) is { CanMap: not false } memberMap)
                    {
                        targetMember.OwningType = map.TargetType;
                        sourceMember.OwningType = map.SourceType;

                        if (!(parentIgnoreTarget || ignoreTarget || map.TargetType.IsInterface))
                        {
                            map.TargetMemberCount++;

                            if (sourceMember.IsNullable)
                                memberMap.AddTargetTryGet = true;

                            hasComplexSttMembers = !memberMap.TargetType.IsPrimitive;

                            memberMap.TargetType.IsRecursive |=
                                memberMap.IsCollection is true && memberMap.TargetType.CollectionInfo.ItemDataType.Id == target.Type.Id;

                            isTargetRecursive.Or(memberMap.TargetType.IsRecursive);

                            map.TargetType.IsRecursive |= memberMap.TargetType.IsRecursive;

                            targetMemberMappers.Add(new MappedMemberRenderer(
                                memberMap,
                                true,
                                sourceMember,
                                targetMember,
                                sttComma,
                                sttSpacing,
                                map,
                                canUseUnsafeAccessor));
                        }

                        if (!(toSameType || parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                        {
                            map.SourceMemberCount++;

                            if (targetMember.IsNullable)
                                memberMap.AddSourceTryGet = true;

                            hasComplexTtsMembers = !memberMap.SourceType.IsPrimitive;

                            memberMap.SourceType.IsRecursive |=
                                memberMap.IsCollection is true && memberMap.SourceType.CollectionInfo.ItemDataType.Id == source.Type.Id;

                            isSourceRecursive.Or(memberMap.SourceType.IsRecursive);

                            map.SourceType.IsRecursive |= memberMap.SourceType.IsRecursive;

                            sourceMemberMappers.Add(new MappedMemberRenderer(
                                memberMap,
                                false,
                                targetMember,
                                sourceMember,
                                ttsComma,
                                ttsSpacing,
                                map,
                                canUseUnsafeAccessor));
                        }

                        if (map.MappingsKind == MappingKind.All && memberMap.MappingsKind != MappingKind.All
                            && (sourceMember.IsInit || targetMember.IsInit))
                        {
                            memberMap.MappingsKind = MappingKind.All;
                        }

                        if (true == (map.CanMap |= map.HasTargetToSourceMap || map.HasSourceToTargetMap)
                            && (!memberMap.TargetType.IsPrimitive || memberMap.TargetType.IsTupleType
                            || !memberMap.SourceType.IsPrimitive || memberMap.SourceType.IsTupleType))
                        {
                            map.ExtraMappings.Add(new NestedMappingMethodsRenderer(memberMap));
                        }

                        break;
                    }
                }
            }

            map.TargetRequiresMapper = map.TargetMemberCount > 0;
            map.SourceRequiresMapper = map.SourceMemberCount > 0;

            if (!map.HasSourceToTargetMap)
            {
                map.AddTargetTryGet = false;
            }

            if (!map.HasTargetToSourceMap)
            {
                map.AddSourceTryGet = false;
            }

            return;

        }
    }

    TypeMeta GetOrAddType(ITypeSymbol typeSymbol, bool dictionaryOwned = false)
    {
        var (membersSource, implementation) = typeSymbol is INamedTypeSymbol { } namedSymbol && namedSymbol.FullGlobalQualifiedNonGenericName == "global::SourceCrafter.Mapifier.IImplement"
            ? (namedSymbol.TypeArguments[0], namedSymbol.TypeArguments[1])
            : (typeSymbol, null);

        var hashCode = GetTypeId(implementation ?? membersSource);

        if (_typesSet.TryGetValue(hashCode, out var item)) return item!;

        // Reserve the slot before the constructor walks members: a self-referencing type resolves
        // to the in-flight instance, which TypeMeta publishes as soon as it is safe to hand out.
        _typesSet[hashCode] = null!;

        return new(_typesSet, compilation, membersSource, implementation, hashCode, dictionaryOwned, GetOrAddType, _typeSymbols);
    }

    // Relocated off TypeMeta so the metadata graph holds no symbols: scalar conversion is a
    // discovery-phase question and needs the Compilation, which must not outlive discovery.
    bool HasConversionTo(TypeMeta source, TypeMeta target, out ScalarConversion toTarget, out ScalarConversion toSource)
    {
        var hasConversionResult = HasConversion(source, target, out toTarget);

        if (target.Id == source.Id)
        {
            toSource = toTarget;
            return hasConversionResult;
        }

        return hasConversionResult | HasConversion(target, source, out toSource);
    }

    bool HasConversion(TypeMeta source, TypeMeta target, out ScalarConversion info)
    {
        if ((source, target) is not (
            ({ IsTupleType: false }, { IsTupleType: false }) and
            ({ DictionaryOwned: false, IsKeyValueType: false }, { DictionaryOwned: false, IsKeyValueType: false })))
        {
            info = default;
            return false;
        }

        ITypeSymbol
            targetTypeSymbol = _typeSymbols[target.Id].AsNonNullable(),
            sourceTypeSymbol = _typeSymbols[source.Id];

        var conversion = compilation.ClassifyConversion(sourceTypeSymbol, targetTypeSymbol);

        info = (conversion.Exists && (source.IsValueType || source.FullName == "string" || source.IsObject), conversion.IsExplicit);

        if (!info.exists)
        {
            if (!info.isExplicit)
            {
                info.exists = info.isExplicit = sourceTypeSymbol
                    .GetMembers()
                    .Any(m => m is IMethodSymbol
                    {
                        MethodKind: MethodKind.Conversion,
                        Parameters: [{ Type: { } firstParam }],
                        ReturnType: { } returnType
                    }
                              && SymbolEqualityComparer.Default.Equals(returnType, sourceTypeSymbol)
                              && SymbolEqualityComparer.Default.Equals(firstParam, targetTypeSymbol)
                    );
            }
            else if (source.IsInterface && sourceTypeSymbol.AllInterfaces.FirstOrDefault(target.Equals) is { } impl)
            {
                info.exists = !(info.isExplicit = false);
            }
        }

        info.exists |= info.isExplicit |= source.IsObject && !target.IsObject;

        return info.exists;
    }



    bool DiscardMapping(bool ignoreCase, MemberMeta source, MemberMeta target, out bool ignoreSource, out bool ignoreTarget)
    {
        ignoreSource = ignoreTarget = false;

        var casing = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return !(AreSimilarMembers(source, target, casing)
            | CheckMappability(casing, target, source, ref ignoreSource, ref ignoreTarget)
            | CheckMappability(casing, source, target, ref ignoreTarget, ref ignoreSource));
    }

    bool CheckMappability(StringComparison casing, MemberMeta target, MemberMeta source, ref bool ignoreTarget, ref bool ignoreSource)
    {
        if (target.IsReadOnly || source.IsWriteOnly)
            return false;

        var canWrite = false;

        if (target.Attributes.IsDefaultOrEmpty) return canWrite;

        foreach (var attr in target.Attributes)
        {
            var className = attr.ClassName;

            if (className == "global::SourceCrafter.Mapifier.Attributes.IgnoreBindAttribute")
            {
                switch (attr.FirstArgValue is int ignoreBindValue ? (IgnoreBind)ignoreBindValue : IgnoreBind.Both)
                {
                    case IgnoreBind.Target:
                        ignoreTarget = true;
                        return false;
                    case IgnoreBind.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case IgnoreBind.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;

                continue;
            }

            if (className == "global::SourceCrafter.Mapifier.Attributes.MaxRecursionAttribute")
            {
                if (attr.FirstArgValue is short maxDepthValue)
                    target.MaxDepth = maxDepthValue;
                continue;
            }

            if (className != "global::SourceCrafter.Mapifier.Attributes.BindAttribute")
                continue;

            if (attr.BindMemberName is { } bindMemberName
                 && (attr.BindMemberId == source.Id
                     || AreSimilarNames(source.Type.Name, bindMemberName, target.Type.Name, target.Name, casing)))
            {
                canWrite = true;

                switch (attr.SecondArgValue is int ignoreValue ? (IgnoreBind)ignoreValue : IgnoreBind.None)
                {
                    case IgnoreBind.Target:
                        ignoreTarget = true;
                        return false;
                    case IgnoreBind.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case IgnoreBind.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;
            }
        }
        return canWrite;
    }


    static bool AreSimilarNames(string sourceTypeName, string sourceMemberName, string targetTypeName, string targetMemberName, StringComparison casing)
    {
        return targetMemberName.Equals(sourceMemberName, casing)
                || targetMemberName.Equals(targetTypeName + sourceMemberName, casing)
                || (sourceTypeName + targetMemberName).Equals(sourceMemberName, casing);
    }

    static bool AreSimilarMembers(MemberMeta source, MemberMeta target, StringComparison casing)
    {
        return target.Name.Equals(source.Name, casing)
                || target.Name.Equals(target.Type.Name + source.Name, casing)
                || (source.Type.Name + target.Name).Equals(source.Name, casing);
    }

    static string GetTypeId(ITypeSymbol type) =>
        (type.Name == "Nullable" ? ((INamedTypeSymbol)type).TypeArguments[0] : type.AsNonNullable()).GlobalNamespaced;

    static string GetId(string targetId, string sourceId) =>
        StringComparer.Ordinal.Compare(targetId, sourceId) switch
        {
            < 0 => $"{targetId}|{sourceId}",
            > 0 => $"{sourceId}|{targetId}",
            _ => targetId
        };

    static void GetKeyValueProps(ImmutableArray<MemberMeta> members, out MemberMeta keyProp, out MemberMeta valueProp)
    {
        keyProp = null!;
        valueProp = null!;

        foreach (var member in members)
        {
            if (!member.IsProperty) continue;

            switch (member.Name)
            {
                case "Key":
                    keyProp = member;

                    if (valueProp != null) return;

                    continue;
                case "Value":
                    valueProp = member;

                    if (keyProp != null) return;

                    continue;
                default:
                    continue;
            }
        }
    }

    static void GetNullability(MemberMeta target, bool targetTypeAllowsNull, MemberMeta source, bool sourceTypeAllowsNull)
    {
        source.DefaultBang = GetDefaultBangChar(target.IsNullable, source.IsNullable, sourceTypeAllowsNull);
        source.Bang = GetBangChar(target.IsNullable, source.IsNullable);
        target.DefaultBang = GetDefaultBangChar(source.IsNullable, target.IsNullable, targetTypeAllowsNull);
        target.Bang = GetBangChar(source.IsNullable, target.IsNullable);
    }

    static string? GetDefaultBangChar(bool isTargetNullable, bool isSourceNullable, bool sourceAllowsNull)
        => !isTargetNullable && (sourceAllowsNull || isSourceNullable) ? "!" : null;

    static string? GetBangChar(bool isTargetNullable, bool isSourceNullable)
        => !isTargetNullable && isSourceNullable ? "!" : null;

    #region Entry points

    /// <summary>
    /// Resolves an enum reference collected by the pipeline and projects it into pure data.
    /// </summary>
    internal bool TryParseEnum(TypeRef enumRef, out EnumMeta meta)
    {
        if (enumRef.Resolve(compilation) is not { TypeKind: TypeKind.Enum } enumType)
        {
            meta = default;

            return false;
        }

        meta = ProjectEnum(enumType);

        return true;
    }

    /// <summary>
    /// Resolves both ends of a bind request and builds the mapping model for them.
    /// </summary>
    internal bool TryParseMapping(TypeRef from, TypeRef to, MappingKind mapKind, IgnoreBind ignore, out TypeMappingMeta mapping)
    {
        if (from.Resolve(compilation) is { } source && to.Resolve(compilation) is { } target)
            return TryGetMapping(source, target, mapKind, ignore, out mapping);

        mapping = null!;

        return false;
    }

    /// <summary>
    /// Builds the mapping that copies a type onto itself. Mapping A to B also makes A to A and
    /// B to B reachable, because nested members of the same type copy through them.
    /// </summary>
    internal bool TryParseSelfMapping(TypeRef type, MappingKind mapKind, IgnoreBind ignore, out TypeMappingMeta mapping)
        => TryParseMapping(type, type, mapKind, ignore, out mapping);

    /// <summary>
    /// Projects an enum into pure data while its symbol is still available, so nothing
    /// Roslyn-shaped reaches the rendering phase.
    /// </summary>
    private static EnumMeta ProjectEnum(ITypeSymbol enumType)
    {
        var fullName = enumType.GlobalNamespaced;

        var members = ImmutableArray.CreateBuilder<EnumFieldMeta>();

        foreach (var member in enumType.GetMembers())
        {
            if (member is not IFieldSymbol { IsConst: true } field) continue;

            members.Add(new EnumFieldMeta(
                fullName + "." + field.Name,
                Quote(GetAttributeArgument(field, "global::System.ComponentModel.DescriptionAttribute") ?? field.Name.Wordify()),
                Quote(GetAttributeArgument(field, "global::System.ComponentModel.CategoryAttribute") ?? ""),
                Convert.ToString(field.ConstantValue) ?? ""));
        }

        return new EnumMeta(
            fullName,
            fullName,
            members.ToImmutable(),
            TypeMeta.SanitizeTypeName(enumType),
            GetContainerNames(enumType));

        static string Quote(string value) => "\"" + value + "\"";

        static ImmutableArray<string> GetContainerNames(ITypeSymbol type)
        {
            var names = ImmutableArray.CreateBuilder<string>();

            for (var parent = type.ContainingType ?? (ISymbol?)type.ContainingAssembly;
                parent is not null;
                parent = parent.ContainingType ?? (ISymbol?)parent.ContainingAssembly)
            {
                names.Add(parent.Name);
            }

            return names.ToImmutable();
        }

        static string? GetAttributeArgument(ISymbol member, string attributeFullName)
        {
            foreach (var attr in member.GetAttributes())
                if (attr.AttributeClass?.GlobalNamespaced == attributeFullName
                    && attr.ConstructorArguments is [{ Kind: not TypedConstantKind.Array, Value: { } value }, ..])
                    return value.ToString();

            return null;
        }
    }

    #endregion
}
