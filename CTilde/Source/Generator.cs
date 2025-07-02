using System;
using System.Collections.Generic;
using System.Linq;
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
        _sb.AppendLine("    call _main");
        _sb.AppendLine("    mov ebx, eax"); // EAX holds the return value from main
        _sb.AppendLine("    push ebx");
        _sb.AppendLine("    call [ExitProcess]");
        _sb.AppendLine();

        foreach (var function in _program.Functions)
        {
            GenerateFunction(function);
            _sb.AppendLine();
        }

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

        _sb.AppendLine($"_{function.Name}:");
        AppendAsm("push ebp");
        AppendAsm("mov ebp, esp");
        _sb.AppendLine();

        // Map parameters to their stack locations.
        // Stack layout: [ebp] = old ebp, [ebp+4] = return address
        // First parameter is at [ebp+8], second at [ebp+12], etc.
        int paramOffset = 8;
        foreach (var param in function.Parameters)
        {
            _variables[param.Name.Value] = paramOffset;
            paramOffset += 4; // Assuming all params are 4 bytes.
        }

        GenerateStatement(function.Body);

        // A function might not have an explicit return (e.g. a void function).
        // To be safe, always add the instructions to clean up the stack frame and return.
        _sb.AppendLine();
        AppendAsm("mov esp, ebp", "Implicit return cleanup");
        AppendAsm("pop ebp");
        AppendAsm("ret");
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
                if (exprStmt.Expression is CallExpressionNode)
                {
                    // For expression statements that are calls, the EAX return value is discarded,
                    // but the stack cleanup must still have happened. No extra ASM needed here.
                }
                else
                {
                    AppendAsm("", "; Expression statement result in EAX is discarded");
                }
                break;
            default:
                throw new NotImplementedException($"Unsupported statement type: {statement.GetType().Name}");
        }
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

    private void GenerateIf(IfStatementNode ifStmt)
    {
        int labelIndex = _labelCounter++;
        string elseLabel = $"_if_else_{labelIndex}";
        string endLabel = $"_if_end_{labelIndex}";

        GenerateExpression(ifStmt.Condition);
        AppendAsm("cmp eax, 0");

        if (ifStmt.ElseBody != null)
        {
            AppendAsm($"je {elseLabel}");
        }
        else
        {
            AppendAsm($"je {endLabel}");
        }

        GenerateStatement(ifStmt.ThenBody);

        if (ifStmt.ElseBody != null)
        {
            AppendAsm($"jmp {endLabel}");
            _sb.AppendLine($"{elseLabel}:");
            GenerateStatement(ifStmt.ElseBody);
        }

        _sb.AppendLine($"{endLabel}:");
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null)
        {
            GenerateExpression(ret.Expression);
        }
        // For a void return, EAX is not explicitly set. The caller should not use it.
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
                string sign = offset > 0 ? "+" : "";
                AppendAsm($"mov eax, [ebp {sign} {offset}]", $"Load variable/param {varExpr.Identifier.Value}");
                break;
            case AssignmentExpressionNode assignExpr:
                if (!_variables.TryGetValue(assignExpr.Identifier.Value, out var assignOffset))
                {
                    throw new InvalidOperationException($"Undeclared variable '{assignExpr.Identifier.Value}' for assignment.");
                }
                GenerateExpression(assignExpr.Value);
                string assignSign = assignOffset > 0 ? "+" : "";
                AppendAsm($"mov [ebp {assignSign} {assignOffset}], eax", $"Assign to variable/param {assignExpr.Identifier.Value}");
                break;
            case BinaryExpressionNode binExpr:
                GenerateBinaryExpression(binExpr);
                break;
            case CallExpressionNode callExpr:
                GenerateCallExpression(callExpr);
                break;
            default:
                throw new NotImplementedException($"Unsupported expression type: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        // Push arguments onto the stack from right to left (cdecl)
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            GenerateExpression(arg);
            AppendAsm("push eax");
        }

        if (callExpr.Callee.Value == "print")
        {
            if (callExpr.Arguments.Count != 1)
                throw new InvalidOperationException("print() intrinsic requires exactly one argument.");

            AppendAsm("push format_int", "Push format string for printf");
            AppendAsm("call [printf]");
            // Caller cleans up stack: 1 arg + 1 format string = 8 bytes
            AppendAsm("add esp, 8", "Clean up stack for printf");
            AppendAsm("mov eax, 0", "A print expression evaluates to 0");
        }
        else
        {
            AppendAsm($"call _{callExpr.Callee.Value}");
            // Caller cleans up the stack
            if (callExpr.Arguments.Count > 0)
            {
                int bytesToClean = callExpr.Arguments.Count * 4;
                AppendAsm($"add esp, {bytesToClean}", $"Clean up {callExpr.Arguments.Count} args from stack");
            }
            // Return value is in EAX, as per convention.
        }
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        // Generate right operand first, push to stack
        GenerateExpression(binExpr.Right);
        AppendAsm("push eax");

        // Generate left operand, result is in EAX
        GenerateExpression(binExpr.Left);

        // Pop right operand into ECX
        AppendAsm("pop ecx");

        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus:
                AppendAsm("add eax, ecx", "eax = eax + ecx");
                break;
            case TokenType.Minus:
                AppendAsm("sub eax, ecx", "eax = eax - ecx");
                break;
            case TokenType.Star:
                AppendAsm("imul eax, ecx", "eax = eax * ecx");
                break;
            case TokenType.Slash:
                AppendAsm("cdq", "Sign-extend EAX into EDX:EAX");
                AppendAsm("idiv ecx", "eax = edx:eax / ecx");
                break;
            case TokenType.DoubleEquals:
                AppendAsm("cmp eax, ecx");
                AppendAsm("sete al", "Set AL if equal");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.NotEquals:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setne al", "Set AL if not equal");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.LessThan:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setl al", "Set AL if less");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.GreaterThan:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setg al", "Set AL if greater");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            default:
                throw new NotImplementedException($"Unsupported binary operator: {binExpr.Operator.Type}");
        }
    }
}