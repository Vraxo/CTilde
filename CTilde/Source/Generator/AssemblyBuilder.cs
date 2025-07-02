using System.Text;

namespace CTilde;

public class AssemblyBuilder
{
    private readonly StringBuilder _sb = new();

    public void AppendDirective(string directive)
    {
        _sb.AppendLine(directive);
    }

    public void AppendLabel(string label)
    {
        _sb.AppendLine($"{label}:");
    }

    public void AppendInstruction(string? instruction, string? comment = null)
    {
        var line = instruction == null ? "" : $"    {instruction}";
        _sb.AppendLine(line.PadRight(35) + (comment == null ? "" : $"; {comment}"));
    }

    public void AppendData(string label, string value)
    {
        _sb.AppendLine($"    {label} db {value}");
    }

    public void AppendBlankLine()
    {
        _sb.AppendLine();
    }

    public override string ToString()
    {
        return _sb.ToString();
    }
}