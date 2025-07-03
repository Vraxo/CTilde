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

        // Define standard libraries and their common functions
        var libraries = new Dictionary<string, List<string>>
        {
            { "kernel32.dll", new List<string> { "ExitProcess" } },
            { "msvcrt.dll", new List<string> { "printf", "malloc", "free", "strlen", "strcpy", "memcpy" } }
        };

        // Get a list of all user-imported libraries from #import directives
        var userLibs = program.Imports.Select(i => i.LibraryName).ToList();

        // Add user-imported libraries to our main dictionary
        foreach (var lib in userLibs)
        {
            if (!libraries.ContainsKey(lib))
            {
                libraries[lib] = new List<string>();
            }
        }

        // Determine the primary user library (heuristic: the first one that isn't standard)
        var primaryUserLib = userLibs.FirstOrDefault(lib => lib != "kernel32.dll" && lib != "msvcrt.dll");

        // Get lists of functions we know belong to standard libraries
        var kernel32Funcs = libraries["kernel32.dll"];
        var msvcrtFuncs = libraries["msvcrt.dll"];

        // Distribute all other external functions to the primary user library
        if (primaryUserLib != null)
        {
            foreach (var funcName in externalFunctions)
            {
                // If the function isn't a known standard library function, assign it to the user's library.
                if (!kernel32Funcs.Contains(funcName) && !msvcrtFuncs.Contains(funcName))
                {
                    libraries[primaryUserLib].Add(funcName);
                }
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