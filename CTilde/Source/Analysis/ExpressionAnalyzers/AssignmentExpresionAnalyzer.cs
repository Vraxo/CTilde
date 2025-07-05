using System;
using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class AssignmentExpressionAnalyzer : ExpressionAnalyzerBase
{
    public AssignmentExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var a = (AssignmentExpressionNode)expr;

        var leftType = _semanticAnalyzer.AnalyzeExpressionType(a.Left, context, diagnostics);
        var rightType = _semanticAnalyzer.AnalyzeExpressionType(a.Right, context, diagnostics);

        // Allow int to pointer conversion (for malloc etc)
        bool isIntToPointerConversion = leftType.EndsWith("*") && rightType == "int";
        // Allow int literal to char conversion
        bool isIntToCharLiteralConversion = leftType == "char" && rightType == "int" && a.Right is IntegerLiteralNode;
        // HACK: Allow assignments to/from a generic type parameter inside a monomorphized method.
        // This happens because the analyzer sometimes resolves a member to `T` and a parameter to `ConcreteType`.
        bool isGenericAssignment = (leftType.Length == 1 && char.IsUpper(leftType[0])) || (rightType.Length == 1 && char.IsUpper(rightType[0]));

        if (rightType != "unknown" && leftType != "unknown" && leftType != rightType && !isIntToPointerConversion && !isIntToCharLiteralConversion && !isGenericAssignment)
        {
            var token = AstHelper.GetFirstToken(a.Right);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot implicitly convert type '{rightType}' to '{leftType}'.", token.Line, token.Column));
        }

        return leftType; // Type of assignment is type of l-value
    }
}