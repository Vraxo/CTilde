namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        return MangleName(f.Namespace, f.OwnerStructName, f.Name);
    }

    private static string MangleType(TypeNode type)
    {
        if (type is PointerTypeNode ptn)
        {
            return "p" + MangleType(ptn.BaseType);
        }

        if (type is SimpleTypeNode stn)
        {
            var typeToken = stn.TypeToken;
            if (typeToken.Type == TokenType.Keyword)
            {
                return typeToken.Value[0].ToString();
            }
            else // Identifier, could be qualified
            {
                var cleanName = typeToken.Value.Replace("::", "_");
                return $"{cleanName.Length}{cleanName}";
            }
        }

        // TODO: Mangle generic types properly
        return "T";
    }

    public static string Mangle(ConstructorDeclarationNode c)
    {
        var paramSignature = string.Concat(c.Parameters.Select(p => MangleType(p.Type)));
        return MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor_{paramSignature}");
    }

    public static string Mangle(DestructorDeclarationNode d)
    {
        return MangleName(d.Namespace, d.OwnerStructName, $"{d.OwnerStructName}_dtor");
    }

    public static string GetVTableLabel(string structFqn)
    {
        return $"_vtable_{structFqn.Replace("::", "_")}";
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
        var parts = new List<string?> { ns, owner, name }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!.Replace("::", "_"));

        return $"_{string.Join("_", parts)}";
    }
}