namespace CTilde;

public record AnalysisContext(
    SymbolTable Symbols,
    CompilationUnitNode CompilationUnit,
    FunctionDeclarationNode CurrentFunction
);