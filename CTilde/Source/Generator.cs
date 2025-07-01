using System;
using System.Text;

namespace CTilde;

public class Generator
{
    private readonly ProgramNode _program;
    private readonly StringBuilder _sb = new();
    private int _labelCounter;

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
        _sb.AppendLine("    library kernel32,'kernel32.dll'");
        _sb.AppendLine("    import kernel32, ExitProcess,'ExitProcess'");

        return _sb.ToString();
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _sb.AppendLine($"{function.Name}:");
        _sb.AppendLine("    push ebp");
        _sb.AppendLine("    mov ebp, esp");
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
            default:
                throw new NotImplementedException($"Unsupported statement type: {statement.GetType().Name}");
        }
    }

    private void GenerateWhile(WhileStatementNode whileStmt)
    {
        int labelIndex = _labelCounter++;
        string startLabel = $"_while_start_{labelIndex}";
        string endLabel = $"_while_end_{labelIndex}";

        _sb.AppendLine($"{startLabel}:");

        GenerateExpression(whileStmt.Condition);
        _sb.AppendLine("    cmp eax, 0");
        _sb.AppendLine($"    je {endLabel}");

        GenerateStatement(whileStmt.Body);

        _sb.AppendLine($"    jmp {startLabel}");

        _sb.AppendLine($"{endLabel}:");
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        GenerateExpression(ret.Expression);
        _sb.AppendLine("    mov esp, ebp");
        _sb.AppendLine("    pop ebp");
        _sb.AppendLine("    ret");
    }

    private void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal:
                _sb.AppendLine($"    mov eax, {literal.Value}");
                break;
            default:
                throw new NotImplementedException($"Unsupported expression type: {expression.GetType().Name}");
        }
    }
}