﻿using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class StringLiteralAnalyzer : ExpressionAnalyzerBase
{
    public StringLiteralAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        return "char*"; // String literals are char pointers
    }
}