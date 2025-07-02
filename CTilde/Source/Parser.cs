namespace CTilde;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _position;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
    }

    private Token Current => _position < _tokens.Count ? _tokens[_position] : new(TokenType.Unknown, string.Empty);
    private Token Peek(int offset) => _position + offset < _tokens.Count ? _tokens[_position + offset] : new(TokenType.Unknown, string.Empty);

    private Token Eat(TokenType expectedType)
    {
        Token currentToken = Current;

        if (currentToken.Type != expectedType)
        {
            throw new InvalidOperationException($"Expected token {expectedType} but got {currentToken.Type}");
        }

        _position++;
        return currentToken;
    }

    public ProgramNode Parse()
    {
        FunctionDeclarationNode function = ParseFunction();
        return new(function);
    }

    private FunctionDeclarationNode ParseFunction()
    {
        Eat(TokenType.Keyword);

        Token identifier = Eat(TokenType.Identifier);

        Eat(TokenType.LeftParen);
        Eat(TokenType.RightParen);

        BlockStatementNode body = ParseBlockStatement();

        return new(identifier.Value, body);
    }

    private BlockStatementNode ParseBlockStatement()
    {
        Eat(TokenType.LeftBrace);

        List<StatementNode> statements = [];

        while (Current.Type != TokenType.RightBrace)
        {
            statements.Add(ParseStatement());
        }

        Eat(TokenType.RightBrace);
        return new(statements);
    }

    private StatementNode ParseStatement()
    {
        if (Current.Type == TokenType.Keyword && Current.Value == "return")
        {
            return ParseReturnStatement();
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "while")
        {
            return ParseWhileStatement();
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "if")
        {
            return ParseIfStatement();
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "int")
        {
            return ParseDeclarationStatement();
        }

        if (Current.Type == TokenType.LeftBrace)
        {
            return ParseBlockStatement();
        }

        // Otherwise, it must be an expression statement (e.g. assignment, call)
        var expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ExpressionStatementNode(expression);
    }

    private StatementNode ParseIfStatement()
    {
        Eat(TokenType.Keyword); // "if"
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);

        var thenBranch = ParseStatement();
        StatementNode? elseBranch = null;

        if (Current.Type == TokenType.Keyword && Current.Value == "else")
        {
            Eat(TokenType.Keyword); // "else"
            elseBranch = ParseStatement();
        }

        return new IfStatementNode(condition, thenBranch, elseBranch);
    }

    private StatementNode ParseDeclarationStatement()
    {
        Eat(TokenType.Keyword); // "int"
        var identifier = Eat(TokenType.Identifier);
        ExpressionNode? initializer = null;

        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment); // "="
            initializer = ParseExpression();
        }

        Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(identifier, initializer);
    }

    private WhileStatementNode ParseWhileStatement()
    {
        Eat(TokenType.Keyword); // "while"
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);
        var body = ParseStatement();
        return new WhileStatementNode(condition, body);
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        Eat(TokenType.Keyword); // "return"
        var expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ReturnStatementNode(expression);
    }

    private ExpressionNode ParseExpression()
    {
        return ParseAssignmentExpression();
    }

    private ExpressionNode ParseAssignmentExpression()
    {
        ExpressionNode left = ParseEqualityExpression();

        if (Current.Type != TokenType.Assignment)
        {
            return left;
        }

        Eat(TokenType.Assignment);

        ExpressionNode right = ParseAssignmentExpression();

        if (left is VariableExpressionNode varNode)
        {
            return new AssignmentExpressionNode(varNode.Identifier, right);
        }

        throw new InvalidOperationException("Invalid assignment target.");
    }

    private ExpressionNode ParseEqualityExpression()
    {
        ExpressionNode left = ParseComparisonExpression();

        while (Current.Type is TokenType.Equal or TokenType.NotEqual)
        {
            Token op = Current;
            Eat(Current.Type);
            ExpressionNode right = ParseComparisonExpression();
            left = new BinaryExpressionNode(left, op, right);
        }

        return left;
    }

    private ExpressionNode ParseComparisonExpression()
    {
        ExpressionNode left = ParseAdditiveExpression();

        while (Current.Type is TokenType.LessThan or TokenType.LessThanOrEqual or TokenType.GreaterThan or TokenType.GreaterThanOrEqual)
        {
            Token op = Current;
            Eat(Current.Type);
            ExpressionNode right = ParseAdditiveExpression();
            left = new BinaryExpressionNode(left, op, right);
        }

        return left;
    }

    private ExpressionNode ParseAdditiveExpression()
    {
        ExpressionNode left = ParseUnaryExpression();

        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            Token op = Current;
            Eat(Current.Type);
            ExpressionNode right = ParseUnaryExpression();
            left = new BinaryExpressionNode(left, op, right);
        }

        return left;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        if (Current.Type == TokenType.Minus)
        {
            Token op = Eat(TokenType.Minus);
            ExpressionNode operand = ParseUnaryExpression();
            return new UnaryExpressionNode(op, operand);
        }

        return ParseCallExpression();
    }

    private ExpressionNode ParseCallExpression()
    {
        ExpressionNode expr = ParsePrimaryExpression();

        if (Current.Type != TokenType.LeftParen)
        {
            return expr;
        }

        if (expr is VariableExpressionNode varNode)
        {
            Eat(TokenType.LeftParen);

            ExpressionNode argument = ParseExpression();

            Eat(TokenType.RightParen);
            return new CallExpressionNode(varNode.Identifier, argument);
        }

        throw new InvalidOperationException("Expression is not callable.");
    }

    private ExpressionNode ParsePrimaryExpression()
    {
        if (Current.Type == TokenType.IntegerLiteral)
        {
            Token token = Eat(TokenType.IntegerLiteral);

            if (int.TryParse(token.Value, out int value))
            {
                return new IntegerLiteralNode(value);
            }

            throw new InvalidOperationException($"Could not parse integer literal: {token.Value}");
        }

        if (Current.Type == TokenType.Identifier)
        {
            Token token = Eat(TokenType.Identifier);
            return new VariableExpressionNode(token);
        }

        if (Current.Type == TokenType.LeftParen)
        {
            Eat(TokenType.LeftParen);
            var expr = ParseExpression();
            Eat(TokenType.RightParen);
            return expr;
        }

        throw new InvalidOperationException($"Unexpected expression token: {Current.Type}");
    }
}