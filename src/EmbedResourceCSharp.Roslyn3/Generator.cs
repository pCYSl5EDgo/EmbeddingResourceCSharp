using System.Text;
using Microsoft.CodeAnalysis;

namespace EmbedResourceCSharp;

[Generator]
public sealed class Generator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        var builder = new StringBuilder();
        var token = context.CancellationToken;
        var receiver = (context.SyntaxReceiver as Receiver)!;
        var options = Utility.SelectOptions(context.AnalyzerConfigOptions, token);
        var compilation = context.Compilation;
        var types = new TypeExtraction(compilation);
        var comparer = SymbolEqualityComparer.Default;

        {
            var file = types.FileEmbedAttributeTypeSymbol;
            foreach (var candidate in receiver.FileCandidates)
            {
                var method = compilation.GetSemanticModel(candidate.SyntaxTree).GetDeclaredSymbol(candidate, token) as IMethodSymbol;
                if (method is null)
                {
                    continue;
                }

                AttributeData attribute;
                var attributes = method.GetAttributes();
                for (var i = 0; i < attributes.Length; i++)
                {
                    attribute = attributes[i];
                    token.ThrowIfCancellationRequested();
                    if (comparer.Equals(file, attribute.AttributeClass))
                    {
                        goto FOUND;
                    }
                }

                continue;
FOUND:
                if (options.IsDesignTimeBuild)
                {
                    Utility.ProcessFileDesignTimeBuild(builder.Clear(), method);
                    goto SUCCESS;
                }

                if (!Utility.ExtractFile(method, attribute, out var extract))
                {
                    return;
                }

                var exists = Utility.ProcessFile(builder.Clear(), options.ProjectDirectory, extract, token);
                if (!exists)
                {
                    var location = Location.None;
                    if (method.AssociatedSymbol is { Locations: { Length: > 0 } locations })
                    {
                        location = locations[0];
                    }

                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticsHelper.FileNotFoundError, location, extract.Path));
                    return;
                }

SUCCESS:
                var source = builder.ToString();
                var hintName = Utility.CalcHintName(builder, method, ".file.g.cs");
                context.AddSource(hintName, source);
            }
        }

        {
            var folder = types.FileEmbedAttributeTypeSymbol;
            foreach (var candidate in receiver.FolderCandidates)
            {
                var method = compilation.GetSemanticModel(candidate.SyntaxTree).GetDeclaredSymbol(candidate, token) as IMethodSymbol;
                if (method is null)
                {
                    continue;
                }

                AttributeData attribute;
                var attributes = method.GetAttributes();
                for (var i = 0; i < attributes.Length; i++)
                {
                    attribute = attributes[i];
                    token.ThrowIfCancellationRequested();
                    if (comparer.Equals(folder, attribute.AttributeClass))
                    {
                        goto FOUND;
                    }
                }

                continue;
FOUND:
                if (options.IsDesignTimeBuild)
                {
                    Utility.ProcessFolderDesignTimeBuild(builder.Clear(), method);
                    goto SUCCESS;
                }

                if (!Utility.ExtractFolder(method, attribute, out var extract))
                {
                    return;
                }

                var exists = Utility.ProcessFolder(builder.Clear(), options.ProjectDirectory, extract, token);
                if (!exists)
                {
                    var location = Location.None;
                    if (method.AssociatedSymbol is { Locations: { Length: > 0 } locations })
                    {
                        location = locations[0];
                    }

                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticsHelper.FolderNotFoundError, location, extract.Path));
                    return;
                }

SUCCESS:
                var source = builder.ToString();
                var hintName = Utility.CalcHintName(builder, method, ".file.g.cs");
                context.AddSource(hintName, source);
            }
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForPostInitialization(GenerateInitialCode);
        context.RegisterForSyntaxNotifications(() => new Receiver());
    }

    private static void GenerateInitialCode(GeneratorPostInitializationContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.AddSource("Attribute.cs", Utility.AttributeCs);
    }
}
