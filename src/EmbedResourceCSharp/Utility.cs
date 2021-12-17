using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EmbedResourceCSharp;

internal static partial class Utility
{
    public const string AttributeCs = @"namespace EmbedResourceCSharp
{
    internal enum PathSeparator
    {
        AsIs,
        Slash,
        BackSlash,
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class FileEmbedAttribute : global::System.Attribute
    {
        public string Path { get; }

        public FileEmbedAttribute(string path)
        {
            Path = path;
        }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class FolderEmbedAttribute : global::System.Attribute
    {
        public string Path { get; private set; }
        public string Filter { get; private set; }
        public global::System.IO.SearchOption Option { get; private set; }
        public PathSeparator Separator { get; private set; }

        public FolderEmbedAttribute(string path, string filter = ""*"", global::System.IO.SearchOption option = global::System.IO.SearchOption.AllDirectories, PathSeparator separator = PathSeparator.Slash)
        {
            Path = path;
            Filter = filter;
            Option = option;
            Separator = separator;
        }
    }
}
";

    public static Options SelectOptions(AnalyzerConfigOptionsProvider provider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var isDesignTimeBuild = provider.GlobalOptions.TryGetValue("build_property.DesignTimeBuild", out var designTimeBuild) && designTimeBuild == "true";
        if (!provider.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDirectory) || projectDirectory is null)
        {
            isDesignTimeBuild = true;
            projectDirectory = "";
        }

        return new(isDesignTimeBuild, projectDirectory);
    }

    public static string CalcHintName(StringBuilder builder, IMethodSymbol method, string suffix)
    {
        return builder.Clear()
            .Append(method.ContainingType.Name)
            .Replace('<', '_').Replace('>', '_')
            .Append("____")
            .Append(method.Name)
            .Append(suffix)
            .ToString();
    }

    public static bool ProcessFolder(StringBuilder buffer, string rootFolderPath, in FolderExtraction folder, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folderPath = Path.Combine(rootFolderPath, folder.Path);
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        Header(buffer, folder.Method);
        buffer.Append("(global::System.ReadOnlySpan<char> ");
        var parameter = folder.Method.Parameters[0].Name;
        buffer.Append(parameter);
        buffer.Append(')');
        buffer.AppendLine();
        buffer.Append("        {");
        var enumerateFileGroups = Directory.EnumerateFiles(folderPath, folder.Filter, folder.Option).GroupBy(s => s.Length);
        cancellationToken.ThrowIfCancellationRequested();

        buffer.AppendLine();
        buffer.Append("            switch (");
        buffer.Append(parameter);
        buffer.Append(".Length)");
        buffer.AppendLine();
        buffer.Append("            {");
        buffer.AppendLine();
        buffer.Append("                default:");
        buffer.AppendLine();
        buffer.Append("                    throw new global::System.IO.FileNotFoundException(new string(");
        buffer.Append(parameter);
        buffer.Append("));");
        buffer.AppendLine();

        foreach (var @group in enumerateFileGroups)
        {
            ProcessFolderEachLength(buffer, folderPath, folder.Separator, @group, parameter, cancellationToken);
        }

        buffer.AppendLine("            }");
        buffer.Append("        }");
        Footer(buffer);
        return true;
    }

    private static void ProcessFolderEachLength(StringBuilder buffer, string folderPath, PathSeparator separator, IGrouping<int, string> @group, string parameter, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        buffer.Append("                case ");
        buffer.Append(@group.Key - folderPath.Length);
        buffer.Append(':');
        buffer.AppendLine();

        foreach (var s in @group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Append("                    if (global::System.MemoryExtensions.SequenceEqual(");
            buffer.Append(parameter);
            buffer.Append(", \"");
            for (var i = folderPath.Length; i < s.Length; ++i)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var c = s[i];
                switch (separator)
                {
                    case PathSeparator.AsIs:
                        if (c == '\\')
                        {
                            buffer.Append('\\');
                            buffer.Append('\\');
                        }
                        else
                        {
                            buffer.Append(c);
                        }

                        break;
                    case PathSeparator.Slash:
                        buffer.Append(c == '\\' ? '/' : c);
                        break;
                    case PathSeparator.BackSlash:
                        if (c == '/' || c == '\\')
                        {
                            buffer.Append('\\');
                            buffer.Append('\\');
                        }
                        else
                        {
                            buffer.Append(c);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            buffer.Append("\"))");
            buffer.AppendLine();
            buffer.Append("                    {");
            buffer.AppendLine();
            buffer.Append("                        return ");

            cancellationToken.ThrowIfCancellationRequested();
            var content = File.ReadAllBytes(s);
            EmbedArray(buffer, content, cancellationToken);
            buffer.Append("                    }");
            buffer.AppendLine();
            buffer.AppendLine();
        }

        buffer.Append("                    goto default;");
        buffer.AppendLine();
    }

    private static void EmbedArray(StringBuilder buffer, byte[] content, System.Threading.CancellationToken cancellationToken)
    {
        if (content.Length == 0)
        {
            buffer.Append("default;");
        }
        else
        {
            buffer.Append("new byte[] { ");
            buffer.Append(content[0]);
            for (var i = 1; i < content.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Append(", ");
                buffer.Append(content[i]);
            }

            buffer.Append(" };");
        }

        buffer.AppendLine();
    }

    public static void ProcessFile(StringBuilder buffer, IMethodSymbol method, string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Header(buffer, method);
        buffer.Append("()").AppendLine();
        buffer.Append("        {").AppendLine();
        buffer.Append("            return ");
        EmbedArray(buffer, File.ReadAllBytes(filePath), cancellationToken);
        buffer.Append("        }");
        Footer(buffer);
    }

    private static void Footer(StringBuilder buffer)
    {
        buffer.AppendLine().Append("    }").AppendLine().Append('}').AppendLine().AppendLine();
    }

    private static void Header(StringBuilder buffer, IMethodSymbol method)
    {
        buffer.Append("namespace ");
        buffer.Append(method.ContainingNamespace.ToDisplayString()).AppendLine();
        buffer.Append('{').AppendLine();
        buffer.Append("    ");
        var type = method.ContainingType;
        PrintAccessibility(buffer, type.DeclaredAccessibility);
        if (type.IsStatic)
        {
            buffer.Append(" static");
        }

        if (type.IsRecord)
        {
            buffer.Append(type.IsReferenceType ? " partial record " : " partial record struct ");
        }
        else
        {
            buffer.Append(type.IsReferenceType ? " partial class " : " partial struct ");
        }

        buffer.Append(type.Name).AppendLine();
        buffer.Append("    {").AppendLine();
        buffer.Append("        ");
        PrintAccessibility(buffer, method.DeclaredAccessibility);
        buffer.Append(" static partial global::System.ReadOnlySpan<byte> ");
        buffer.Append(method.Name);
    }

    private static void PrintAccessibility(StringBuilder buffer, Accessibility accessibility)
    {
        switch (accessibility)
        {
            case Accessibility.NotApplicable:
                break;
            case Accessibility.Private:
                buffer.Append("private");
                break;
            case Accessibility.ProtectedAndInternal:
                buffer.Append("private protected");
                break;
            case Accessibility.Protected:
                buffer.Append("protected");
                break;
            case Accessibility.Internal:
                buffer.Append("internal");
                break;
            case Accessibility.ProtectedOrInternal:
                buffer.Append("protected internal");
                break;
            case Accessibility.Public:
                buffer.Append("public");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public readonly struct FolderExtraction
    {
        public readonly IMethodSymbol Method;
        public readonly string Path;
        public readonly string Filter;
        public readonly SearchOption Option;
        public readonly PathSeparator Separator;

        public FolderExtraction(IMethodSymbol method, string path, string filter, SearchOption option, PathSeparator separator)
        {
            Method = method;
            Path = path;
            Filter = filter;
            Option = option;
            Separator = separator;
        }
    }

    public static bool ExtractFolder(IMethodSymbol method, AttributeData attributeData, out FolderExtraction extraction)
    {
        if (method.Parameters.Length != 1
            || attributeData.ConstructorArguments[0].Value is not string path
            || attributeData.ConstructorArguments[1].Value is not string filter
            || attributeData.ConstructorArguments[2].Value is not int option
            || attributeData.ConstructorArguments[3].Value is not int separator)
        {
            Unsafe.SkipInit(out extraction);
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        if (path[path.Length - 1] != '/' && path[path.Length - 1] != '\\')
        {
            path += '/';
        }

        extraction = new(method, path, filter, (SearchOption)option, (PathSeparator)separator);
        return true;
    }

    public static void ProcessFolderDesignTimeBuild(StringBuilder buffer, IMethodSymbol method)
    {
        Header(buffer, method);
        buffer.Append("(global::System.ReadOnlySpan<char> ");
        var parameter = method.Parameters[0].Name;
        buffer.Append(parameter);
        buffer.Append(')');
        buffer.AppendLine();
        buffer.Append("        {");
        buffer.AppendLine();
        buffer.Append("            throw new global::System.NotImplementedException(); // DesignTime ");
        buffer.AppendLine();
        buffer.Append("        }");
        Footer(buffer);
    }

    public static void ProcessFileDesignTimeBuild(StringBuilder buffer, IMethodSymbol method)
    {
        Header(buffer, method);
        buffer.Append("()");
        buffer.AppendLine();
        buffer.Append("        {");
        buffer.AppendLine();
        buffer.Append("            throw new global::System.NotImplementedException(); // DesignTime ");
        buffer.AppendLine();
        buffer.Append("        }");
        Footer(buffer);
    }
}
