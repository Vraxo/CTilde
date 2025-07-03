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
}
public abstract record StatementNode : AstNode;
public abstract record ExpressionNode : AstNode;

// Program structure
public record ImportDirectiveNode(string LibraryName) : AstNode;
public record UsingDirectiveNode(string Namespace, string? Alias) : AstNode;
public record MemberVariableNode(bool IsConst, Token Type, int PointerLevel, Token Name, AccessSpecifier AccessLevel) : AstNode;
public record StructDefinitionNode(string Name, string? BaseStructName, string? Namespace, List<MemberVariableNode> Members) : AstNode;
public record ParameterNode(Token Type, int PointerLevel, Token Name) : AstNode;
public record FunctionDeclarationNode(Token ReturnType, int ReturnPointerLevel, string Name, List<ParameterNode> Parameters, StatementNode? Body, string? OwnerStructName, AccessSpecifier AccessLevel, string? Namespace) : AstNode;
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
public record DeclarationStatementNode(bool IsConst, Token Type, int PointerLevel, Token Identifier, ExpressionNode? Initializer) : StatementNode;
public record ExpressionStatementNode(ExpressionNode Expression) : StatementNode;


// Expressions
public record InitializerListExpressionNode(List<ExpressionNode> Values) : ExpressionNode;
public record IntegerLiteralNode(int Value) : ExpressionNode;
public record StringLiteralNode(string Value, string Label) : ExpressionNode;
public record UnaryExpressionNode(Token Operator, ExpressionNode Right) : ExpressionNode;
public record AssignmentExpressionNode(ExpressionNode Left, ExpressionNode Right) : ExpressionNode;
public record VariableExpressionNode(Token Identifier) : ExpressionNode;
public record CallExpressionNode(ExpressionNode Callee, List<ExpressionNode> Arguments) : ExpressionNode;
public record BinaryExpressionNode(ExpressionNode Left, Token Operator, ExpressionNode Right) : ExpressionNode;
public record MemberAccessExpressionNode(ExpressionNode Left, Token Operator, Token Member) : ExpressionNode;
public record QualifiedAccessExpressionNode(ExpressionNode Left, Token Member) : ExpressionNode; // MODIFIED