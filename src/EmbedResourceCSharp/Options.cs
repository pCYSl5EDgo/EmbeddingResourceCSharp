using System;
using System.Collections.Generic;

namespace EmbedResourceCSharp;

internal sealed class Options : IEquatable<Options>
{
    public readonly bool IsDesignTimeBuild;
    public readonly string ProjectDirectory;

    public Options(bool isDesignTimeBuild, string projectDirectory)
    {
        IsDesignTimeBuild = isDesignTimeBuild;
        ProjectDirectory = projectDirectory;
    }

    public bool Equals(Options other)
    {
        return other == this || (other.IsDesignTimeBuild == IsDesignTimeBuild && other.ProjectDirectory == ProjectDirectory);
    }

    public override int GetHashCode()
    {
        var hashCode = ProjectDirectory.GetHashCode();
        return IsDesignTimeBuild ? hashCode : -hashCode;
    }

    public sealed class Comparer : IEqualityComparer<Options>
    {
        public static readonly Comparer Instance = new();

        public bool Equals(Options x, Options y) => x.Equals(y);

        public int GetHashCode(Options obj) => obj.GetHashCode();
    }
}
