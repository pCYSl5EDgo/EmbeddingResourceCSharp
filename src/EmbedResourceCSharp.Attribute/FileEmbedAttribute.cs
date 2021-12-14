using System;

namespace EmbedResourceCSharp;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FileEmbedAttribute : Attribute
{
    public string Path { get; }

    public FileEmbedAttribute(string path)
    {
        Path = path;
    }
}
