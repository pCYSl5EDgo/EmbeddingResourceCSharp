using System;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EmbedResourceCSharp.Generator;

[Generator]
public sealed class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider
            .Select(SelectOptions);
        var types = context.CompilationProvider
            .Select(SelectExtractionTypes);
        var files = context.SyntaxProvider
            .CreateSyntaxProvider(PredicateFile, Transform)
            .Combine(types)
            .Select(PostTransformFile)
            .Where(x => x.Method is not null);
        var folders = context.SyntaxProvider
            .CreateSyntaxProvider(PredicateFolder, Transform)
            .Combine(types)
            .Select(PostTransformFolder)
            .Where(x => x.Method is not null);

        context.RegisterSourceOutput(files.Combine(options), GenerateFileEmbed!);
        context.RegisterSourceOutput(folders.Combine(options), GenerateFolderEmbed!);
    }

    private (IMethodSymbol? Method, AttributeData? Data) PostTransformFolder((IMethodSymbol? Method, SourceCodeGenerationUtility.TypeExtraction Types) pair, CancellationToken token)
    {
        var method = pair.Method;
        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ReturnType, pair.Types.ReadOnlySpanByteTypeSymbol) || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, pair.Types.ReadOnlySpanCharTypeSymbol))
        {
            return default;
        }

        var folder = pair.Types.FolderEmbedAttributeTypeSymbol;
        foreach (var attribute in method.GetAttributes())
        {
            token.ThrowIfCancellationRequested();
            if (folder.Equals(attribute.AttributeClass, SymbolEqualityComparer.Default))
            {
                return (method, attribute);
            }
        }

        return default;
    }

    private (IMethodSymbol? Method, AttributeData? Data) PostTransformFile((IMethodSymbol? Method, SourceCodeGenerationUtility.TypeExtraction Types) pair, CancellationToken token)
    {
        var method = pair.Method;
        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ReturnType, pair.Types.ReadOnlySpanByteTypeSymbol))
        {
            return default;
        }

        var file = pair.Types.FileEmbedAttributeTypeSymbol;
        foreach (var attribute in method.GetAttributes())
        {
            token.ThrowIfCancellationRequested();
            if (file.Equals(attribute.AttributeClass, SymbolEqualityComparer.Default))
            {
                return (method, attribute);
            }
        }

        return default;
    }

    private SourceCodeGenerationUtility.TypeExtraction SelectExtractionTypes(Compilation source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(source);
    }

    private void GenerateFolderEmbed(SourceProductionContext context, ((IMethodSymbol Method, AttributeData Data) Left, Options Options) pair)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            var builder = new StringBuilder();
            SourceCodeGenerationUtility.ProcessFolderDesignTimeBuild(ref builder, method);
            context.AddSource(method.Name + ".folder.g.cs", builder.ToString());
            return;
        }

        if (SourceCodeGenerationUtility.ExtractFolder(method, pair.Left.Data, out var extract))
        {
            var builder = new StringBuilder();
            try
            {
                SourceCodeGenerationUtility.ProcessFolder(builder, pair.Options.ProjectDirectory, extract, context.CancellationToken);
            }
            catch (Exception e)
            {
                builder.Clear().Append(e.ToString());
            }
            finally
            {
                var source = builder.ToString();
                context.AddSource(method.Name + ".folder.g.cs", source);
            }
        }
    }

    private void GenerateFileEmbed(SourceProductionContext context, ((IMethodSymbol Method, AttributeData Data) Left, Options Options) pair)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            var builder = new StringBuilder();
            SourceCodeGenerationUtility.ProcessFileDesignTimeBuild(ref builder, method);
            context.AddSource(method.Name + ".file.g.cs", builder.ToString());
            return;
        }

        if (SourceCodeGenerationUtility.ExtractFile(method, pair.Left.Data, out var extract))
        {
            var builder = new StringBuilder();
            try
            {
                SourceCodeGenerationUtility.ProcessFile(builder, pair.Options.ProjectDirectory, extract, context.CancellationToken);
            }
            catch (Exception e)
            {
                builder.Clear().Append(e.ToString());
            }
            finally
            {
                var source = builder.ToString();
                context.AddSource(method.Name + ".file.g.cs", source);
            }
        }
    }

    private IMethodSymbol? Transform(GeneratorSyntaxContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var syntax = (context.Node as MethodDeclarationSyntax)!;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax, token);
        return symbol;
    }

    private static bool Predicate(SyntaxNode node, CancellationToken token, out MethodDeclarationSyntax methodDeclarationSyntax)
    {
        token.ThrowIfCancellationRequested();
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0, TypeParameterList: null } declarationSyntax)
        {
            methodDeclarationSyntax = default!;
            return false;
        }

        methodDeclarationSyntax = declarationSyntax;
        var @static = false;
        var @partial = false;
        foreach (var declarationSyntaxModifier in declarationSyntax.Modifiers)
        {
            token.ThrowIfCancellationRequested();
            if (declarationSyntaxModifier.IsKind(SyntaxKind.StaticKeyword))
            {
                @static = true;
            }
            else if (declarationSyntaxModifier.IsKind(SyntaxKind.PartialKeyword))
            {
                @partial = true;
            }
        }

        return @static && @partial;
    }

    private bool PredicateFile(SyntaxNode node, CancellationToken token)
    {
        if (!Predicate(node, token, out var declarationSyntax))
        {
            return false;
        }

        foreach (var list in declarationSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                token.ThrowIfCancellationRequested();
                switch (attribute.Name)
                {
                    case SimpleNameSyntax simple when simple.Identifier.Text.StartsWith("FileEmbed"):
                    case QualifiedNameSyntax qualified when qualified.Right.Identifier.Text.StartsWith("FileEmbed"):
                        return true;
                }
            }
        }

        return false;
    }

    private bool PredicateFolder(SyntaxNode node, CancellationToken token)
    {
        if (!Predicate(node, token, out var declarationSyntax) || declarationSyntax.ParameterList.Parameters.Count != 1)
        {
            return false;
        }

        foreach (var list in declarationSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                token.ThrowIfCancellationRequested();
                switch (attribute.Name)
                {
                    case SimpleNameSyntax simple when simple.Identifier.Text.StartsWith("FolderEmbed"):
                    case QualifiedNameSyntax qualified when qualified.Right.Identifier.Text.StartsWith("FolderEmbed"):
                        return true;
                }
            }
        }

        return false;
    }

    private Options SelectOptions(AnalyzerConfigOptionsProvider provider, CancellationToken token)
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
}
