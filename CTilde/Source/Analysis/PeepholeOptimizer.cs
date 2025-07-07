using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde.Analysis;

/// <summary>
/// Performs simple pattern-based optimizations on the final assembly code.
/// </summary>
public class PeepholeOptimizer
{
    public string Optimize(string asmCode, OptimizationLogger? logger)
    {
        var lines = asmCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

        bool changed;
        do
        {
            changed = false;
            changed |= RemoveRedundantPushPop(lines, logger);
            changed |= CoalesceAddEsp(lines, logger);
        } while (changed);

        return string.Join(Environment.NewLine, lines);
    }

    private bool RemoveRedundantPushPop(List<string> lines, OptimizationLogger? logger)
    {
        bool changed = false;
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var line1 = lines[i].Trim();
            var line2 = lines[i + 1].Trim();

            if (!line1.StartsWith("push ") || !line2.StartsWith("pop ")) continue;

            var reg1 = line1.Substring(4).Trim();
            var reg2 = line2.Substring(3).Trim();

            if (reg1 == reg2)
            {
                logger?.Log(
                    "Peephole: Redundant Push/Pop",
                    $"L{i + 1}: {line1} / L{i + 2}: {line2}",
                    "Removed both lines.",
                    "Assembly"
                );

                lines.RemoveAt(i + 1);
                lines.RemoveAt(i);
                i--;
                changed = true;
            }
        }
        return changed;
    }

    private bool CoalesceAddEsp(List<string> lines, OptimizationLogger? logger)
    {
        bool changed = false;
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var line1 = lines[i].Trim();
            var line2 = lines[i + 1].Trim();

            if (!line1.StartsWith("add esp,") || !line2.StartsWith("add esp,")) continue;

            var val1Str = line1.Substring("add esp,".Length).Trim().Split(';')[0].Trim();
            var val2Str = line2.Substring("add esp,".Length).Trim().Split(';')[0].Trim();

            if (int.TryParse(val1Str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val1) &&
                int.TryParse(val2Str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val2))
            {
                int combinedValue = val1 + val2;
                string indentation = lines[i].Substring(0, lines[i].IndexOf("add"));
                string comment = $"; Clean up stack (coalesced from {val1} + {val2})";
                string combinedLine = $"{indentation}add esp, {combinedValue}".PadRight(35) + comment;

                logger?.Log(
                    "Peephole: Coalesce ESP Additions",
                    $"L{i + 1}: {line1} / L{i + 2}: {line2}",
                    $"L{i + 1}: {combinedLine.Trim()}",
                    "Assembly"
                );

                lines[i] = combinedLine;
                lines.RemoveAt(i + 1);
                i--;
                changed = true;
            }
        }
        return changed;
    }
}