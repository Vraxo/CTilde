using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class FasmWriter
{
    public void WritePreamble(AssemblyBuilder builder)
    {
        builder.AppendDirective("format PE GUI 4.0");
        builder.AppendDirective("entry start");
        builder.AppendBlankLine();
        builder.AppendDirective("include 'win32a.inc'");
        builder.AppendBlankLine();
    }

    public void WriteDataSection(AssemblyBuilder builder, Dictionary<string, string> stringLiterals)
    {
        builder.AppendDirective("section '.data' data readable writeable");
        foreach (var (label, value) in stringLiterals)
        {
            builder.AppendData(label, FormatStringForFasm(value));
        }
        builder.AppendBlankLine();
    }

    public void WriteTextSectionHeader(AssemblyBuilder builder)
    {
        builder.AppendDirective("section '.text' code readable executable");
        builder.AppendBlankLine();
    }

    public void WriteEntryPoint(AssemblyBuilder builder)
    {
        builder.AppendLabel("start");
        builder.AppendInstruction("call _main");
        builder.AppendInstruction("mov ebx, eax");
        builder.AppendInstruction("push ebx");
        builder.AppendInstruction("call [ExitProcess]");
        builder.AppendBlankLine();
    }

    public void WriteImportDataSection(AssemblyBuilder builder, ProgramNode program, IEnumerable<string> externalFunctions)
    {
        builder.AppendDirective("section '.idata' import data readable");
        builder.AppendBlankLine();

        var libraries = new Dictionary<string, List<string>>
        {
            { "kernel32.dll", new List<string> { "ExitProcess" } },
            { "msvcrt.dll", new List<string> { "printf", "malloc", "free" } } // MODIFIED
        };

        foreach (var import in program.Imports)
        {
            if (!libraries.ContainsKey(import.LibraryName)) libraries[import.LibraryName] = new List<string>();
        }

        foreach (var funcName in externalFunctions)
        {
            bool found = false;
            foreach (var import in program.Imports)
            {
                if (import.LibraryName != "kernel32.dll" && import.LibraryName != "msvcrt.dll")
                {
                    libraries[import.LibraryName].Add(funcName);
                    found = true;
                    break;
                }
            }
            if (!found && funcName != "printf")
            {
                if (!libraries.ContainsKey("user32.dll")) libraries["user32.dll"] = new List<string>();
                if (!libraries["user32.dll"].Contains(funcName)) libraries["user32.dll"].Add(funcName);
            }
        }

        var libDefs = libraries.Keys.Select(lib => $"{lib.Split('.')[0]},'{lib}'");
        builder.AppendDirective($"    library {string.Join(", ", libDefs)}");
        builder.AppendBlankLine();

        foreach (var (libName, functions) in libraries)
        {
            if (functions.Count > 0)
            {
                var libAlias = libName.Split('.')[0];
                var importDefs = functions.Distinct().Select(f => $"{f},'{f}'");
                builder.AppendDirective($"    import {libAlias}, {string.Join(", ", importDefs)}");
            }
        }
    }

    private string FormatStringForFasm(string value)
    {
        var parts = new List<string>();
        var currentString = new StringBuilder();

        foreach (char c in value)
        {
            if (c is '\n' or '\t' or '\r' or '\'' or '"')
            {
                if (currentString.Length > 0)
                {
                    parts.Add($"'{currentString}'");
                    currentString.Clear();
                }
                parts.Add(((byte)c).ToString());
            }
            else currentString.Append(c);
        }

        if (currentString.Length > 0) parts.Add($"'{currentString}'");
        parts.Add("0");
        return string.Join(", ", parts);
    }
}