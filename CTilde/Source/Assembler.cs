using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CTilde;

public class Assembler
{
    public void Assemble(string relativeAsmPath)
    {
        // Define paths for FASM. A more advanced implementation could find this in PATH or via configuration.
        string? fasmPath = @"D:\Program Files\FASM\fasm.exe";
        string? fasmIncludePath = @"D:\Program Files\FASM\INCLUDE";

        if (!File.Exists(fasmPath))
        {
            Console.Error.WriteLine($"\nASSEMBLER ERROR: FASM executable not found at the expected path: '{fasmPath}'.");
            Console.Error.WriteLine("Please install FASM (flat assembler) to 'D:\\Program Files\\FASM\\' or update the path in Assembler.cs.");
            return;
        }

        if (!Directory.Exists(fasmIncludePath))
        {
            Console.Error.WriteLine($"\nASSEMBLER ERROR: FASM include directory not found at the expected path: '{fasmIncludePath}'.");
            return;
        }

        try
        {
            Console.WriteLine($"\nExecuting FASM assembler...");

            string? fullAsmPath = Path.GetFullPath(relativeAsmPath);
            string? workingDirectory = Path.GetDirectoryName(fullAsmPath) ?? ".";

            // The fasm command needs the output file path relative to its working directory.
            // Since we set the working directory to the same folder as the asm file, we only need the filename.
            string? fasmArgument = Path.GetFileName(fullAsmPath);

            ProcessStartInfo? startInfo = StartAssemblerProcess(fasmPath, workingDirectory, fasmArgument);

            // Set the INCLUDE environment variable specifically for the FASM process
            startInfo.EnvironmentVariables["INCLUDE"] = fasmIncludePath;

            using Process? process = new() { StartInfo = startInfo };
            StringBuilder? outputBuilder = new();
            StringBuilder? errorBuilder = new();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data is null) return;
                outputBuilder.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data is not null) errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            Console.Write(outputBuilder.ToString());
            Console.Error.Write(errorBuilder.ToString());

            if (process.ExitCode == 0)
            {
                Console.WriteLine("FASM execution successful.");
            }
            else
            {
                Console.Error.WriteLine($"FASM execution failed with exit code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing FASM: {ex.Message}");
        }
    }

    private static ProcessStartInfo StartAssemblerProcess(string fasmPath, string workingDirectory, string fasmArgument)
    {
        return new()
        {
            FileName = fasmPath,
            Arguments = fasmArgument,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
    }
}