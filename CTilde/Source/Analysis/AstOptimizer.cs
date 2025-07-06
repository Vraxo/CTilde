using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde.Analysis;

/// <summary>
/// An AST visitor that performs tree-to-tree transformations for optimization.
/// Currently implements constant folding for arithmetic and comparison expressions.
/// </summary>
public class AstOptimizer
{
    private Parser _dummyParser = new Parser(new List<Token>());

    public ProgramNode Optimize(ProgramNode programNode)
    {
        var newProgram = Visit(programNode);
        _dummyParser.SetParents(newProgram, null);
        return newProgram;
    }

    private T Visit<T>(T? node) where T : AstNode
    {
        if (node == null) return null!;
        return (T)Visit((dynamic)node);
    }

    // --- Fallback and Top-Level ---
    private AstNode Visit(AstNode node) => node; // Unknown nodes are passed through.

    private ProgramNode Visit(ProgramNode node)
    {
        return new ProgramNode(
            node.Imports.Select(Visit).ToList(),
            node.CompilationUnits.Select(Visit).ToList()
        );
    }

    private CompilationUnitNode Visit(CompilationUnitNode node)
    {
        return new CompilationUnitNode(
            node.FilePath,
            node.Usings.Select(Visit).ToList(),
            node.Structs.Select(Visit).ToList(),
            node.Functions.Select(Visit).ToList(),
            node.Enums.Select(Visit).ToList()
        );
    }

    private FunctionDeclarationNode Visit(FunctionDeclarationNode node)
    {
        return new FunctionDeclarationNode(
            node.ReturnType, node.Name, node.Parameters, Visit(node.Body),
            node.OwnerStructName, node.AccessLevel, node.IsVirtual, node.IsOverride, node.Namespace
        );
    }

    private ConstructorDeclarationNode Visit(ConstructorDeclarationNode node)
    {
        return new ConstructorDeclarationNode(
            node.OwnerStructName, node.Namespace, node.AccessLevel,
            node.Parameters, Visit(node.Initializer), Visit(node.Body)
        );
    }

    private DestructorDeclarationNode Visit(DestructorDeclarationNode node)
    {
        return new DestructorDeclarationNode(
            node.OwnerStructName, node.Namespace, node.AccessLevel,
            node.IsVirtual, Visit(node.Body)
        );
    }

    // --- Statements ---
    private StatementNode Visit(StatementNode node)
    {
        return node switch
        {
            BlockStatementNode b => new BlockStatementNode(b.Statements.Select(Visit).ToList()),
            ReturnStatementNode r => new ReturnStatementNode(Visit(r.Expression)),
            WhileStatementNode w => new WhileStatementNode(Visit(w.Condition), Visit(w.Body)),
            IfStatementNode i => new IfStatementNode(Visit(i.Condition), Visit(i.ThenBody), Visit(i.ElseBody)),
            DeclarationStatementNode d => new DeclarationStatementNode(d.IsConst, d.Type, d.Identifier, Visit(d.Initializer), d.ConstructorArguments?.Select(Visit).ToList()),
            ExpressionStatementNode e => new ExpressionStatementNode(Visit(e.Expression)),
            DeleteStatementNode del => new DeleteStatementNode(Visit(del.Expression)),
            _ => node // Passthrough
        };
    }

    // --- Expressions (The Core of the Optimization) ---
    private ExpressionNode Visit(ExpressionNode node)
    {
        return node switch
        {
            InitializerListExpressionNode il => new InitializerListExpressionNode(il.OpeningBrace, il.Values.Select(Visit).ToList()),
            UnaryExpressionNode u => new UnaryExpressionNode(u.Operator, Visit(u.Right)),
            AssignmentExpressionNode a => new AssignmentExpressionNode(Visit(a.Left), Visit(a.Right)),
            CallExpressionNode c => new CallExpressionNode(Visit(c.Callee), c.Arguments.Select(Visit).ToList()),
            BinaryExpressionNode b => Visit(b), // Special handling
            MemberAccessExpressionNode ma => new MemberAccessExpressionNode(Visit(ma.Left), ma.Operator, ma.Member),
            QualifiedAccessExpressionNode qa => new QualifiedAccessExpressionNode(Visit(qa.Left), qa.Member),
            NewExpressionNode n => new NewExpressionNode(n.Type, n.Arguments.Select(Visit).ToList()),
            _ => node, // Passthrough for literals, variables, etc.
        };
    }

    private ExpressionNode Visit(BinaryExpressionNode node)
    {
        // Recursively optimize the children first
        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // Check if both children are now integer literals
        if (left is IntegerLiteralNode l && right is IntegerLiteralNode r)
        {
            var token = l.Token; // Use the left token for position info
            switch (node.Operator.Type)
            {
                case TokenType.Plus:
                    return new IntegerLiteralNode(token, l.Value + r.Value);
                case TokenType.Minus:
                    return new IntegerLiteralNode(token, l.Value - r.Value);
                case TokenType.Star:
                    return new IntegerLiteralNode(token, l.Value * r.Value);
                case TokenType.Slash:
                    if (r.Value != 0) // Avoid division by zero at compile time
                        return new IntegerLiteralNode(token, l.Value / r.Value);
                    break; // Fall through to not optimize
                case TokenType.DoubleEquals:
                    return new IntegerLiteralNode(token, l.Value == r.Value ? 1 : 0);
                case TokenType.NotEquals:
                    return new IntegerLiteralNode(token, l.Value != r.Value ? 1 : 0);
                case TokenType.LessThan:
                    return new IntegerLiteralNode(token, l.Value < r.Value ? 1 : 0);
                case TokenType.GreaterThan:
                    return new IntegerLiteralNode(token, l.Value > r.Value ? 1 : 0);
            }
        }

        // If we cannot fold, return a new binary expression with the (potentially optimized) children
        if (ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right))
            return node; // No changes, return original node.

        return new BinaryExpressionNode(left, node.Operator, right);
    }

    // Pass-through for nodes that don't have children to visit or are not transformed
    private ImportDirectiveNode Visit(ImportDirectiveNode n) => n;
    private UsingDirectiveNode Visit(UsingDirectiveNode n) => n;
    private StructDefinitionNode Visit(StructDefinitionNode n) => n; // We optimize method bodies, not the struct itself yet.
    private EnumDefinitionNode Visit(EnumDefinitionNode n) => n;
    private BaseInitializerNode Visit(BaseInitializerNode n) => n;
}