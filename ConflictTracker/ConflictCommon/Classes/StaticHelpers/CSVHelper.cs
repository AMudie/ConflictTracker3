using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
namespace ConflictCommon.Classes.StaticHelpers
{


    public static class CsvHelper
    {
        #region "Reading"
        public static IEnumerable<Dictionary<string, string>> ReadCsv(string pathOrResource)
        {
            Stream? stream = null;

            // Try to open as a file path first
            if (File.Exists(pathOrResource))
            {
                stream = File.OpenRead(pathOrResource);
            }
            else
            {
                // Try to open as an embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                stream = assembly.GetManifestResourceStream(pathOrResource);

                if (stream == null)
                    throw new FileNotFoundException(
                        $"Neither a file nor an embedded resource was found for: {pathOrResource}");
            }

            using var reader = new StreamReader(stream);

            // Read header
            string? headerLine = reader.ReadLine();
            if (headerLine == null)
                yield break;

            string[] headers = headerLine.Split(',');

            // Read each row
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] values = line.Split(',');

                var row = new Dictionary<string, string>();

                for (int i = 0; i < headers.Length; i++)
                {
                    string key = headers[i];
                    string value = i < values.Length ? values[i] : "";
                    row[key] = value.Replace(Environment.NewLine, "").Trim() ;
                }

                yield return row;
            }
        }

        public static async IAsyncEnumerable<Dictionary<string, string>> ReadCsvAsync(string pathOrResource)
        {
            Stream? stream = null;

            // Try to open as a file path first
            if (File.Exists(pathOrResource))
            {
                stream = File.OpenRead(pathOrResource);
            }
            else
            {
                // Try to open as an embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                stream = assembly.GetManifestResourceStream(pathOrResource);

                if (stream == null)
                    throw new FileNotFoundException(
                        $"Neither a file nor an embedded resource was found for: {pathOrResource}");
            }

            using var reader = new StreamReader(stream);

            // Read header line asynchronously
            string? headerLine = await reader.ReadLineAsync();
            if (headerLine == null)
                yield break;

            string[] headers = headerLine.Split(',');

            // Read each row asynchronously
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null)
                    yield break;

                string[] values = line.Split(',');

                var row = new Dictionary<string, string>();

                for (int i = 0; i < headers.Length; i++)
                {
                    string key = headers[i];
                    string value = i < values.Length ? values[i] : "";
                    row[key] = value;
                }

                yield return row;
            }
        }
        #endregion

        #region "Writing"
        public static void WriteToCSV(List<Dictionary<string, string>> dataset, string filePath)
        {
            if (dataset == null || dataset.Count == 0)
                throw new ArgumentException("Dataset is empty.");

            // Extract column headers from the union of all keys
            var headers = dataset
                .SelectMany(d => d.Keys)
                .Distinct()
                .ToList();

            using (var writer = new StreamWriter(filePath))
            {
                // Write header row
                writer.WriteLine(string.Join(",", headers));

                // Write each row
                foreach (var row in dataset)
                {
                    var values = headers.Select(h =>
                    {
                        row.TryGetValue(h, out var value);
                        return EscapeCsv(value ?? "");
                    });

                    writer.WriteLine(string.Join(",", values));
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
                return "";

            bool mustQuote =
                value.Contains(",") ||
                value.Contains("\"") ||
                value.Contains("\n") ||
                value.Contains("\r");

            if (mustQuote)
            {
                // Escape quotes by doubling them
                value = value.Replace("\"", "\"\"");
                return $"\"{value}\"";
            }

            return value;
        }
        #endregion
    }
}
