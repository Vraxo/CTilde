using System.Collections;
using System.Linq;

namespace CTilde;

public static class AstHelper
{
    public static Token GetFirstToken(AstNode node)
    {
        return node switch
        {
            IntegerLiteralNode n => n.Token,
            StringLiteralNode n => n.Token,
            VariableExpressionNode n => n.Identifier,
            UnaryExpressionNode n => n.Operator,
            InitializerListExpressionNode n => n.OpeningBrace,
            DeclarationStatementNode n => n.Type.GetFirstToken(),
            NewExpressionNode n => n.Type.GetFirstToken(),
            MemberAccessExpressionNode n => GetFirstToken(n.Left),
            _ => FindFirstTokenByReflection(node)
        };
    }

    private static Token FindFirstTokenByReflection(AstNode node)
    {
        var properties = node.GetType().GetProperties()
            .Where(p => p.Name != "Parent")
            .OrderBy(p => p.MetadataToken);

        foreach (var prop in properties)
        {
            var value = prop.GetValue(node);
            if (value is Token token) return token;
            if (value is AstNode childNode) return GetFirstToken(childNode);
            if (value is IEnumerable children and not string)
            {
                foreach (var child in children)
                {
                    if (child is AstNode innerChildNode) return GetFirstToken(innerChildNode);
                }
            }
        }
        return new Token(TokenType.Unknown, "", -1, -1);
    }
}