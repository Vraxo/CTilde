namespace CTilde;

// Base classes
public abstract record AstNode;
public abstract record StatementNode : AstNode;
public abstract record ExpressionNode : AstNode;

// Program structure
public record ProgramNode(List<FunctionDeclarationNode> Functions) : AstNode;
public record FunctionDeclarationNode(string Name, StatementNode Body) : AstNode;

// Statements
public record BlockStatementNode(List<StatementNode> Statements) : StatementNode;
public record ReturnStatementNode(ExpressionNode Expression) : StatementNode;
public record WhileStatementNode(ExpressionNode Condition, StatementNode Body) : StatementNode;
public record IfStatementNode(ExpressionNode Condition, StatementNode ThenBody, StatementNode? ElseBody) : StatementNode;
public record DeclarationStatementNode(Token Identifier, ExpressionNode? Initializer) : StatementNode;
public record ExpressionStatementNode(ExpressionNode Expression) : StatementNode;


// Expressions
public record IntegerLiteralNode(int Value) : ExpressionNode;
public record AssignmentExpressionNode(Token Identifier, ExpressionNode Value) : ExpressionNode;
public record VariableExpressionNode(Token Identifier) : ExpressionNode;
public record CallExpressionNode(Token Callee, List<ExpressionNode> Arguments) : ExpressionNode;
public record BinaryExpressionNode(ExpressionNode Left, Token Operator, ExpressionNode Right) : ExpressionNode;