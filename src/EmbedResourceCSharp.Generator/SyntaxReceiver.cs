using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmbedResourceCSharp
{
    public class SyntaxReceiver
        : ISyntaxReceiver
    {
        public List<MethodDeclarationSyntax> Candidates { get; } = new List<MethodDeclarationSyntax>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (!(syntaxNode is MethodDeclarationSyntax declarationSyntax)
                || declarationSyntax.AttributeLists.Count <= 0)
            {
                return;
            }

            var @static = false;
            var @partial = false;
            foreach (var declarationSyntaxModifier in declarationSyntax.Modifiers)
            {
                if (declarationSyntaxModifier.ValueText == "static")
                {
                    @static = true;
                }
                else if (declarationSyntaxModifier.ValueText == "partial")
                {
                    @partial = true;
                }
            }

            if (!@static || !@partial)
            {
                return;
            }

            Candidates.Add(declarationSyntax);
        }
    }
}
