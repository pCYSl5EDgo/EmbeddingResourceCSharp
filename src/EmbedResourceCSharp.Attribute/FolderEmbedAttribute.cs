using System;
using System.IO;

namespace EmbedResourceCSharp
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FolderEmbedAttribute : Attribute
    {
        public string Path { get; private set; }
        public string Filter { get; private set; }
        public SearchOption Option { get; private set; }
        public PathSeparator Separator { get; private set; }

        public FolderEmbedAttribute(string path, string filter = "*", SearchOption option = SearchOption.AllDirectories, PathSeparator separator = PathSeparator.Slash)
        {
            Path = path;
            Filter = filter;
            Option = option;
            Separator = separator;
        }
    }
}
