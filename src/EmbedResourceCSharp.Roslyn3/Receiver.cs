using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmbedResourceCSharp;

internal class Receiver : ISyntaxReceiver
{
    public readonly List<MethodDeclarationSyntax> FileCandidates = new();
    public readonly List<MethodDeclarationSyntax> FolderCandidates = new();

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } declarationSyntax)
        {
            return;
        }

        foreach (var list in declarationSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                switch (attribute.Name)
                {
                    case SimpleNameSyntax simple:
                        {
                            var name = simple.Identifier.Text;
                            if (name is "FileEmbed" or "FileEmbedAttribute")
                            {
                                FileCandidates.Add(declarationSyntax);
                                return;
                            }
                            else if (name is "FolderEmbed" or "FolderEmbedAttribute")
                            {
                                FolderCandidates.Add(declarationSyntax);
                                return;
                            }
                        }
                        break;
                    case QualifiedNameSyntax qualified:
                        {
                            var name = qualified.Right.Identifier.Text;
                            if (name is "FileEmbed" or "FileEmbedAttribute")
                            {
                                FileCandidates.Add(declarationSyntax);
                                return;
                            }
                            else if (name is "FolderEmbed" or "FolderEmbedAttribute")
                            {
                                FolderCandidates.Add(declarationSyntax);
                                return;
                            }
                        }
                        break;
                }
            }
        }
    }
}
