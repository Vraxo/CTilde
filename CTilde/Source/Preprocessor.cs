using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CTilde;

public class Preprocessor
{
    public List<string> DiscoverDependencies(string entryFilePath)
    {
        var finalOrder = new List<string>();
        var visited = new HashSet<string>();

        DiscoverRec(Path.GetFullPath(entryFilePath), visited, finalOrder);

        return finalOrder;
    }

    private void DiscoverRec(string currentFilePath, HashSet<string> visited, List<string> finalOrder)
    {
        if (!File.Exists(currentFilePath) || visited.Contains(currentFilePath))
        {
            return;
        }

        visited.Add(currentFilePath);

        var directory = Path.GetDirectoryName(currentFilePath) ?? "";
        var includes = new List<string>();

        foreach (var line in File.ReadLines(currentFilePath))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#include"))
            {
                var startIndex = trimmedLine.IndexOf('"');
                var endIndex = trimmedLine.LastIndexOf('"');
                if (startIndex != -1 && endIndex > startIndex)
                {
                    var includeFileName = trimmedLine.Substring(startIndex + 1, endIndex - startIndex - 1);
                    var fullIncludePath = Path.GetFullPath(Path.Combine(directory, includeFileName));
                    includes.Add(fullIncludePath);
                }
            }
        }

        // Recursively visit all dependencies first
        foreach (var includePath in includes)
        {
            DiscoverRec(includePath, visited, finalOrder);
        }

        // Add the current file to the list only AFTER all its dependencies have been added
        finalOrder.Add(currentFilePath);
    }
}