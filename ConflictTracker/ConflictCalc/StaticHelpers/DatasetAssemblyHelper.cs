using ConflictCalc.Enumerations;
using ConflictCommon.Classes.StaticHelpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace ConflictCalc.StaticHelpers
{
    internal static  class DatasetAssemblyHelper
    {

        //builddataset -placeName Khartoum -startDate 20200101 -frequencyPeriod Monthly
        //builddataset -kgname sudan -placeName -startDate 20200101 -frequencyPeriod Monthly
        //builddataset -kgname eastafrica -placeName -startDate 20150101 -frequencyPeriod Monthly

        //<summary>Builds a dataset based on the provided parameters, and opens it.</summary>
        public static void BuildDataset(Dictionary<string, string> flags)
        {

            try
            {

                //defines expected params:
                string[] allowedArgs = ["kgname", "placeName", "startDate", "frequencyPeriod"];

                // Validate that all provided flags are valid
                string[] unexpected = flags.Keys
                .Where(flag => !allowedArgs.Contains(flag, StringComparer.OrdinalIgnoreCase))
                .ToArray();

                if (unexpected.Length > 0)
                {
                    throw new ArgumentException(
                        $"Unexpected parameters: {string.Join(", ", unexpected)}. " +
                        $"Allowed parameters: {string.Join(", ", allowedArgs)}"
                    );
                }

                string kgName = flags["kgname"];
                if (string.IsNullOrWhiteSpace(kgName))
                {
                    throw new ArgumentException("kgName has not been specified.");
                }

                //placeName is allowed to be empty:
                string placeName = "";
                if (flags.ContainsKey("placeName"))
                {
                    placeName = flags["placeName"];
                }


                string startDateString = flags["startDate"];
                if (string.IsNullOrWhiteSpace(startDateString))
                {
                    throw new ArgumentException("StartDate has not been specified.");
                }

                //Validate the passed date string is in the correct format (yyyyMMdd) and can be parsed into a DateTime object
                if (!DateTime.TryParseExact(
                startDateString.Trim(),
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime startDateTime))
                {
                    throw new ArgumentException($"Specified startDate {flags["startDateString"]} was not in a parable format. Use yyyyMMdd only.");
                }

                //Validate the frequency period is one of the expected values (Weekly, Monthly, Quarterly)
                if (!Enum.TryParse<PeriodFrequency>(flags["frequencyPeriod"], ignoreCase: true, out var freqPeriod))
                {
                    throw new ArgumentException($"Specified frequencyPeriod {flags["frequencyPeriod"]} was not a valid value. Valid values: Weekly, Monthly, Quarterly");
                }

                //Params assumed to be valid, so we query the Neo4J database for the data and build the dataset.
                Neo4jQueryService service = new Neo4jQueryService(
            AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password")

        );


                List<Dictionary<string, string>> datasetBase = service.BuildBaseLocalDatasetAsync(kgName, placeName, startDateTime, freqPeriod.ToString()).Result;

                if (datasetBase != null && datasetBase.Count() > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Dataset of {datasetBase.Count} records built successfully for place: {placeName} starting from {startDateString} with frequency: {freqPeriod}.");
                    Console.ResetColor();

                    List<Dictionary<string, string>> datasetRegional = service.BuildBaseRegionalDatasetAsync(kgName, placeName, startDateTime, freqPeriod.ToString()).Result;

                    MergeRegionalIntoLocalIntoDataset(ref datasetBase, ref datasetRegional);

                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"No data found for place: {placeName} starting from {startDateString} with frequency: {freqPeriod}.");
                    Console.ResetColor();
                    return; // Exit the method if no data is found
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Dataset size: {datasetBase.Count()} records.");
                Console.ResetColor();

                // Determine the min and max years from the dataset for building the fact dataset
                int minYear = datasetBase.Select(row => DateTime.ParseExact(row["periodStart"], "yyyy-MM-dd", CultureInfo.InvariantCulture).Year).Min();
                int maxYear = datasetBase.Select(row => DateTime.ParseExact(row["periodStart"], "yyyy-MM-dd", CultureInfo.InvariantCulture).Year).Max();

                // Load facts for the dataset. We'll get creating and pivot for the joining to the base dataset via year and country.
                List<Dictionary<string, string>> datasetFacts = service.BuildFactDataset(kgName, "", minYear, maxYear).Result;
                if (datasetFacts != null)
                {
                    MergeFactsIntoDataset(ref datasetBase, ref datasetFacts);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{datasetFacts.Count()} facts merged into dataset.");
                    Console.ResetColor();
                }

                service.DisposeAsync().AsTask().Wait();


                if (datasetBase != null)
                {

                    //Add additional keys for the change over time. 
                    //ApplyPeriodChanges(datasetBase, freqPeriod);

                    var assemblyPath = Assembly.GetExecutingAssembly().Location;
                    var directory = Path.GetDirectoryName(assemblyPath);

                    //use the kgName if place is not set. 
                    string name = kgName;
                    if (!string.IsNullOrEmpty(placeName))
                    {
                        name = placeName;
                    }

                    var filePath = Path.Combine(directory!, $"{name}_{startDateString}_{Guid.NewGuid().ToString()}.csv");
                    ConflictCommon.Classes.StaticHelpers.CsvHelper.WriteToCSV(datasetBase, filePath);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Base dataset constructed: {filePath}");
                    Console.WriteLine("O to open file, any other key to return to console.");
                    Console.ResetColor();

                    if (Console.ReadLine().ToUpper().Trim() == "O")
                    {

                        ConflictCommon.Classes.StaticHelpers.FileHelper.OpenFile(filePath);
                    }
                    else
                    {
                        //Do nothing
                        Console.ResetColor();
                    }


                }

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }

        }

        ///<summary>Adds additional keys to the dataset, with the change from the previous period for that place.</summary>
        ///<summary>
        /// Adds additional keys to the dataset, with the change from the previous period
        /// for that place. Now uses (country, place, periodStart) as the composite key.
        ///</summary>
        ///<remarks>
        /// Only features starting with "local" or "regional" are calculated.
        ///</remarks>
        static void ApplyPeriodChanges(
            List<Dictionary<string, string>> dataset,
            PeriodFrequency freqPeriod)
        {
            // Build fast lookup: (country, place, periodStart) → row
            var lookup = new Dictionary<(string country, string place, string period), Dictionary<string, string>>();

            foreach (var row in dataset)
            {
                string country = row.TryGetValue("country", out var c) ? c : "N/A";
                string place = row.TryGetValue("place", out var p) ? p : "N/A";
                string period = row.TryGetValue("periodStart", out var ps) ? ps : "N/A";

                lookup[(country, place, period)] = row;
            }

            foreach (var row in dataset)
            {
                string country = row.TryGetValue("country", out var c) ? c : "N/A";
                string place = row.TryGetValue("place", out var p) ? p : "N/A";
                string period = row.TryGetValue("periodStart", out var ps) ? ps : "N/A";

                // Parse current period
                if (!DateTime.TryParseExact(period, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out DateTime currentDate))
                {
                    continue; // skip invalid dates
                }

                // Compute previous period correctly
                DateTime prevDate = freqPeriod switch
                {
                    PeriodFrequency.Monthly => currentDate.AddMonths(-1),
                    PeriodFrequency.Quarterly => currentDate.AddMonths(-3),
                    _ => currentDate
                };

                string prevPeriod = prevDate.ToString("yyyy-MM-dd");

                // Fast lookup using the triple composite key
                if (!lookup.TryGetValue((country, place, prevPeriod), out var prevRow))
                {
                    // No previous row → all deltas = 0
                    foreach (var key in row.Keys.ToList())
                    {
                        if (key.StartsWith("local", StringComparison.OrdinalIgnoreCase) ||
                            key.StartsWith("regional", StringComparison.OrdinalIgnoreCase))
                        {
                            row[$"{key}_Delta"] = "0";
                            row[$"{key}_Lag"] = "0";
                        }
                    }
                }
                else
                {
                    // Compute changes: Apply changes as _Delta and raw previous values as _Lag:
                    foreach (var key in row.Keys.ToList())
                    {
                        if (key.StartsWith("local", StringComparison.OrdinalIgnoreCase) ||
                            key.StartsWith("regional", StringComparison.OrdinalIgnoreCase))
                        {
                            string prevValue = prevRow.TryGetValue(key, out var pv) ? pv : "0";
                            string currentValue = row[key];

                            if (double.TryParse(prevValue, out double prevDouble) &&
                                double.TryParse(currentValue, out double currDouble))
                            {
                                row[$"{key}_Delta"] = (currDouble - prevDouble).ToString();
                                row[$"{key}_Lag"] = (prevDouble).ToString();
                            }
                            else
                            {
                                row[$"{key}_Delta"] = "0";
                                row[$"{key}_Lag"] = "0";
                            }
                        }
                    }
                }


            }
        }


        ///<summary>Merges the contents of the facts from the facts dataset into the base dataset, using the country and year as a composite key.</summary>
        private static void MergeFactsIntoDataset(
           ref List<Dictionary<string, string>> datasetBase,
           ref List<Dictionary<string, string>> datasetFacts)
        {
            // Build an index for fast lookup: (country, year) → list of dataset rows
            var datasetIndex = new Dictionary<(string country, int year), List<Dictionary<string, string>>>();

            foreach (var row in datasetBase)
            {
                var country = row["country"];
                var year = DateTime.ParseExact(row["periodStart"], "yyyy-MM-dd", CultureInfo.InvariantCulture).Year;

                var key = (country, year);

                if (!datasetIndex.TryGetValue(key, out var list))
                {
                    list = new List<Dictionary<string, string>>();
                    datasetIndex[key] = list;
                }

                list.Add(row);
            }

            // Merge facts into dataset
            if (datasetFacts != null)
            {
                foreach (var fact in datasetFacts)
                {
                    var factCountry = fact["country"];
                    var factYear = int.Parse(fact["year"]);

                    var key = (factCountry, factYear);

                    if (datasetIndex.TryGetValue(key, out var baseItems))
                    {
                        foreach (var baseItem in baseItems)
                        {
                            foreach (var kvp in fact)
                            {
                                var keyName = kvp.Key;

                                // Skip join keys
                                if (keyName != "country" && keyName != "year")
                                {
                                    //baseItem[kvp.Value] = fact["values"];
                                    baseItem[fact["subkey"]] = fact["values"];
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Merges the contents of the regional dataset into the local dataset,
        /// using (country, place, periodStart) as a composite key.
        /// </summary>
        /// <summary>
        /// Merges the contents of the regional dataset into the local dataset,
        /// using (country, place, periodStart) as a composite key.
        /// If no regional record exists for a given local row, regional fields are set to "0".
        /// </summary>
        private static void MergeRegionalIntoLocalIntoDataset(
            ref List<Dictionary<string, string>> datasetLocal,
            ref List<Dictionary<string, string>> datasetRegional)
        {
            // Build an index for fast lookup: (country, place, periodStart) → list of dataset rows
            var datasetIndex = new Dictionary<(string country, string place, string periodStart), List<Dictionary<string, string>>>();

            foreach (var row in datasetLocal)
            {
                var country = row["country"];
                var place = row["place"];
                var periodStart = row["periodStart"];   // NEW: include periodStart

                var key = (country, place, periodStart);

                if (!datasetIndex.TryGetValue(key, out var list))
                {
                    list = new List<Dictionary<string, string>>();
                    datasetIndex[key] = list;
                }

                list.Add(row);
            }

            // Build regional index using same composite key
            var regionalIndex = new Dictionary<(string country, string place, string periodStart), Dictionary<string, string>>();

            if (datasetRegional != null)
            {
                foreach (var regionalRecord in datasetRegional)
                {
                    var country = regionalRecord["country"];
                    var place = regionalRecord["place"];
                    var periodStart = regionalRecord["periodStart"];

                    var key = (country, place, periodStart);
                    regionalIndex[key] = regionalRecord;
                }
            }

            // Determine which regional fields exist (excluding join keys)
            var regionalFields = new HashSet<string>();
            if (datasetRegional != null && datasetRegional.Count > 0)
            {
                foreach (var kvp in datasetRegional[0])
                {
                    if (kvp.Key.Trim().ToUpper().StartsWith("REGIONAL"))
                    {
                        regionalFields.Add(kvp.Key);
                    }
                }
            }

            // Merge regional values into local dataset
            foreach (var row in datasetLocal)
            {
                var country = row["country"];
                var place = row["place"];
                var periodStart = row["periodStart"];

                var key = (country, place, periodStart);

                if (regionalIndex.TryGetValue(key, out var regionalRecord))
                {
                    // Copy regional fields
                    foreach (var field in regionalFields)
                    {
                        row[field] = regionalRecord[field];
                    }
                }
                else
                {
                    // No regional record → fill defaults
                    foreach (var field in regionalFields)
                    {
                      
                            row[field] = "0";   // Default value
                        
                    }
                }
            }
        }

    }
}
