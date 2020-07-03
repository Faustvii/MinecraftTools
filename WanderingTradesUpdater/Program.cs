using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WanderingTradesUpdater
{
    class Program {
        static void Main(string[] args) {
            var lines = File.ReadAllLines("add_trade.mcfunction");
            var entries = lines.Count(x => x.StartsWith("execute if score @s wt_tradeIndex matches"));
            var newLines = new List<string>();
            var currentIndex = 2;
            var regexMatcher = new Regex(@"execute if score @s wt_tradeIndex matches (\d+)");
            
            foreach (var line in lines) {
                var newLine = line;
                if (regexMatcher.IsMatch(line)) {
                    var regResult = regexMatcher.Match(line);
                    var currentLineIndex = regResult.Groups[1].Value;
                    newLine = newLine.Replace($"execute if score @s wt_tradeIndex matches {currentLineIndex}", $"execute if score @s wt_tradeIndex matches {currentIndex}");
                    currentIndex++;
                }
                newLines.Add(newLine);
            }

            File.WriteAllLines("out/test.txt", newLines);

        }
    }
}