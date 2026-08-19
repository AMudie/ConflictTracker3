using ConflictCalc.Enumerations;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.Reflection;
using System.Text;

using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using ConflictCommon.Classes.StaticHelpers;
using Microsoft.Recognizers.Text.NumberWithUnit.Chinese;

namespace ConflictCalc.StaticHelpers
{
    internal static class RegressionMLHelper
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="placeName"></param>
        /// <param name="v">Algorithm</param>
        /// <param name="timespan"></param>
        /// <param name="metric"></param>
        /// <example>buildmodel -filepath "C:\Users\andre\source\repos\ConflictTracker2\ConflictTracker\ConflictBI\Sudan_Test2000_18July2026.csv" -placename Khartoum -algorithm SDCARegression -metric LocalTotalFatalities -time OneMonth</example>
        internal static void BuildModel(string filePath, string placeName, object? v, TimePrediction timespan, ConflictDefinition metric)
        {

            string preprocessedPath = PreprocessDataset(filePath, timespan, metric, v);

            FileHelper.OpenFile(preprocessedPath); //open the file, just for the developer to see the changes. 


        }

        private static string PreprocessDataset(string filePath, TimePrediction timespan, ConflictDefinition metric, object? algorithm)
        {

            if (File.Exists(filePath))
            {

                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var directory = Path.GetDirectoryName(assemblyPath);
                string filePathCopy = Path.Combine( Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_Preprocessed" + Path.GetExtension(filePath));
                File.Copy(filePath, filePathCopy, true);

                RemoveSpecificColumns(filePathCopy, new List<String> { "latitude", "longitude", "periodEnd", "PlacesWithinRegion" });

                RemoveConstantAndUniqueColumns(filePathCopy);

                RobustScaleNumericColumns(filePathCopy);

                if ((algorithm) is MLAlgorithmRegression || (algorithm is MLAlgorithmBinaryClassification))
                {
                    ApplyClassicalMLPredictorColumn(filePathCopy, metric);
                }
                else if ((algorithm) is MLAlgorithmTimeSeries)
                {
                    ApplyTimeSeriesMLPredictorColumn(filePathCopy, timespan, metric);
                }
                else
                {
                    throw new ArgumentException($"Specified algorithm {algorithm} is not valid.");
                }



                return filePathCopy;
            }



            return string.Empty;
        }


        private static void RemoveSpecificColumns(string filePath, List<string> columnsToRemove)
        {
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                List<dynamic> rows;

                // Read CSV
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    rows = csv.GetRecords<dynamic>().ToList();
                }

                if (rows.Count == 0)
                    return; // nothing to clean

                // Convert dynamic rows to dictionaries
                var dictRows = rows
                    .Select(r => (IDictionary<string, object>)r)
                    .ToList();



                // Remove columns
                foreach (var row in dictRows)
                {
                    foreach (var col in columnsToRemove)
                        row.Remove(col);
                }

                // Write cleaned CSV back to file
                using (var writer = new StreamWriter(filePath))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    // Write header
                    foreach (var col in dictRows.First().Keys)
                        csv.WriteField(col);
                    csv.NextRecord();

                    // Write rows
                    foreach (var row in dictRows)
                    {
                        foreach (var col in row.Values)
                            csv.WriteField(col);
                        csv.NextRecord();
                    }
                }
            }
        }

        private static void RemoveConstantAndUniqueColumns(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            List<dynamic> rows;

            // Read CSV
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                rows = csv.GetRecords<dynamic>().ToList();
            }

            if (rows.Count == 0)
                return; // nothing to clean

            // Convert dynamic rows to dictionaries
            var dictRows = rows
                .Select(r => (IDictionary<string, object>)r)
                .ToList();

            List<string> columnNames = dictRows.First().Keys.ToList();
            List<string> columnsToRemove = new List<string>();

            foreach (var col in columnNames)
            {
                List<string> values = dictRows
                    .Select(r => r[col]?.ToString() ?? string.Empty)
                    .ToList();

                bool isConstant = values.Distinct().Count() == 1;
                bool isUnique = values.Distinct().Count() == values.Count;

                if (isConstant || isUnique)
                    columnsToRemove.Add(col);
            }

            // Remove columns
            foreach (var row in dictRows)
            {
                foreach (var col in columnsToRemove)
                    row.Remove(col);
            }

            // Write cleaned CSV back to file
            using (var writer = new StreamWriter(filePath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                // Write header
                foreach (var col in dictRows.First().Keys)
                    csv.WriteField(col);
                csv.NextRecord();

                // Write rows
                foreach (var row in dictRows)
                {
                    foreach (var col in row.Values)
                        csv.WriteField(col);
                    csv.NextRecord();
                }
            }
        }

        private static void ApplyClassicalMLPredictorColumn(string filePath,  ConflictDefinition metric)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            // Load CSV
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return; // nothing to process

            var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
            var rows = lines.Skip(1)
                            .Select(line => line.Split(','))
                            .Select(values => values.Select(v => v.Trim()).ToList())
                            .ToList();

            // Determine metric column
            int metricIndex = metric switch
            {
                ConflictDefinition.LocalTotalFatalities => headers.IndexOf("LocalTotalFatalities"),
                ConflictDefinition.RegionalTotalFatalities => headers.IndexOf("RegionalTotalFatalities"),
                ConflictDefinition.LocalAvgSeverity => headers.IndexOf("LocalAvgSeverity"),
                ConflictDefinition.RegionalAvgSeverity => headers.IndexOf("RegionalAvgSeverity"),
                _ => throw new ArgumentException($"Unsupported metric: {metric}")
            };

            if (metricIndex < 0)
                throw new ArgumentException($"CSV must contain a '{metric}' column.");

            // Build target column name
            string targetColumn = $"Target_{headers[metricIndex]}";
            headers.Add(targetColumn);

            // Add target values (same as metric value)
            foreach (var row in rows)
            {
                string metricValue = row[metricIndex];
                row.Add(metricValue);
            }

            // Remove the original metric column from headers and rows
            headers.RemoveAt(metricIndex);

            foreach (var row in rows)
                row.RemoveAt(metricIndex);

            // Write updated CSV
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine(string.Join(",", headers));
                foreach (var row in rows)
                    writer.WriteLine(string.Join(",", row));
            }
        }


        private static void ApplyTimeSeriesMLPredictorColumn(string filePath, TimePrediction timespan, ConflictDefinition metric)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            // Load CSV
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return; // nothing to process

            var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
            var rows = lines.Skip(1)
                            .Select(line => line.Split(','))
                            .Select(values => values.Select(v => v.Trim()).ToList())
                            .ToList();

            // Required columns
            int dateIndex = headers.IndexOf("periodStart");
            int placeIndex = headers.IndexOf("place");
            int countryIndex = headers.IndexOf("country");
            int metricIndex = -1;

            switch (metric)
            {
                case ConflictDefinition.LocalTotalFatalities:
                    metricIndex = headers.IndexOf("LocalTotalFatalities");
                    break;
                case ConflictDefinition.RegionalTotalFatalities:
                    metricIndex = headers.IndexOf("RegionalTotalFatalities");
                    break;
                case ConflictDefinition.LocalAvgSeverity:
                    metricIndex = headers.IndexOf("LocalAvgSeverity");
                    break;
                case ConflictDefinition.RegionalAvgSeverity:
                    metricIndex = headers.IndexOf("RegionalAvgSeverity");
                    break;
                default:
                    throw new ArgumentException($"Unsupported metric: {metric}");
            }

            if (dateIndex < 0)
                throw new ArgumentException("CSV must contain a 'periodStart' column.");

            if (placeIndex < 0)
                throw new ArgumentException("CSV must contain a 'place' column.");

            if (metricIndex < 0)
                throw new ArgumentException($"CSV must contain a '{metric}' column.");

            // Determine future offset
            TimeSpan offset = timespan switch
            {

                TimePrediction.OneMonth => TimeSpan.FromDays(28),
                TimePrediction.ThreeMonths => TimeSpan.FromDays(90),
                TimePrediction.SixMonths => TimeSpan.FromDays(181),
                TimePrediction.TwelveMonths => TimeSpan.FromDays(365),

                _ => throw new ArgumentOutOfRangeException(nameof(timespan))
            };

            // Window length (currently same as offset, but can differ later)
            TimeSpan window = timespan switch
            {
            
                TimePrediction.OneMonth => TimeSpan.FromDays(28),
                TimePrediction.ThreeMonths => TimeSpan.FromDays(90),
                TimePrediction.SixMonths => TimeSpan.FromDays(181),
                TimePrediction.TwelveMonths => TimeSpan.FromDays(365),
                _ => throw new ArgumentOutOfRangeException(nameof(timespan))
            };

            // New predictor column name
            string predictorColumn = $"Target{metric}_In_{timespan}";
            headers.Add(predictorColumn);

            // Build lookup: (PlaceName, Date) → metric value
            var lookup = rows.ToDictionary(
                row => (Place: row[placeIndex],
                        Date: DateTime.Parse(row[dateIndex]),
                        Country: row[countryIndex]
                        
                        ),
                row => row[metricIndex]
            );

            // Add predictor values using offset + window
            for (int i = 0; i < rows.Count; i++)
            {

                if (i % 100 == 0)
                {
                    Console.WriteLine($"Index: {i} of {rows.Count}");
                }

                var row = rows[i];
                string place = row[placeIndex];
                DateTime currentDate = DateTime.Parse(row[dateIndex]);

                DateTime windowStart = currentDate + offset;
                DateTime windowEnd = windowStart + window;

                // Collect all future rows in the window
                var futureValues = lookup
                    .Where(kvp =>
                        kvp.Key.Place == place &&
                        kvp.Key.Date >= windowStart &&
                        kvp.Key.Date <= windowEnd)
                    .Select(kvp => kvp.Value)
                    .ToList();

                string aggregatedValue;

                if (futureValues.Count == 0)
                {
                    aggregatedValue = "0"; // no future data
                }
                else
                {
                    // Compute average of numeric values
                    var numericValues = futureValues
                        .Select(v => double.TryParse(v, out var d) ? d : 0)
                        .ToList();

                    double mean = numericValues.Average();
                    aggregatedValue = mean.ToString("0.###");
                }

                row.Add(aggregatedValue);
            }

            // Write updated CSV
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine(string.Join(",", headers));
                foreach (var row in rows)
                    writer.WriteLine(string.Join(",", row));
            }

            string x = "";
        }

        private static void RobustScaleNumericColumns(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            // Load CSV
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return; // nothing to process

            var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
            var rows = lines.Skip(1)
                            .Select(line => line.Split(',').Select(v => v.Trim()).ToList())
                            .ToList();

            int columnCount = headers.Count;

            // Identify numeric columns
            var numericColumnIndexes = new List<int>();

            for (int col = 0; col < columnCount; col++)
            {
                bool isNumeric = true;

                foreach (var row in rows)
                {
                    if (!double.TryParse(row[col], out _))
                    {
                        isNumeric = false;
                        break;
                    }
                }

                if (isNumeric)
                    numericColumnIndexes.Add(col);
            }

            // Compute median and IQR for each numeric column
            var medians = new Dictionary<int, double>();
            var iqrs = new Dictionary<int, double>();

            foreach (int col in numericColumnIndexes)
            {
                var values = rows.Select(r => double.Parse(r[col])).OrderBy(v => v).ToList();
                int n = values.Count;

                double median = (n % 2 == 1)
                    ? values[n / 2]
                    : (values[n / 2 - 1] + values[n / 2]) / 2.0;

                double q1 = values[n / 4];
                double q3 = values[(3 * n) / 4];
                double iqr = q3 - q1;

                if (iqr == 0)
                    iqr = 1; // avoid divide-by-zero

                medians[col] = median;
                iqrs[col] = iqr;
            }

            // Apply robust scaling
            foreach (var row in rows)
            {
                foreach (int col in numericColumnIndexes)
                {
                    double original = double.Parse(row[col]);
                    double scaled = (original - medians[col]) / iqrs[col];
                    row[col] = scaled.ToString("G17"); // high precision
                }
            }

            // Write updated CSV
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine(string.Join(",", headers));
                foreach (var row in rows)
                    writer.WriteLine(string.Join(",", row));
            }
        }


    }
}
