using System.Collections.Generic;
using System.Linq;

namespace CTilde;

/// <summary>
/// A visitor that performs a deep clone of an AST subtree.
/// It can replace TypeNodes on the fly based on a provided dictionary.
/// </summary>
public class AstCloner
{
    private readonly Dictionary<string, TypeNode> _replacements;

    public AstCloner(Dictionary<string, TypeNode> replacements)
    {
        _replacements = replacements;
    }

    public T Clone<T>(T? node) where T : AstNode
    {
        if (node is null) return null!;
        return (T)Visit((dynamic)node);
    }

    private AstNode Visit(AstNode node) => node; // Fallback for unknown nodes

    private TypeNode Visit(TypeNode node)
    {
        // This is the core substitution logic
        if (node is SimpleTypeNode stn && _replacements.TryGetValue(stn.TypeToken.Value, out var replacement))
        {
            return Clone(replacement); // Clone the replacement to ensure the new tree is fully independent
        }

        // Standard cloning for other TypeNodes
        return node switch
        {
            SimpleTypeNode s => new SimpleTypeNode(s.TypeToken),
            PointerTypeNode p => new PointerTypeNode(Visit(p.BaseType)),
            GenericInstantiationTypeNode g => new GenericInstantiationTypeNode(g.BaseType, g.TypeArguments.Select(Visit).ToList()),
            _ => throw new System.NotImplementedException($"Clone not implemented for TypeNode: {node.GetType().Name}")
        };
    }

    // --- Expression Nodes ---
    private ExpressionNode Visit(ExpressionNode node)
    {
        return node switch
        {
            InitializerListExpressionNode il => new InitializerListExpressionNode(il.OpeningBrace, il.Values.Select(Clone).ToList()),
            IntegerLiteralNode i => i, // Immutable
            StringLiteralNode s => s, // Immutable
            UnaryExpressionNode u => new UnaryExpressionNode(u.Operator, Clone(u.Right)),
            AssignmentExpressionNode a => new AssignmentExpressionNode(Clone(a.Left), Clone(a.Right)),
            VariableExpressionNode v => new VariableExpressionNode(v.Identifier),
            CallExpressionNode c => new CallExpressionNode(Clone(c.Callee), c.Arguments.Select(Clone).ToList()),
            BinaryExpressionNode b => new BinaryExpressionNode(Clone(b.Left), b.Operator, Clone(b.Right)),
            MemberAccessExpressionNode ma => new MemberAccessExpressionNode(Clone(ma.Left), ma.Operator, ma.Member),
            QualifiedAccessExpressionNode qa => new QualifiedAccessExpressionNode(Clone(qa.Left), qa.Member),
            NewExpressionNode n => new NewExpressionNode(Visit(n.Type), n.Arguments.Select(Clone).ToList()),
            SizeofExpressionNode s => new SizeofExpressionNode(s.SizeofToken, Visit(s.Type)),
            _ => throw new System.NotImplementedException($"Clone not implemented for ExpressionNode: {node.GetType().Name}")
        };
    }

    // --- Statement Nodes ---
    private StatementNode Visit(StatementNode node)
    {
        return node switch
        {
            BlockStatementNode b => new BlockStatementNode(b.Statements.Select(Clone).ToList()),
            ReturnStatementNode r => new ReturnStatementNode(Clone(r.Expression)),
            WhileStatementNode w => new WhileStatementNode(Clone(w.Condition), Clone(w.Body)),
            IfStatementNode i => new IfStatementNode(Clone(i.Condition), Clone(i.ThenBody), Clone(i.ElseBody)),
            DeclarationStatementNode d => new DeclarationStatementNode(d.IsConst, Visit(d.Type), d.Identifier, Clone(d.Initializer), d.ConstructorArguments?.Select(Clone).ToList()),
            ExpressionStatementNode e => new ExpressionStatementNode(Clone(e.Expression)),
            DeleteStatementNode del => new DeleteStatementNode(Clone(del.Expression)),
            _ => throw new System.NotImplementedException($"Clone not implemented for StatementNode: {node.GetType().Name}")
        };
    }

    // --- Top Level & Definitions ---
    public StructDefinitionNode Visit(StructDefinitionNode node)
    {
        return new StructDefinitionNode(
            node.Name,
            node.GenericParameters, // These are kept for now and cleared in the Monomorphizer
            node.BaseStructName,
            node.Namespace,
            node.Members.Select(Clone).ToList(),
            node.Properties.Select(Clone).ToList(),
            node.Methods.Select(Clone).ToList(),
            node.Constructors.Select(Clone).ToList(),
            node.Destructors.Select(Clone).ToList()
        );
    }

    private MemberVariableNode Visit(MemberVariableNode node) => new(node.IsConst, Visit(node.Type), node.Name, node.AccessLevel);
    private PropertyDefinitionNode Visit(PropertyDefinitionNode node) => new(Visit(node.Type), node.Name, node.AccessLevel, node.Accessors.Select(Clone).ToList());
    private PropertyAccessorNode Visit(PropertyAccessorNode node) => node; // Immutable
    private ParameterNode Visit(ParameterNode node) => new(Visit(node.Type), node.Name);
    private BaseInitializerNode Visit(BaseInitializerNode node) => new(node.Arguments.Select(Clone).ToList());

    private FunctionDeclarationNode Visit(FunctionDeclarationNode node)
    {
        return new FunctionDeclarationNode(
            Visit(node.ReturnType),
            node.Name,
            node.Parameters.Select(Clone).ToList(),
            Clone(node.Body),
            node.OwnerStructName,
            node.AccessLevel,
            node.IsVirtual,
            node.IsOverride,
            node.Namespace
        );
    }

    private ConstructorDeclarationNode Visit(ConstructorDeclarationNode node)
    {
        return new ConstructorDeclarationNode(
            node.OwnerStructName,
            node.Namespace,
            node.AccessLevel,
            node.Parameters.Select(Clone).ToList(),
            Clone(node.Initializer),
            Clone(node.Body)
        );
    }

    private DestructorDeclarationNode Visit(DestructorDeclarationNode node)
    {
        return new DestructorDeclarationNode(
            node.OwnerStructName,
            node.Namespace,
            node.AccessLevel,
            node.IsVirtual,
            Clone(node.Body)
        );
    }
}