using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Tools.ExternalDevServices.Utils;

public static class RoslynUtils
{
    public static ImmutableDictionary<int, string> GetCSharpFileLineNumbersToDeclarationStringsMap(string csharpFileText)
    {
        var tree = CSharpSyntaxTree.ParseText(csharpFileText);
        var root = tree.GetRoot();
        var text = tree.GetText();
        var lines = text.Lines;

        var map = ImmutableDictionary.CreateBuilder<int, string>();

        // Iterate once over members, plus local functions which are statements (not members).
        foreach (var node in root.DescendantNodes(descendIntoTrivia: false))
        {
            int? startPos = node switch
            {
                // Type declarations
                ClassDeclarationSyntax @class => FirstModifierOrFallback(@class.Modifiers, @class.Keyword.SpanStart),
                StructDeclarationSyntax @struct => FirstModifierOrFallback(@struct.Modifiers, @struct.Keyword.SpanStart),
                InterfaceDeclarationSyntax @interface => FirstModifierOrFallback(@interface.Modifiers, @interface.Keyword.SpanStart),
                RecordDeclarationSyntax record => FirstModifierOrFallback(record.Modifiers, record.Keyword.SpanStart),

                // Methods / ctors / operators (members)
                MethodDeclarationSyntax method => FirstModifierOrFallback(method.Modifiers, method.ReturnType.SpanStart),
                ConstructorDeclarationSyntax ctor => FirstModifierOrFallback(ctor.Modifiers, ctor.Identifier.SpanStart),
                OperatorDeclarationSyntax op => FirstModifierOrFallback(op.Modifiers, op.ReturnType.SpanStart),
                ConversionOperatorDeclarationSyntax conv
                                                 => FirstModifierOrFallback(conv.Modifiers, conv.Type.SpanStart),

                // Ignore everything else
                _ => null
            };

            if (startPos is null) continue;

            var line = lines.GetLineFromPosition(startPos.Value).LineNumber;
            // Keep first occurrence for a line (rare collisions). Change to indexer assignment if you prefer "last wins".
            map.TryAdd(line, lines[line].ToString());
        }

        return map.ToImmutable();

        static int FirstModifierOrFallback(SyntaxTokenList modifiers, int fallbackStart)
            => modifiers.Count > 0 ? modifiers[0].SpanStart : fallbackStart;
    }
}
