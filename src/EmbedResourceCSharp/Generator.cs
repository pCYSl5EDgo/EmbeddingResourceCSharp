using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EmbedResourceCSharp;

[Generator(LanguageNames.CSharp)]
public sealed class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(GenerateInitialCode);
        var options = context.AnalyzerConfigOptionsProvider
            .Select(SelectOptions)
            .WithComparer(Options.Comparer.Instance);
        var types = context.CompilationProvider
            .Select(SelectExtractionTypes)
            .WithComparer(TypeExtraction.Comparer.Instance);
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

    private (IMethodSymbol? Method, AttributeData? Data) PostTransformFolder((IMethodSymbol? Method, TypeExtraction Types) pair, CancellationToken token)
    {
        var method = pair.Method;
        if (method is null)
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

    private (IMethodSymbol? Method, AttributeData? Data) PostTransformFile((IMethodSymbol? Method, TypeExtraction Types) pair, CancellationToken token)
    {
        var method = pair.Method;
        if (method is null)
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

    private TypeExtraction SelectExtractionTypes(Compilation source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(source);
    }

    private void GenerateFolderEmbed(SourceProductionContext context, ((IMethodSymbol Method, AttributeData Data) Left, Options Options) pair)
    {
        StringBuilder builder;

        context.CancellationToken.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            builder = new StringBuilder();
            Utility.ProcessFolderDesignTimeBuild(ref builder, method);
            goto SUCCESS;
        }

        if (!Utility.ExtractFolder(method, pair.Left.Data, out var extract))
        {
            return;
        }

        builder = new StringBuilder();
        var exists = Utility.ProcessFolder(builder, pair.Options.ProjectDirectory, extract, context.CancellationToken);
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

        context.CancellationToken.ThrowIfCancellationRequested();
        var method = pair.Left.Method;
        if (pair.Options.IsDesignTimeBuild)
        {
            builder = new StringBuilder();
            Utility.ProcessFileDesignTimeBuild(ref builder, method);
            goto SUCCESS;
        }

        var attribute = pair.Left.Data;
        if (!Utility.ExtractFile(method, attribute, out var extract))
        {
            return;
        }

        builder = new StringBuilder();
        var exists = Utility.ProcessFile(builder, pair.Options.ProjectDirectory, extract, context.CancellationToken);
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

    private IMethodSymbol? Transform(GeneratorSyntaxContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var syntax = (context.Node as MethodDeclarationSyntax)!;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax, token);
        return symbol;
    }

    private bool PredicateFile(SyntaxNode node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } declarationSyntax)
        {
            return false;
        }

        const string Embed = "FileEmbed";
        const string EmbedAttribute = Embed + "Attribute";

        foreach (var list in declarationSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                token.ThrowIfCancellationRequested();
                switch (attribute.Name)
                {
                    case SimpleNameSyntax simple when simple.Identifier.Text is Embed or EmbedAttribute:
                    case QualifiedNameSyntax qualified when qualified.Right.Identifier.Text is Embed or EmbedAttribute:
                        return true;
                }
            }
        }

        return false;
    }

    private bool PredicateFolder(SyntaxNode node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } declarationSyntax)
        {
            return false;
        }

        const string Embed = "FolderEmbed";
        const string EmbedAttribute = Embed + "Attribute";

        foreach (var list in declarationSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                token.ThrowIfCancellationRequested();
                switch (attribute.Name)
                {
                    case SimpleNameSyntax simple when simple.Identifier.Text is Embed or EmbedAttribute:
                    case QualifiedNameSyntax qualified when qualified.Right.Identifier.Text is Embed or EmbedAttribute:
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

    private void GenerateInitialCode(IncrementalGeneratorPostInitializationContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.AddSource("Attribute.cs", @"namespace EmbedResourceCSharp
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
");
    }
}
