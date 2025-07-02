using System;
using System.Collections.Generic;
using System.Text;

namespace CTilde;

public class Generator
{
    private readonly ProgramNode _program;
    private readonly StringBuilder _sb = new();
    private int _labelCounter;

    // Rudimentary symbol table for a function's scope
    private Dictionary<string, int> _variables = new();
    private int _stackOffset;

    public Generator(ProgramNode program)
    {
        _program = program;
    }

    public string Generate()
    {
        _sb.AppendLine("format PE console");
        _sb.AppendLine("entry start");
        _sb.AppendLine();
        _sb.AppendLine("include 'win32a.inc'");
        _sb.AppendLine();
        _sb.AppendLine("section '.data' data readable writeable");
        _sb.AppendLine("    format_int db '%d', 10, 0 ; Format string for printing an integer with a newline");
        _sb.AppendLine();
        _sb.AppendLine("section '.text' code readable executable");
        _sb.AppendLine();
        _sb.AppendLine("start:");
        _sb.AppendLine("    call main");
        _sb.AppendLine("    mov ebx, eax"); // EAX holds the return value from main
        _sb.AppendLine("    push ebx");
        _sb.AppendLine("    call [ExitProcess]");
        _sb.AppendLine();

        GenerateFunction(_program.Function);

        _sb.AppendLine();
        _sb.AppendLine("section '.idata' import data readable");
        _sb.AppendLine();
        _sb.AppendLine("    library kernel32,'kernel32.dll', msvcrt,'msvcrt.dll'");
        _sb.AppendLine();
        _sb.AppendLine("    import kernel32, ExitProcess,'ExitProcess'");
        _sb.AppendLine("    import msvcrt, printf,'printf'");

        return _sb.ToString();
    }

