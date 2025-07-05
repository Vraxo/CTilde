using System; // Added for InvalidOperationException
using System.Collections.Generic; // Added for List
using CTilde.Diagnostics; // Added for Diagnostic

namespace CTilde.Analysis.ExpressionAnalyzers; // Corrected namespace

public class QualifiedAccessExpressionAnalyzer : ExpressionAnalyzerBase // Corrected class name
{
    public QualifiedAccessExpressionAnalyzer( // Corrected constructor name
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var q = (QualifiedAccessExpressionNode)expr;

        string qualifier = TypeResolver.ResolveQualifier(q.Left);
        string memberName = q.Member.Value;

        // 1. Try to resolve as an enum member (e.g., raylib::KeyboardKey::KEY_D)
        string? enumTypeFQN = _typeResolver.ResolveEnumTypeName(qualifier, context.CurrentFunction?.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            if (_functionResolver.GetEnumValue(enumTypeFQN, memberName).HasValue)
            {
                return "int"; // Enum members are integers.
            }
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Enum '{qualifier}' (resolved to '{enumTypeFQN}') does not contain member '{memberName}'.", q.Member.Line, q.Member.Column));
            return "unknown";
        }

        // 2. Try to resolve as a qualified function (e.g., rl::InitWindow)
        // If it's not an enum, but a qualified *function* name, its type is effectively a function pointer.
        // We resolve it here, but its "type" for now is just void*.
        try
        {
            _functionResolver.ResolveFunctionCall(q, context);
            return "void*"; // Represents a function pointer type
        }
        catch (InvalidOperationException)
        {
            // Not an enum member, not a function. It's an error.
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Qualified access '{qualifier}::{memberName}' cannot be evaluated as a value directly. Only enum members or function calls are supported.", q.Member.Line, q.Member.Column));
            return "unknown";
        }
    }
}