namespace CTilde;

public class Preprocessor
{
    public List<string> DiscoverDependencies(string entryFilePath)
    {
        var allFiles = new List<string>();
        var processedFiles = new HashSet<string>();
        var filesToProcess = new Queue<string>();

        filesToProcess.Enqueue(Path.GetFullPath(entryFilePath));

        while (filesToProcess.Count > 0)
        {
            var currentFile = filesToProcess.Dequeue();
            if (!File.Exists(currentFile) || processedFiles.Contains(currentFile))
            {
                continue;
            }

            processedFiles.Add(currentFile);
            allFiles.Add(currentFile);

            string directory = Path.GetDirectoryName(currentFile) ?? "";

            foreach (var line in File.ReadLines(currentFile))
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
                        filesToProcess.Enqueue(fullIncludePath);
                    }
                }
            }
        }

        // The order matters for parsing: dependencies should come first.
        // We reverse because the entry point was added first.
        allFiles.Reverse();
        return allFiles;
    }
}