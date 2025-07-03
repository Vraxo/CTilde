using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        return MangleName(f.Namespace, f.OwnerStructName, f.Name);
    }

    public static string Mangle(ConstructorDeclarationNode c)
    {
        // Mangle based on parameter types to support overloading.
        var paramTypeNames = c.Parameters.Select(p =>
        {
            // Sanitize type name for label. Replace '::' and add 'p' for pointers.
            string typeName = p.Type.Value.Replace("::", "__");
            typeName += new string('p', p.PointerLevel);
            return typeName;
        });

        string paramSignature = string.Join("_", paramTypeNames);

        // If there are no parameters, use a simple '0' to avoid a trailing underscore.
        string name = c.Parameters.Any()
            ? $"{c.OwnerStructName}_ctor_{paramSignature}"
            : $"{c.OwnerStructName}_ctor0";

        return MangleName(c.Namespace, c.OwnerStructName, name);
    }

    public static string Mangle(DestructorDeclarationNode d)
    {
        return MangleName(d.Namespace, d.OwnerStructName, $"{d.OwnerStructName}_dtor");
    }

    public static string GetVTableLabel(StructDefinitionNode s)
    {
        var segments = new List<string> { "vtable" };
        if (!string.IsNullOrEmpty(s.Namespace)) segments.Add(s.Namespace.Replace("::", "_"));
        segments.Add(s.Name);
        return "_" + string.Join("_", segments);
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
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(ns)) segments.Add(ns.Replace("::", "_"));
        if (!string.IsNullOrEmpty(owner)) segments.Add(owner);
        segments.Add(name);

        return "_" + string.Join("_", segments.Where(s => !string.IsNullOrEmpty(s)));
    }
}