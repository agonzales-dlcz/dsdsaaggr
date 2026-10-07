using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Riga.InyectorDatos.Helpers
{
    public static class CsvParserHelper
    {
        /// <summary>
        /// Parses a CSV file robustly, handling commas inside quotes.
        /// Returns a list of dictionaries, where the dictionary keys are the column headers.
        /// </summary>
        public static List<Dictionary<string, string>> Parse(string filepath)
        {
            var result = new List<Dictionary<string, string>>();

            if (!File.Exists(filepath))
            {
                return result;
            }

            var lines = File.ReadAllLines(filepath);
            if (lines.Length == 0)
            {
                return result;
            }

            // Regex to split on commas not inside quotes
            var csvSplitRegex = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");

            // Extract headers
            var headers = csvSplitRegex.Split(lines[0])
                .Select(h => CleanCsvValue(h))
                .ToList();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = csvSplitRegex.Split(line);
                var row = new Dictionary<string, string>();

                for (int j = 0; j < headers.Count; j++)
                {
                    string header = headers[j];
                    string value = j < values.Length ? CleanCsvValue(values[j]) : string.Empty;
                    row[header] = value;
                }

                result.Add(row);
            }

            return result;
        }

        private static string CleanCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            // Remove wrapping quotes if they exist and unescape double quotes
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }
            return value.Replace("\"\"", "\"").Trim();
        }
    }
}
