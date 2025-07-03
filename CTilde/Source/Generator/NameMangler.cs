namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        return MangleName(f.Namespace, f.OwnerStructName, f.Name);
    }

    private static string MangleType(Token type, int pointerLevel)
    {
        var typeName = type.Value;
        string mangled;
        if (type.Type == TokenType.Keyword)
        {
            mangled = typeName[0].ToString();
        }
        else // Identifier, could be qualified
        {
            var cleanName = typeName.Replace("::", "_");
            mangled = $"{cleanName.Length}{cleanName}";
        }

        for (int i = 0; i < pointerLevel; i++)
        {
            mangled = "p" + mangled;
        }
        return mangled;
    }

    public static string Mangle(ConstructorDeclarationNode c)
    {
        var paramSignature = string.Concat(c.Parameters.Select(p => MangleType(p.Type, p.PointerLevel)));
        return MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor_{paramSignature}");
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
        var parts = new List<string?> { ns, owner, name }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!.Replace("::", "_"));

        return $"_{string.Join("_", parts)}";
    }
}