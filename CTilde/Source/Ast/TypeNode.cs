using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public abstract record TypeNode : AstNode
{
    public abstract Token GetFirstToken();
    public abstract string GetBaseTypeName();
    public abstract int GetPointerLevel();
}

public record SimpleTypeNode(Token TypeToken) : TypeNode
{
    public override string ToString() => TypeToken.Value;
    public override Token GetFirstToken() => TypeToken;
    public override string GetBaseTypeName() => TypeToken.Value;
    public override int GetPointerLevel() => 0;
}

public record PointerTypeNode(TypeNode BaseType) : TypeNode
{
    public override string ToString() => $"{BaseType}*";
    public override Token GetFirstToken() => BaseType.GetFirstToken();
    public override string GetBaseTypeName() => BaseType.GetBaseTypeName();
    public override int GetPointerLevel() => BaseType.GetPointerLevel() + 1;
}

public record GenericInstantiationTypeNode(Token BaseType, List<TypeNode> TypeArguments) : TypeNode
{
    public override string ToString() => $"{BaseType.Value}<{string.Join(", ", TypeArguments.Select(a => a.ToString()))}>";
    public override Token GetFirstToken() => BaseType;
    public override string GetBaseTypeName() => BaseType.Value;
    public override int GetPointerLevel() => 0;
}