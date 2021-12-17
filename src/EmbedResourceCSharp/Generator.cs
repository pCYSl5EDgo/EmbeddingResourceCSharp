using System;
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
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName("EmbedResourceCSharp.FileEmbedAttribute") ?? throw new NullReferenceException("FileEmbedAttribute not found"))
            .WithComparer(SymbolEqualityComparer.Default);
        var folder = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName("EmbedResourceCSharp.FolderEmbedAttribute") ?? throw new NullReferenceException("FolderEmbedAttribute not found"))
            .WithComparer(SymbolEqualityComparer.Default);
        var files = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Combine(file)
            .Select(PostTransform)
            .Where(x => x.Method is not null);
        var folders = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Combine(folder)
            .Select(PostTransform)
            .Where(x => x.Method is not null);

        context.RegisterSourceOutput(files.Combine(options), GenerateFileEmbed!);
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

    private (IMethodSymbol? Method, AttributeData? Data) PostTransform((IMethodSymbol? Method, INamedTypeSymbol Type) pair, CancellationToken token)
    {
        var method = pair.Method;
        if (method is null)
        {
            return default;
        }

        foreach (var attribute in method.GetAttributes())
        {
            token.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, pair.Type))
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

    private void GenerateFileEmbed(SourceProductionContext context, ((IMethodSymbol Method, AttributeData Data) Left, Options Options) pair)
    {
        StringBuilder builder;

        var token = context.CancellationToken;
        token.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            builder = new StringBuilder();
            Utility.ProcessFileDesignTimeBuild(builder, method);
            goto SUCCESS;
        }

        var attribute = pair.Left.Data;
        if (!Utility.ExtractFile(method, attribute, out var extract))
        {
            return;
        }

        builder = new StringBuilder();
        var exists = Utility.ProcessFile(builder, pair.Options.ProjectDirectory, extract, token);
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
