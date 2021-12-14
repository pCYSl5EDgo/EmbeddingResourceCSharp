namespace EmbedResourceCSharp.Generator;

internal readonly struct Options
{
    public readonly bool IsDesignTimeBuild;
    public readonly string ProjectDirectory;

    public Options(bool isDesignTimeBuild, string projectDirectory)
    {
        IsDesignTimeBuild = isDesignTimeBuild;
        ProjectDirectory = projectDirectory;
    }
}
