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
        var varName = v.Identifier.Value;

        // 1. Check for special property keywords: 'value' and 'field'
        if (varName == "field" || varName == "value")
        {
            var accessor = v.FindAncestorOfType<PropertyAccessorNode>();
            if (accessor == null)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"The '{varName}' keyword can only be used inside a property accessor.", v.Identifier.Line, v.Identifier.Column));
                return "unknown";
            }

            if (varName == "value" && accessor.AccessorKeyword.Value != "set")
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, "The 'value' keyword can only be used inside a 'set' accessor.", v.Identifier.Line, v.Identifier.Column));
                return "unknown";
            }

            // The type is the type of the property itself. Find it by walking up the tree.
            var property = accessor.FindAncestorOfType<PropertyDefinitionNode>()!;
            var structDef = property.FindAncestorOfType<StructDefinitionNode>()!;
            return _typeResolver.ResolveType(property.Type, structDef.Namespace, context.CompilationUnit);
        }

        // 2. Check local variables and parameters in the symbol table.
        if (context.Symbols.TryGetSymbol(v.Identifier.Value, out _, out var type, out _))
        {
            context.Symbols.MarkAsRead(v.Identifier.Value);
            return type;
        }

        // 3. Try resolving as an unqualified enum member (e.g., `KEY_D`).
        var unqualifiedEnumValue = _functionResolver.ResolveUnqualifiedEnumMember(v.Identifier.Value, context.CompilationUnit, context.CurrentFunction?.Namespace);
        if (unqualifiedEnumValue.HasValue)
        {
            return "int";
        }

        // 4. If in a method, try resolving as an implicit `this->member`.
        if (context.CurrentFunction?.OwnerStructName != null)
        {
            string ownerStructFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction)!;

            // Walk up the inheritance chain to find the member
            string? currentStructFqn = ownerStructFqn;
            MemberVariableNode? member = null;
            StructDefinitionNode? definingStruct = null;

            while (currentStructFqn != null)
            {
                var structDef = _typeRepository.FindStruct(currentStructFqn);
                if (structDef == null) break;

                member = structDef.Members.FirstOrDefault(m => m.Name.Value == v.Identifier.Value);
                if (member != null)
                {
                    definingStruct = structDef;
                    break;
                }

                if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

                var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
                var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
                currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
            }

            if (member != null && definingStruct != null)
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