using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
namespace ConflictChat.Classes
{


    public static class CsvHelper
    {
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


    }
}
