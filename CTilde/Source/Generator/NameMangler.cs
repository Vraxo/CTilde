using System.Linq;
using System.Text;

namespace CTilde;

public static class NameMangler
{
    public static string Mangle(FunctionDeclarationNode f)
    {
        var ownerName = f.OwnerStructName;
        // If the owner is a mangled generic name already, don't re-mangle it.
        if (ownerName != null && ownerName.Contains("__"))
        {
            return $"_{ownerName}_{f.Name}";
        }
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

        if (type is GenericInstantiationTypeNode gitn)
        {
            var sb = new StringBuilder();
            sb.Append(MangleType(new SimpleTypeNode(gitn.BaseType)));
            foreach (var arg in gitn.TypeArguments)
            {
                sb.Append(MangleType(arg));
            }
            return sb.ToString();
        }

        // TODO: Mangle generic types properly
        return "T";
    }

    public static string MangleGenericInstance(string templateFqn, List<TypeNode> concreteTypeNodes)
    {
        var sb = new StringBuilder();
        sb.Append(templateFqn.Replace("::", "__"));
        foreach (var typeNode in concreteTypeNodes)
        {
            sb.Append('_');
            sb.Append(MangleType(typeNode));
        }
        return sb.ToString();
    }


    public static string Mangle(ConstructorDeclarationNode c)
    {
        var paramSignature = string.Concat(c.Parameters.Select(p => MangleType(p.Type)));
        var ownerName = c.OwnerStructName;
        if (ownerName.Contains("__"))
        {
            return $"_{ownerName}_ctor_{paramSignature}";
        }
        return MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor_{paramSignature}");
    }

    public static string Mangle(DestructorDeclarationNode d)
    {
        var ownerName = d.OwnerStructName;
        if (ownerName.Contains("__"))
        {
            return $"_{ownerName}_dtor";
        }
        return MangleName(d.Namespace, d.OwnerStructName, $"{d.OwnerStructName}_dtor");
    }

    public static string MangleBackingField(string propertyName)
    {
        return $"__{propertyName}_BackingField";
    }

    public static string GetVTableLabel(string structFqn)
    {
        return $"_vtable_{structFqn.Replace("::", "_").Replace("<", "_").Replace(">", "").Replace("*", "p")}";
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