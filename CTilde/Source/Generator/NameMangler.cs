using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        return MangleName(f.Namespace, f.OwnerStructName, f.Name);
    }

    private static string CreateConstructorSignature(IEnumerable<ParameterNode> parameters)
    {
        if (!parameters.Any()) return "void";

        var typeSignatures = parameters.Select(p =>
        {
            var rawTypeName = TypeRepository.GetTypeNameFromToken(p.Type, p.PointerLevel);
            // Sanitize for assembly label
            return rawTypeName.Replace("::", "_").Replace("*", "p");
        });
        return string.Join("_", typeSignatures);
    }

    public static string Mangle(ConstructorDeclarationNode c)
    {
        var signature = CreateConstructorSignature(c.Parameters);
        return MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor_{signature}");
    }

    public static string Mangle(DestructorDeclarationNode d)
    {
        return MangleName(d.Namespace, d.OwnerStructName, $"{d.OwnerStructName}_dtor");
    }

    public static string GetVTableLabel(StructDefinitionNode s)
    {
        return $"_vtable_{s.Namespace?.Replace("::", "_")}_{s.Name}".Replace("__", "_");
    }

    public static string MangleOperator(string op)
    {
        return op switch
        {
            "+" => "plus",
            _ => throw new System.NotImplementedException($"Operator mangling for '{op}' is not implemented.")
        };
    }

    private static string MangleName(string? ns, string? owner, string name)
    {
        return $"_{ns?.Replace("::", "_")}_{owner}_{name}".Replace("___", "_").Replace("__", "_");
    }
}