using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmbedResourceCSharp;

[Generator(LanguageNames.CSharp)]
public sealed class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(GenerateInitialCode);

        var options = context.AnalyzerConfigOptionsProvider
            .Select(Utility.SelectOptions)
            .WithComparer(Options.Comparer.Instance);
        var file = context.CompilationProvider
            .Select(static (compilation, token) =>
            {
                token.ThrowIfCancellationRequested();
                return compilation.GetTypeByMetadataName("EmbedResourceCSharp.FileEmbedAttribute");
            })
            .WithComparer(SymbolEqualityComparer.Default);
        var folder = context.CompilationProvider
            .Select(static (compilation, token) =>
            {
                token.ThrowIfCancellationRequested();
                return compilation.GetTypeByMetadataName("EmbedResourceCSharp.FolderEmbedAttribute");
            })
            .WithComparer(SymbolEqualityComparer.Default);
        var files = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Combine(file)
            .Select(PostTransformFile)
            .Where(x => x.Method is not null && x.Path is not null)!
            .WithComparer(FileAttributeComparer.Instance);
        var folders = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Combine(folder)
            .Select(PostTransform)
            .Where(x => x.Method is not null);

        context.RegisterSourceOutput(files.Combine(options), GenerateFileEmbed);
        context.RegisterSourceOutput(folders.Combine(options), GenerateFolderEmbed!);
    }

    private void GenerateInitialCode(IncrementalGeneratorPostInitializationContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.AddSource("Attribute.cs", Utility.AttributeCs);
    }

    private bool Predicate(SyntaxNode node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private IMethodSymbol? Transform(GeneratorSyntaxContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var syntax = (context.Node as MethodDeclarationSyntax)!;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax, token);
        return symbol;
    }

    private (IMethodSymbol? Method, string? Path) PostTransformFile((IMethodSymbol? Method, INamedTypeSymbol? Type) pair, CancellationToken token)
    {
        var type = pair.Type;
        if (type is null)
        {
            return default;
        }

        var method = pair.Method;
        if (method is null)
        {
            return default;
        }

        foreach (var attribute in method.GetAttributes())
        {
            token.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, type))
            {
                return (method, attribute.ConstructorArguments[0].Value as string);
            }
        }

        return default;
    }

    private (IMethodSymbol? Method, AttributeData? Data) PostTransform((IMethodSymbol? Method, INamedTypeSymbol? Type) pair, CancellationToken token)
    {
        var type = pair.Type;
        if (type is null)
        {
            return default;
        }

        var method = pair.Method;
        if (method is null)
        {
            return default;
        }

        foreach (var attribute in method.GetAttributes())
        {
            token.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, type))
            {
                return (method, attribute);
            }
        }

        return default;
    }

    private void GenerateFolderEmbed(SourceProductionContext context, ((IMethodSymbol Method, AttributeData Data) Left, Options Options) pair)
    {
        StringBuilder builder;

        var token = context.CancellationToken;
        token.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            builder = new StringBuilder();
            Utility.ProcessFolderDesignTimeBuild(builder, method);
            goto SUCCESS;
        }

        if (!Utility.ExtractFolder(method, pair.Left.Data, out var extract))
        {
            return;
        }

        builder = new StringBuilder();
        var exists = Utility.ProcessFolder(builder, pair.Options.ProjectDirectory, extract, token);
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
        var hintName = Utility.CalcHintName(builder, method, ".folder.g.cs");
        context.AddSource(hintName, source);
    }

    private void GenerateFileEmbed(SourceProductionContext context, ((IMethodSymbol Method, string Path) Left, Options Options) pair)
    {
        StringBuilder builder;

        var token = context.CancellationToken;
        token.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        var path = pair.Left.Path;

        var filePath = Path.Combine(pair.Options.ProjectDirectory, path);
        if (!File.Exists(filePath))
        {
            var location = Location.None;
            if (method.AssociatedSymbol is { Locations: { Length: > 0 } locations })
            {
                location = locations[0];
            }

            context.ReportDiagnostic(Diagnostic.Create(DiagnosticsHelper.FileNotFoundError, location, filePath));
            return;
        }

        builder = new StringBuilder();
        if (pair.Options.IsDesignTimeBuild)
        {
            Utility.ProcessFileDesignTimeBuild(builder, method);
        }
        else
        {
            Utility.ProcessFile(builder, method, filePath, token);
        }

        var source = builder.ToString();
        var hintName = Utility.CalcHintName(builder, method, ".file.g.cs");
        context.AddSource(hintName, source);
    }

    private sealed class FileAttributeComparer : IEqualityComparer<ValueTuple<IMethodSymbol, string>>
    {
        public static readonly FileAttributeComparer Instance = new();

        public bool Equals((IMethodSymbol, string) x, (IMethodSymbol, string) y) => x.Item2.Equals(y.Item2) && SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1);

        public int GetHashCode((IMethodSymbol, string) obj) => SymbolEqualityComparer.Default.GetHashCode(obj.Item1);
    }
}
