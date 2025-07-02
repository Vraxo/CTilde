using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CTilde;

public class Preprocessor
{
    public string Process(string entryFilePath)
    {
        return PreprocessFile(entryFilePath, new HashSet<string>());
    }

    private string PreprocessFile(string filePath, HashSet<string> processedFiles)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath) || processedFiles.Contains(fullPath))
        {
            return string.Empty;
        }
        processedFiles.Add(fullPath);

        var codeBuilder = new StringBuilder();
        string directory = Path.GetDirectoryName(fullPath) ?? "";

        foreach (var line in File.ReadLines(fullPath))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#include"))
            {
                var startIndex = trimmedLine.IndexOf('"');
                var endIndex = trimmedLine.LastIndexOf('"');
                if (startIndex != -1 && endIndex > startIndex)
                {
                    var includeFileName = trimmedLine.Substring(startIndex + 1, endIndex - startIndex - 1);
                    var fullIncludePath = Path.Combine(directory, includeFileName);
                    codeBuilder.Append(PreprocessFile(fullIncludePath, processedFiles));
                    codeBuilder.AppendLine(); // Ensure a newline after an included file
                }
            }
            else
            {
                codeBuilder.AppendLine(line);
            }
        }
        return codeBuilder.ToString();
    }
}