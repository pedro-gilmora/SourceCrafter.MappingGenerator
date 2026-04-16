using System;

namespace SourceCrafter.Mappify;

public enum ApplyTo { None, Source, Target, Both }

internal enum CollectionKind { Queue, Stack, Enumerable, ReadOnlyCollection, ReadOnlySpan, Collection, Array, Span, Dictionary }

[Flags] public enum MappingKind { Normal = 1, Fill = 2, All = Normal | Fill }
