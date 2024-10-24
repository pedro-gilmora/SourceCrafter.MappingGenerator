﻿using System;

namespace SourceCrafter.Bindings.Constants;

public enum ApplyOn { None, Source, Target, Both }

internal enum EnumerableType { Queue, Stack, Enumerable, ReadOnlyCollection, ReadOnlySpan, Collection, Array, Span, Dictionary }

[Flags] public enum MappingKind { All, Normal, Fill }
