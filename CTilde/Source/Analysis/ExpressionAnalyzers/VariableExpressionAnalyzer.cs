using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class VariableExpressionAnalyzer : ExpressionAnalyzerBase
{
    public VariableExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var v = (VariableExpressionNode)expr;

        // 1. Check local variables and parameters in the symbol table.
        if (context.Symbols.TryGetSymbol(v.Identifier.Value, out _, out var type, out _))
        {
            context.Symbols.MarkAsRead(v.Identifier.Value);
            return type;
        }

        // 2. Try resolving as an unqualified enum member (e.g., `KEY_D`).
        var unqualifiedEnumValue = _functionResolver.ResolveUnqualifiedEnumMember(v.Identifier.Value, context.CompilationUnit, context.CurrentFunction?.Namespace);
        if (unqualifiedEnumValue.HasValue)
        {
            return "int";
        }

        // 3. If in a method, try resolving as an implicit `this->member`.
        if (context.CurrentFunction?.OwnerStructName is not null)
        {
            string ownerStructFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction)!;

            // Walk up the inheritance chain to find the member
            string? currentStructFqn = ownerStructFqn;
            MemberVariableNode? member = null;
            StructDefinitionNode? definingStruct = null;

            while (currentStructFqn is not null)
            {
                var structDef = _typeRepository.FindStruct(currentStructFqn);
                if (structDef is null) break;

                member = structDef.Members.FirstOrDefault(m => m.Name.Value == v.Identifier.Value);
                if (member is not null)
                {
                    definingStruct = structDef;
                    break;
                }

                if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

                var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
                var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
                currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
            }

            if (member is not null && definingStruct is not null)
            {
                context.Symbols.MarkAsRead("this");
                if (member.AccessLevel == AccessSpecifier.Private)
                {
                    if (context.CurrentFunction.OwnerStructName != definingStruct.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
                    {
                        diagnostics.Add(new Diagnostic(
                            context.CompilationUnit.FilePath,
                            $"Member '{definingStruct.Name}::{member.Name.Value}' is private and cannot be accessed from this context.",
                            v.Identifier.Line,
                            v.Identifier.Column
                        ));
                    }
                }
                var (_, memberTypeResolved) = _memoryLayoutManager.GetMemberInfo(ownerStructFqn, v.Identifier.Value, context.CompilationUnit);
                return memberTypeResolved;
            }
        }

        diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot determine type for undefined variable '{v.Identifier.Value}'.", v.Identifier.Line, v.Identifier.Column));
        return "unknown"; // Sentinel value
    }
}