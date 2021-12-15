using Microsoft.CodeAnalysis;

namespace EmbedResourceCSharp;

internal sealed class DiagnosticsHelper
{
    internal static readonly DiagnosticDescriptor FileNotFoundError = new(
        id: "EMBED001",
        title: "File Not Found",
        messageFormat: "File '{0}' is not found",
        category: "ResourceEmbedCSharp",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor FolderNotFoundError = new(
        id: "EMBED002",
        title: "Folder Not Found",
        messageFormat: "Folder '{0}' is not found",
        category: "ResourceEmbedCSharp",
        DiagnosticSeverity.Error,
        true);
}
