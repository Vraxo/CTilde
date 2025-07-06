using System.Collections.Generic;

namespace CTilde;

public enum AccessSpecifier { Public, Private }

// Base classes
public abstract record AstNode
{
    public AstNode? Parent { get; set; }

    public IEnumerable<AstNode> Ancestors()
    {
        var current = Parent;
        while (current != null)
        {
            yield return current;
            current = current.Parent;
        }
    }

    public T? FindAncestorOfType<T>() where T : AstNode
    {
        var current = Parent;
        while (current != null)
        {
            if (current is T target) return target;
            current = current.Parent;
        }
        return null;
    }
}
public abstract record StatementNode : AstNode;
public abstract record ExpressionNode : AstNode;

// Program structure
public record ImportDirectiveNode(string LibraryName) : AstNode;
public record UsingDirectiveNode(string Namespace, string? Alias) : AstNode;
public record MemberVariableNode(bool IsConst, TypeNode Type, Token Name, AccessSpecifier AccessLevel) : AstNode;
public record PropertyAccessorNode(Token AccessorKeyword, StatementNode? Body) : AstNode;
public record PropertyDefinitionNode(TypeNode Type, Token Name, AccessSpecifier AccessLevel, bool IsVirtual, bool IsOverride, PropertyAccessorNode? Getter, PropertyAccessorNode? Setter) : AstNode;
public record StructDefinitionNode(string Name, List<Token> GenericParameters, string? BaseStructName, string? Namespace, List<MemberVariableNode> Members, List<FunctionDeclarationNode> Methods, List<ConstructorDeclarationNode> Constructors, List<DestructorDeclarationNode> Destructors, List<PropertyDefinitionNode> Properties) : AstNode;
public record ParameterNode(TypeNode Type, Token Name) : AstNode;
public record FunctionDeclarationNode(TypeNode ReturnType, string Name, List<ParameterNode> Parameters, StatementNode? Body, string? OwnerStructName, AccessSpecifier AccessLevel, bool IsVirtual, bool IsOverride, string? Namespace) : AstNode;
public record BaseInitializerNode(List<ExpressionNode> Arguments) : AstNode;
public record ConstructorDeclarationNode(string OwnerStructName, string? Namespace, AccessSpecifier AccessLevel, List<ParameterNode> Parameters, BaseInitializerNode? Initializer, StatementNode Body) : AstNode;
public record DestructorDeclarationNode(string OwnerStructName, string? Namespace, AccessSpecifier AccessLevel, bool IsVirtual, StatementNode Body) : AstNode;
public record EnumDefinitionNode(string Name, string? Namespace, List<EnumMemberNode> Members) : AstNode;
public record EnumMemberNode(Token Name, int Value) : AstNode;

// New top-level structure for compilation units
public record CompilationUnitNode(string FilePath, List<UsingDirectiveNode> Usings, List<StructDefinitionNode> Structs, List<FunctionDeclarationNode> Functions, List<EnumDefinitionNode> Enums) : AstNode;
public record ProgramNode(List<ImportDirectiveNode> Imports, List<CompilationUnitNode> CompilationUnits) : AstNode;


// Statements
public record BlockStatementNode(List<StatementNode> Statements) : StatementNode;
public record ReturnStatementNode(ExpressionNode? Expression) : StatementNode;
public record WhileStatementNode(ExpressionNode Condition, StatementNode Body) : StatementNode;
public record IfStatementNode(ExpressionNode Condition, StatementNode ThenBody, StatementNode? ElseBody) : StatementNode;
public record DeclarationStatementNode(bool IsConst, TypeNode Type, Token Identifier, ExpressionNode? Initializer, List<ExpressionNode>? ConstructorArguments) : StatementNode;
public record ExpressionStatementNode(ExpressionNode Expression) : StatementNode;
public record DeleteStatementNode(ExpressionNode Expression) : StatementNode;


// Expressions
public record InitializerListExpressionNode(Token OpeningBrace, List<ExpressionNode> Values) : ExpressionNode;
public record IntegerLiteralNode(Token Token, int Value) : ExpressionNode;
public record StringLiteralNode(Token Token, string Value, string Label) : ExpressionNode;
public record UnaryExpressionNode(Token Operator, ExpressionNode Right) : ExpressionNode;
public record AssignmentExpressionNode(ExpressionNode Left, ExpressionNode Right) : ExpressionNode;
public record VariableExpressionNode(Token Identifier) : ExpressionNode;
public record CallExpressionNode(ExpressionNode Callee, List<ExpressionNode> Arguments) : ExpressionNode;
public record BinaryExpressionNode(ExpressionNode Left, Token Operator, ExpressionNode Right) : ExpressionNode;
public record MemberAccessExpressionNode(ExpressionNode Left, Token Operator, Token Member) : ExpressionNode;
public record QualifiedAccessExpressionNode(ExpressionNode Left, Token Member) : ExpressionNode;
public record NewExpressionNode(TypeNode Type, List<ExpressionNode> Arguments) : ExpressionNode;
public record SizeofExpressionNode(Token SizeofToken, TypeNode Type) : ExpressionNode;