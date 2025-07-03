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

        var libraries = new Dictionary<string, HashSet<string>>
        {
            { "kernel32.dll", new HashSet<string> { "ExitProcess" } },
            { "msvcrt.dll", new HashSet<string> { "printf", "malloc", "free", "strlen", "strcpy", "memcpy" } }
        };

        // Register all libraries from #import directives
        foreach (var import in program.Imports)
        {
            if (!libraries.ContainsKey(import.LibraryName))
            {
                libraries[import.LibraryName] = new HashSet<string>();
            }
        }

        // Get all functions that are already assigned to a default library
        var claimedFunctions = new HashSet<string>(libraries.SelectMany(kvp => kvp.Value));

        // Distribute all other external functions among the imported libraries
        foreach (var funcName in externalFunctions.Except(claimedFunctions))
        {
            // Assign the function to the first non-standard DLL found in the #import list.
            var ownerLib = program.Imports
                .Select(i => i.LibraryName)
                .FirstOrDefault(lib => lib != "kernel32.dll" && lib != "msvcrt.dll");

            if (ownerLib != null)
            {
                libraries[ownerLib].Add(funcName);
            }
            else // Fallback for functions if only standard libs are imported (e.g. user32 functions)
            {
                if (!libraries.ContainsKey("user32.dll")) libraries["user32.dll"] = new HashSet<string>();
                libraries["user32.dll"].Add(funcName);
            }
        }

        // Filter out libraries that have no functions to import
        var finalLibraries = libraries
            .Where(kvp => kvp.Value.Any())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());

        if (!finalLibraries.Any()) return;

        var libDefs = finalLibraries.Keys.OrderBy(k => k).Select(lib => $"{lib.Split('.')[0]},'{lib}'");
        builder.AppendDirective($"    library {string.Join(", ", libDefs)}");
        builder.AppendBlankLine();

        foreach (var (libName, functions) in finalLibraries.OrderBy(kvp => kvp.Key))
        {
            var libAlias = libName.Split('.')[0];
            functions.Sort();
            var importDefs = functions.Select(f => $"{f},'{f}'");
            builder.AppendDirective($"    import {libAlias}, {string.Join(", ", importDefs)}");
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