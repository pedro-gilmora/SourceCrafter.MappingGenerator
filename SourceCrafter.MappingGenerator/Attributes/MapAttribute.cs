﻿using SourceCrafter.Mapping.Constants;
using System;
using System.Collections.Generic;
using System.Text;

#pragma warning disable IDE0060 // Quitar el parámetro no utilizado
namespace SourceCrafter.Mapping.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct| AttributeTargets.Interface, AllowMultiple = true)]
public class MapAttribute<T> : Attribute
{
    public MapAttribute(
        string leftMapper = "", 
        string rightMapper = "", 
        Ignore ignore = Ignore.None) { }
}
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MapAttribute<TSource, TTarget> : Attribute
{
    public MapAttribute(
        string[] extraMappersNameOfExprs = null!, 
        string leftMapper = "", string rightMapper = "", 
        Ignore ignore = Ignore.None) { }
}


[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class MapAttribute : Attribute
{
    public MapAttribute(
        string mapper, 
        Ignore ignore = Ignore.None) { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class IgnoreAttribute : Attribute
{
    public IgnoreAttribute(Ignore twoWay = Ignore.Both) { }
}
#pragma warning restore IDE0060 // Quitar el parámetro no utilizado
