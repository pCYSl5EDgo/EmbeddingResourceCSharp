namespace EmbedResourceCSharp;

public sealed partial class Options
{
    public bool IsDesignTimeBuild => DesignTimeBuild == "true";
}