    private void AppendAsm(string instruction, string? comment = null)
    {
        const int commentColumn = 36;
        string indentedInstruction = $"    {instruction}";

        if (string.IsNullOrEmpty(comment))
        {
            _sb.AppendLine(indentedInstruction);
        }
        else
        {
            _sb.AppendLine(indentedInstruction.PadRight(commentColumn - 1) + $"; {comment}");
        }
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _variables = new Dictionary<string, int>();
        _stackOffset = 0;

        _sb.AppendLine($"{function.Name}:");
        AppendAsm("push ebp");
        AppendAsm("mov ebp, esp");
        _sb.AppendLine();

        GenerateStatement(function.Body);
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatementNode ret:
                GenerateReturn(ret);
                break;
            case BlockStatementNode block:
                foreach (var stmt in block.Statements)
                {
                    GenerateStatement(stmt);
                }
                break;
            case WhileStatementNode whileStmt:
                GenerateWhile(whileStmt);
                break;
            case IfStatementNode ifStmt:
                GenerateIf(ifStmt);
                break;
            case DeclarationStatementNode decl:
                GenerateDeclaration(decl);
                break;
            case ExpressionStatementNode exprStmt:
                GenerateExpression(exprStmt.Expression);
                _sb.AppendLine("    ; Expression statement result is discarded");
                break;
            default:
                throw new NotImplementedException($"Unsupported statement type: {statement.GetType().Name}");
        }
    }

    private void GenerateIf(IfStatementNode ifStmt)
    {
        int labelIndex = _labelCounter++;
        string elseLabel = $"_else_{labelIndex}";
        string endIfLabel = $"_end_if_{labelIndex}";

        // Evaluate condition
        GenerateExpression(ifStmt.Condition);
        AppendAsm("cmp eax, 0");

        bool hasElse = ifStmt.ElseBranch != null;
        string jumpTarget = hasElse ? elseLabel : endIfLabel;

        AppendAsm($"je {jumpTarget}", "Condition is false, jump away");

        // Then branch
        GenerateStatement(ifStmt.ThenBranch);

        // If there's an else branch, handle jump and generation
        if (hasElse)
        {
            AppendAsm($"jmp {endIfLabel}", "Finished 'then' branch, skip 'else'");
            _sb.AppendLine($"{elseLabel}:");
            GenerateStatement(ifStmt.ElseBranch!);
        }

        // End label for both cases
        _sb.AppendLine($"{endIfLabel}:");
    }

    private void GenerateDeclaration(DeclarationStatementNode decl)
    {
        if (_variables.ContainsKey(decl.Identifier.Value))
        {
            throw new InvalidOperationException($"Variable '{decl.Identifier.Value}' is already defined.");
        }

        _stackOffset -= 4; // 4 bytes for an int
        _variables[decl.Identifier.Value] = _stackOffset;
        AppendAsm("sub esp, 4", "Allocate space for variable " + decl.Identifier.Value);

        if (decl.Initializer != null)
        {
            GenerateExpression(decl.Initializer);
            AppendAsm($"mov [ebp + {_stackOffset}], eax");
        }
    }

    private void GenerateWhile(WhileStatementNode whileStmt)
    {
        int labelIndex = _labelCounter++;
        string startLabel = $"_while_start_{labelIndex}";
        string endLabel = $"_while_end_{labelIndex}";

        _sb.AppendLine($"{startLabel}:");

        GenerateExpression(whileStmt.Condition);
        AppendAsm("cmp eax, 0");
        AppendAsm($"je {endLabel}");

        GenerateStatement(whileStmt.Body);

        AppendAsm($"jmp {startLabel}");

        _sb.AppendLine($"{endLabel}:");
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        GenerateExpression(ret.Expression);
        AppendAsm("mov esp, ebp");
        AppendAsm("pop ebp");
        AppendAsm("ret");
    }

    private void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal:
                AppendAsm($"mov eax, {literal.Value}");
                break;
            case VariableExpressionNode varExpr:
                if (!_variables.TryGetValue(varExpr.Identifier.Value, out var offset))
                {
                    throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                }
                AppendAsm($"mov eax, [ebp + {offset}]", $"Load variable {varExpr.Identifier.Value}");
                break;
            case AssignmentExpressionNode assignExpr:
                if (!_variables.TryGetValue(assignExpr.Identifier.Value, out var assignOffset))
                {
                    throw new InvalidOperationException($"Undeclared variable '{assignExpr.Identifier.Value}' for assignment.");
                }
                GenerateExpression(assignExpr.Value);
                AppendAsm($"mov [ebp + {assignOffset}], eax", $"Assign to variable {assignExpr.Identifier.Value}");
                break;
            case CallExpressionNode callExpr:
                if (callExpr.Callee.Value != "print")
                {
                    throw new InvalidOperationException($"Undefined function call: '{callExpr.Callee.Value}'");
                }
                // The 'print' intrinsic
                GenerateExpression(callExpr.Argument);
                AppendAsm("push eax", "Push argument for printf");
                AppendAsm("push format_int", "Push format string");
                AppendAsm("call [printf]");
                AppendAsm("add esp, 8", "Clean up stack for printf");
                AppendAsm("mov eax, 0", "A print expression evaluates to 0");
                break;
            case UnaryExpressionNode unaryExpr:
                GenerateExpression(unaryExpr.Operand);
                if (unaryExpr.Operator.Type == TokenType.Minus)
                {
                    AppendAsm("neg eax", "Negate value");
                }
                else
                {
                    throw new NotImplementedException($"Unsupported unary operator: {unaryExpr.Operator.Type}");
                }
                break;
            case BinaryExpressionNode binExpr:
                GenerateBinaryExpression(binExpr);
                break;
            default:
                throw new NotImplementedException($"Unsupported expression type: {expression.GetType().Name}");
        }
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        // Evaluate right-hand side first and push to stack
        GenerateExpression(binExpr.Right);
        AppendAsm("push eax");

        // Evaluate left-hand side, result is in EAX
        GenerateExpression(binExpr.Left);

        // Pop right-hand side into EBX
        AppendAsm("pop ebx");

        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus:
                AppendAsm("add eax, ebx", "+");
                break;
            case TokenType.Minus:
                AppendAsm("sub eax, ebx", "-"); // EAX = EAX - EBX
                break;

            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.LessThan:
            case TokenType.LessThanOrEqual:
            case TokenType.GreaterThan:
            case TokenType.GreaterThanOrEqual:
                AppendAsm("cmp eax, ebx");
                string setInstruction = binExpr.Operator.Type switch
                {
                    TokenType.Equal => "sete al",
                    TokenType.NotEqual => "setne al",
                    TokenType.LessThan => "setl al",
                    TokenType.LessThanOrEqual => "setle al",
                    TokenType.GreaterThan => "setg al",
                    TokenType.GreaterThanOrEqual => "setge al",
                    _ => "" // Should not happen
                };
                AppendAsm(setInstruction, binExpr.Operator.Value);
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;

            default:
                throw new NotImplementedException($"Unsupported binary operator: {binExpr.Operator.Type}");
        }
    }
}