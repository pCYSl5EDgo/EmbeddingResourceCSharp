using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace EmbedResourceCSharp;

public sealed class TypeExtraction : IEquatable<TypeExtraction>
{
    public TypeExtraction(Compilation compilation)
    {
        Compilation = compilation;
        FileEmbedAttributeTypeSymbol = compilation.GetTypeByMetadataName("EmbedResourceCSharp.FileEmbedAttribute") ?? throw new NullReferenceException("FileEmbedAttribute not found");
        FolderEmbedAttributeTypeSymbol = compilation.GetTypeByMetadataName("EmbedResourceCSharp.FolderEmbedAttribute") ?? throw new NullReferenceException("FolderEmbedAttribute not found");
        var readOnlySpan = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1") ?? throw new NullReferenceException("ReadOnlySpan`1 not found");
        var @byte = compilation.GetTypeByMetadataName("System.Byte") ?? throw new NullReferenceException("byte not found");
        var @char = compilation.GetTypeByMetadataName("System.Char") ?? throw new NullReferenceException("byte not found");
        ReadOnlySpanByteTypeSymbol = readOnlySpan.Construct(@byte);
        ReadOnlySpanCharTypeSymbol = readOnlySpan.Construct(@char);
    }

    public readonly Compilation Compilation;

    public readonly INamedTypeSymbol FileEmbedAttributeTypeSymbol;

    public readonly INamedTypeSymbol FolderEmbedAttributeTypeSymbol;

    public readonly INamedTypeSymbol ReadOnlySpanByteTypeSymbol;

    public readonly INamedTypeSymbol ReadOnlySpanCharTypeSymbol;

    public bool Equals(TypeExtraction other)
    {
        var comparer = SymbolEqualityComparer.Default;
        return comparer.Equals(FileEmbedAttributeTypeSymbol, other.FileEmbedAttributeTypeSymbol)
            && comparer.Equals(FolderEmbedAttributeTypeSymbol, other.FolderEmbedAttributeTypeSymbol)
            && comparer.Equals(ReadOnlySpanByteTypeSymbol, other.ReadOnlySpanByteTypeSymbol)
            && comparer.Equals(ReadOnlySpanCharTypeSymbol, other.ReadOnlySpanCharTypeSymbol);
    }

    public sealed class Comparer : IEqualityComparer<TypeExtraction>
    {
        public static readonly Comparer Instance = new();

        public bool Equals(TypeExtraction x, TypeExtraction y) => x.Equals(y);

        public int GetHashCode(TypeExtraction obj) => default;
    }
}
