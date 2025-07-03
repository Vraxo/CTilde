namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        return MangleName(f.Namespace, f.OwnerStructName, f.Name);
    }

    public static string Mangle(ConstructorDeclarationNode c)
    {
        return MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor{c.Parameters.Count}");
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