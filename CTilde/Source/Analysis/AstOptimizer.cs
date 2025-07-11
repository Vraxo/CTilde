﻿using System.Linq;
using CTilde.Diagnostics;

namespace CTilde.Analysis;

public class AstOptimizer
{
    private readonly Parser _dummyParser = new([]);
    private OptimizationLogger? _logger;

    public ProgramNode Optimize(ProgramNode programNode, OptimizationLogger? logger)
    {
        _logger = logger;
        ProgramNode newProgram = Visit(programNode);
        _dummyParser.SetParents(newProgram, null);

        return newProgram;
    }

    private string GetContextString(AstNode node)
    {
        var func = node.Ancestors().OfType<FunctionDeclarationNode>().FirstOrDefault();
        if (func != null) return $"Function '{func.Name}'";

        var ctor = node.Ancestors().OfType<ConstructorDeclarationNode>().FirstOrDefault();
        if (ctor != null) return $"Constructor for '{ctor.OwnerStructName}'";

        var dtor = node.Ancestors().OfType<DestructorDeclarationNode>().FirstOrDefault();
        if (dtor != null) return $"Destructor for '{dtor.OwnerStructName}'";

        return "Global scope";
    }

    private T? Visit<T>(T? node) where T : AstNode
    {
        return node is null
            ? null
            : (T)Visit((dynamic)node);
    }

    private AstNode Visit(AstNode node)
    {
        return node;
    }

    private ProgramNode Visit(ProgramNode node)
    {
        return new(
            node.Imports.Select(Visit).ToList(),
            node.CompilationUnits.Select(Visit).ToList()
        );
    }

    private CompilationUnitNode Visit(CompilationUnitNode node)
    {
        return new(
            node.FilePath,
            node.Usings.Select(Visit).ToList(),
            node.Structs.Select(Visit).ToList(),
            node.Functions.Select(Visit).ToList(),
            node.Enums.Select(Visit).ToList()
        );
    }

    private FunctionDeclarationNode Visit(FunctionDeclarationNode node)
    {
        return new(
            node.ReturnType, node.Name, node.Parameters, Visit(node.Body),
            node.OwnerStructName, node.AccessLevel, node.IsVirtual, node.IsOverride, node.Namespace
        );
    }

    private ConstructorDeclarationNode Visit(ConstructorDeclarationNode node)
    {
        return new(
            node.OwnerStructName, node.Namespace, node.AccessLevel,
            node.Parameters, Visit(node.Initializer), Visit(node.Body)
        );
    }

    private DestructorDeclarationNode Visit(DestructorDeclarationNode node)
    {
        return new(
            node.OwnerStructName, node.Namespace, node.AccessLevel,
            node.IsVirtual, Visit(node.Body)
        );
    }

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
            _ => node
        };
    }

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
            _ => node,
        };
    }

    private ExpressionNode Visit(BinaryExpressionNode node)
    {
        ExpressionNode left = Visit(node.Left);
        ExpressionNode right = Visit(node.Right);

        if (left is IntegerLiteralNode l && right is IntegerLiteralNode r)
        {
            Token token = l.Token;
            var originalExpression = $"{l.Value} {node.Operator.Value} {r.Value}";
            IntegerLiteralNode? result = null;

            switch (node.Operator.Type)
            {
                case TokenType.Plus:
                    result = new IntegerLiteralNode(token, l.Value + r.Value);
                    break;
                case TokenType.Minus:
                    result = new IntegerLiteralNode(token, l.Value - r.Value);
                    break;
                case TokenType.Star:
                    result = new IntegerLiteralNode(token, l.Value * r.Value);
                    break;
                case TokenType.Slash:
                    if (r.Value != 0) // Avoid division by zero at compile time
                    {
                        result = new IntegerLiteralNode(token, l.Value / r.Value);
                    }
                    break; // Fall through to not optimize
                case TokenType.DoubleEquals:
                    result = new IntegerLiteralNode(token, l.Value == r.Value ? 1 : 0);
                    break;
                case TokenType.NotEquals:
                    result = new IntegerLiteralNode(token, l.Value != r.Value ? 1 : 0);
                    break;
                case TokenType.LessThan:
                    result = new IntegerLiteralNode(token, l.Value < r.Value ? 1 : 0);
                    break;
                case TokenType.GreaterThan:
                    result = new IntegerLiteralNode(token, l.Value > r.Value ? 1 : 0);
                    break;
            }

            if (result is not null)
            {
                _logger?.Log(
                    "Constant Folding",
                    originalExpression,
                    result.Value.ToString(),
                    GetContextString(node)
                );
                return result;
            }
        }

        if (ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right))
        {
            return node;
        }

        return new BinaryExpressionNode(left, node.Operator, right);
    }

    private ImportDirectiveNode Visit(ImportDirectiveNode n)
    {
        return n;
    }

    private UsingDirectiveNode Visit(UsingDirectiveNode n)
    {
        return n;
    }

    private StructDefinitionNode Visit(StructDefinitionNode n)
    {
        return n;
    }

    private EnumDefinitionNode Visit(EnumDefinitionNode n)
    {
        return n;
    }

    private BaseInitializerNode Visit(BaseInitializerNode n)
    {
        return n;
    }
}