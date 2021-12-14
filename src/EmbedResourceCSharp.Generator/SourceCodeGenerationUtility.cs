using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmbedResourceCSharp.Generator;

internal static class SourceCodeGenerationUtility
{
    public static void ProcessFolder(StringBuilder buffer, string rootFolderPath, in FolderExtraction folder, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folderPath = Path.Combine(rootFolderPath, folder.Path);
        Header(buffer, folder.Method);
        buffer.Append("(global::System.ReadOnlySpan<char> ");
        var parameter = folder.Method.Parameters[0].Name;
        buffer.Append(parameter);
        buffer.Append(')');
        buffer.AppendLine();
        buffer.Append("        {");
        if (Directory.Exists(folderPath))
        {
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
        }
        else
        {
            buffer.Append("            throw new global::System.IO.DirectoryNotFoundException(@\"");
            buffer.Append(folder.Path.Replace("\"", "\"\""));
            buffer.AppendLine("\");");
        }

        buffer.Append("        }");
        Footer(buffer);
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

    public static void ProcessFile(StringBuilder buffer, string rootFolderPath, in FileExtraction file, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = Path.Combine(rootFolderPath, file.Path);
        Header(buffer, file.Method);
        buffer.Append("()");
        buffer.AppendLine();
        buffer.Append("        {");
        buffer.AppendLine();
        if (File.Exists(filePath))
        {
            buffer.Append("            return ");
            EmbedArray(buffer, File.ReadAllBytes(filePath), cancellationToken);
        }
        else
        {
            buffer.Append("            throw new global::System.IO.FileNotFoundException(@\"");
            buffer.Append(file.Path.Replace("\"", "\"\""));
            buffer.AppendLine("\");");
        }

        buffer.Append("        }");
        Footer(buffer);
    }

    private static void Footer(StringBuilder buffer)
    {
        buffer.AppendLine();
        buffer.Append("    }");
        buffer.AppendLine();
        buffer.Append('}');
        buffer.AppendLine();
        buffer.AppendLine();
    }

    private static void Header(StringBuilder buffer, IMethodSymbol method)
    {
        buffer.Append("namespace ");
        buffer.Append(method.ContainingNamespace.ToDisplayString());
        buffer.AppendLine();
        buffer.Append('{');
        buffer.AppendLine();
        buffer.Append("    ");
        var type = method.ContainingType;
        PrintAccessibility(buffer, type.DeclaredAccessibility);
        if (type.IsStatic)
        {
            buffer.Append(" static");
        }

        buffer.Append(type.IsReferenceType ? " partial class " : " partial struct ");
        buffer.Append(type.Name);
        buffer.AppendLine();
        buffer.Append("    {");
        buffer.AppendLine();
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

    public readonly struct FileExtraction
    {
        public readonly IMethodSymbol Method;
        public readonly string Path;

        public FileExtraction(IMethodSymbol method, string path)
        {
            Method = method;
            Path = path;
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

    public readonly struct TypeExtraction
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
    }

    public static void Extract(IEnumerable<MethodDeclarationSyntax> candidates, Compilation compilation, out List<FileExtraction> files, out List<FolderExtraction> folders)
    {
        var types = new TypeExtraction(compilation);

        files = new List<FileExtraction>();
        folders = new List<FolderExtraction>();

        foreach (var candidate in candidates)
        {
            ExtractEach(compilation, candidate, in types, files, folders);
        }
    }

    private static void ExtractEach(Compilation compilation, SyntaxNode candidate, in TypeExtraction types, List<FileExtraction> files, List<FolderExtraction> folders)
    {
        var model = compilation.GetSemanticModel(candidate.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(candidate);
        if (symbol is not IMethodSymbol method
            || method.Parameters.Length > 1
            || !SymbolEqualityComparer.Default.Equals(method.ReturnType, types.ReadOnlySpanByteTypeSymbol))
        {
            return;
        }

        foreach (var attributeData in method.GetAttributes())
        {
            var attributeClass = attributeData.AttributeClass;
            if (attributeClass is null)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(attributeClass, types.FileEmbedAttributeTypeSymbol))
            {
                if (ExtractFile(method, attributeData, out var value))
                {
                    files.Add(value);
                }

                break;
            }

            if (SymbolEqualityComparer.Default.Equals(attributeClass, types.FolderEmbedAttributeTypeSymbol))
            {
                if (ExtractFolder(method, attributeData, out var value))
                {
                    folders.Add(value);
                }

                break;
            }
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

    public static bool ExtractFile(IMethodSymbol method, AttributeData attributeData, out FileExtraction extraction)
    {
        if (method.Parameters.Length != 0
            || attributeData.ConstructorArguments[0].Value is not string path)
        {
            Unsafe.SkipInit(out extraction);
            return false;
        }

        extraction = new(method, path);
        return true;
    }

    public static void ProcessFolderDesignTimeBuild(ref StringBuilder buffer, IMethodSymbol method)
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

    public static void ProcessFileDesignTimeBuild(ref StringBuilder buffer, IMethodSymbol method)
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
