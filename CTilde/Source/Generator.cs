using System;
using System.Text;

namespace CTilde;

public class Generator
{
    private readonly ProgramNode _program;
    private readonly StringBuilder _sb = new();

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

        if (function.Body is BlockStatementNode block)
        {
            foreach (var stmt in block.Statements)
            {
                GenerateStatement(stmt);
            }
        }
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatementNode ret:
                GenerateReturn(ret);
                break;
            default:
                throw new NotImplementedException($"Unsupported statement type: {statement.GetType().Name}");
        }
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        switch (ret.Expression)
        {
            case IntegerLiteralNode literal:
                _sb.AppendLine($"    mov eax, {literal.Value}");
                _sb.AppendLine("    mov esp, ebp");
                _sb.AppendLine("    pop ebp");
                _sb.AppendLine("    ret");
                break;
            default:
                throw new NotImplementedException($"Unsupported expression in return: {ret.Expression.GetType().Name}");
        }
    }
}