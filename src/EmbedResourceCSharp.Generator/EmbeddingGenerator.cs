using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmbedResourceCSharp
{
    [Generator]
    // ReSharper disable once UnusedMember.Global
    public class EmbeddingGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            //System.Diagnostics.Debugger.Launch();
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver)
                || !context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.ProjectDir", out var rootFolderPath))
            {
                return;
            }

            var compilation = context.Compilation;
            Extract(receiver.Candidates, compilation, out var files, out var folders);

            var buffer = new StringBuilder();

            ProcessFiles(ref buffer, rootFolderPath, files);
            ProcessFolders(ref buffer, rootFolderPath, folders);

            context.AddSource("Resources.cs", buffer.ToString());
        }

        private static void ProcessFolders(ref StringBuilder buffer, string rootFolderPath, List<(IMethodSymbol, string, string, SearchOption, PathSeparator)> folders)
        {
            foreach (var (method, path, filter, option, separator) in folders)
            {
                var folderPath = Path.Combine(rootFolderPath, path);
                if (!Directory.Exists(folderPath))
                {
                    continue;
                }

                ProcessFolder(buffer, folderPath, filter, option, method, separator);
            }
        }

        private static void ProcessFolder(StringBuilder buffer, string folderPath, string filter, SearchOption option, IMethodSymbol method, PathSeparator separator)
        {
            var enumerateFileGroups = Directory.EnumerateFiles(folderPath, filter, option).GroupBy(s => s.Length);
            Header(buffer, method);
            buffer.Append("(global::System.ReadOnlySpan<char> ");
            var parameter = method.Parameters[0].Name;
            buffer.Append(parameter);
            buffer.Append(')');
            buffer.AppendLine();
            buffer.Append("        {");
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
                ProcessFolderEachLength(buffer, folderPath, separator, @group, parameter);
            }

            buffer.Append("            }");
            buffer.AppendLine();
            buffer.Append("        }");
            Footer(buffer);
        }

        private static void ProcessFolderEachLength(StringBuilder buffer, string folderPath, PathSeparator separator, IGrouping<int, string> @group, string parameter)
        {
            buffer.Append("                case ");
            buffer.Append(@group.Key - folderPath.Length);
            buffer.Append(':');
            buffer.AppendLine();

            foreach (var s in @group)
            {
                buffer.Append("                    if (global::System.MemoryExtensions.SequenceEqual(");
                buffer.Append(parameter);
                buffer.Append(", \"");
                for (var i = folderPath.Length; i < s.Length; ++i)
                {
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

                var content = File.ReadAllBytes(s);
                EmbedArray(buffer, content);
                buffer.Append("                    }");
                buffer.AppendLine();
                buffer.AppendLine();
            }

            buffer.Append("                    goto default;");
            buffer.AppendLine();
        }

        private static void EmbedArray(StringBuilder buffer, byte[] content)
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
                    buffer.Append(", ");
                    buffer.Append(content[i]);
                }

                buffer.Append(" };");
            }

            buffer.AppendLine();
        }

        private static void ProcessFiles(ref StringBuilder buffer, string rootFolderPath, List<(IMethodSymbol, string)> files)
        {
            foreach (var (method, path) in files)
            {
                var file = Path.Combine(rootFolderPath, path);
                if (!File.Exists(file))
                {
                    continue;
                }

                ProcessFile(ref buffer, method, File.ReadAllBytes(file));
            }
        }

        private static void ProcessFile(ref StringBuilder buffer, IMethodSymbol method, byte[] content)
        {
            Header(buffer, method);
            buffer.Append("()");
            buffer.AppendLine();
            buffer.Append("        {");
            buffer.AppendLine();
            buffer.Append("            return ");
            EmbedArray(buffer, content);
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

        private static void Extract(List<MethodDeclarationSyntax> candidates, Compilation compilation, out List<(IMethodSymbol, string)> files, out List<(IMethodSymbol, string, string, SearchOption, PathSeparator)> folders)
        {
            var file = compilation.GetTypeByMetadataName("EmbedResourceCSharp.FileEmbedAttribute") ?? throw new NullReferenceException("FileEmbedAttribute not found");
            var folder = compilation.GetTypeByMetadataName("EmbedResourceCSharp.FolderEmbedAttribute") ?? throw new NullReferenceException("FolderEmbedAttribute not found");
            var readOnlySpan = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1") ?? throw new NullReferenceException("ReadOnlySpan`1 not found");
            var @byte = compilation.GetTypeByMetadataName("System.Byte") ?? throw new NullReferenceException("byte not found");
            var @char = compilation.GetTypeByMetadataName("System.Char") ?? throw new NullReferenceException("byte not found");
            var readOnlySpanByte = readOnlySpan.Construct(@byte);
            var readOnlySpanChar = readOnlySpan.Construct(@char);

            files = new List<(IMethodSymbol, string)>(candidates.Count);
            folders = new List<(IMethodSymbol, string, string, SearchOption, PathSeparator)>(candidates.Count);

            foreach (var candidate in candidates)
            {
                ExtractEach(compilation, candidate, file, folder, readOnlySpanByte, readOnlySpanChar, files, folders);
            }
        }

        private static void ExtractEach(Compilation compilation, SyntaxNode candidate, ISymbol file, ISymbol folder, ISymbol readOnlySpanByte, INamedTypeSymbol readOnlySpanChar, List<(IMethodSymbol, string)> files, List<(IMethodSymbol, string, string, SearchOption, PathSeparator)> folders)
        {
            var model = compilation.GetSemanticModel(candidate.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(candidate);
            if (!(symbol is IMethodSymbol method)
                || method.Parameters.Length > 1
                || !SymbolEqualityComparer.Default.Equals(method.ReturnType, readOnlySpanByte))
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

                if (SymbolEqualityComparer.Default.Equals(attributeClass, file))
                {
                    ExtractFile(method, attributeData, files);
                    break;
                }

                if (SymbolEqualityComparer.Default.Equals(attributeClass, folder))
                {
                    ExtractFolder(method, attributeData, folders, readOnlySpanChar);
                    break;
                }
            }
        }

        private static void ExtractFolder(IMethodSymbol method, AttributeData attributeData, List<(IMethodSymbol, string, string, SearchOption, PathSeparator)> folders, INamedTypeSymbol readOnlySpanChar)
        {
            if (method.Parameters.Length != 1
                || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, readOnlySpanChar)
                || !(attributeData.ConstructorArguments[0].Value is string path)
                || !(attributeData.ConstructorArguments[1].Value is string filter)
                || !(attributeData.ConstructorArguments[2].Value is int option)
                || !(attributeData.ConstructorArguments[3].Value is int separator))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            if (path[path.Length - 1] != '/' && path[path.Length - 1] != '\\')
            {
                path += '/';
            }
            
            folders.Add((method, path, filter, (SearchOption)option, (PathSeparator)separator));
        }

        private static void ExtractFile(IMethodSymbol method, AttributeData attributeData, List<(IMethodSymbol, string)> files)
        {
            if (method.Parameters.Length != 0
                || !(attributeData.ConstructorArguments[0].Value is string path))
            {
                return;
            }

            files.Add((method, path));
        }
    }
}
