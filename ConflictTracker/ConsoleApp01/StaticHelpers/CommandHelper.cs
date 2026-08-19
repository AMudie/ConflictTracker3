using ConflictConsole.Classes;
using ConflictCommon.Classes.DTOs;
using ConflictCommon.Classes.StaticHelpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
using System.Text;

namespace ConflictConsole.StaticHelpers
{
    internal static class CommandHelper
    {
        internal static async void CreateKG(string name)
        {

            if (name.Contains(" "))
            {
                name = name.Replace(" ", "");
                Console.WriteLine($"Space characters are not supported in Neo4J database names. Name used: {name}");
            }


            bool result = await Neo4JHelper.CreateEmptyGraphAsync(
                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                name
                );

            if (result)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Neo4J database {name} created successfully.");
                Console.ResetColor();
            }

        }

        public static async Task<string[]> ListDatabases()
        {
            string[] values =
        await ConflictCommon.Classes.StaticHelpers.Neo4JHelper.GetDatabaseNamesAsync(
                            AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password")
            );

            return values;

        }

        internal static void Load(string kgName, string? filter = "")
        {
            Dictionary<string, string> resourceStrings = ConflictConsole.StaticHelpers.ResourceHelper.GetResourceStrings(filter);

            if (string.IsNullOrWhiteSpace(kgName))
            {
                throw new ArgumentException($"knowledge graph name '{kgName}' not specified.");
            }

            if (resourceStrings != null && resourceStrings.Count > 0)
            {
                foreach (var kvp in resourceStrings)
                {
                    Console.WriteLine($"Resource Name: {kvp.Key}, Resource Path: {kvp.Value}");
                }

                

                bool validResourceSelected = false;
                while (validResourceSelected == false)
                {
                    Console.Write("> ");
                    string resourceName = Console.ReadLine();
                    if (resourceStrings.ContainsKey(resourceName))
                    {
                        validResourceSelected = true;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(resourceName + " is a valid resource. Loading now...");
                        Console.ResetColor();

                        if (resourceStrings[resourceName].ToUpper().Contains(".ACLED."))
                        {
                            CommandHelper.LoadACLEDResource(kgName, resourceStrings[resourceName]);
                        }
                        else if (resourceStrings[resourceName].ToUpper().Contains(".CIA."))
                        {
                            CommandHelper.LoadCIAResource(kgName, resourceStrings[resourceName]);
                        }
                        else if (resourceStrings[resourceName].ToUpper().Contains(".GEOBORDERS."))
                        {
                            CommandHelper.LoadGeoBordersResource(kgName, resourceStrings[resourceName]);
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(resourceName + " is not a valid resource.");
                        Console.ResetColor();
                    }
                }

            }
            else
            {
                Console.WriteLine("No resource strings found.");
            }
        }

        #region "ACLED"
        private static async Task LoadACLEDResource(string kgName, string resourcePath)
        {


            try
            {

                if (resourcePath.ToUpper().EndsWith(".CSV"))
                {

                    var assembly = Assembly.GetExecutingAssembly();

                    using Stream stream = assembly.GetManifestResourceStream(resourcePath)
                        ?? throw new InvalidOperationException($"Resource not found: {resourcePath}");

                    using StreamReader reader = new StreamReader(stream);
                    string firstLine = reader.ReadLine();

                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        Console.WriteLine("First line of the resource: " + firstLine);
                        Console.WriteLine("Is this line a header? Enter T if header, any other key if not.");
                        Console.Write("> ");
                        bool isHeaderInput = Console.ReadLine() == "T";

                        if (isHeaderInput)
                        {
                            ACLEDDataLoader a = new ACLEDDataLoader();

                            // Load Places
                            var places = await a.LoadPlacesAsync(new[] { resourcePath });
                            var taskPlace = Neo4JHelper.LoadPlacesAsync(
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                                kgName,
                                places);



                            // Load Actors
                            var actors = await a.LoadActorsAsync(new[] { resourcePath });
                            var taskActor = Neo4JHelper.LoadActorsAsync(
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                                kgName,
                                actors);

                            // Load Events
                            var events = await a.LoadEventsAsync(new[] { resourcePath });
                            var taskEvent = Neo4JHelper.LoadEventsAsync(
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                                kgName,
                                events);

                            // wait for the node creation to complete:
                            await Task.WhenAll(taskPlace, taskActor, taskEvent);

                            // execute parallel tasks to create relationships between nodes:
                            var taskRelPlaces = Neo4JHelper.LoadRelationshipsFromPlaces(
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                                kgName,
                                places);

                            var taskRelEvents = Neo4JHelper.LoadRelationshipsFromEvents(
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                                kgName,
                                events);

                            var taskPlaceDistanceToCapital = Neo4JHelper.LoadPlacesWithDistanceToCapital(
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
kgName,
places.Select(p => p.Country).Distinct().ToList());

                            await Task.WhenAll(taskRelPlaces, taskRelEvents, taskPlaceDistanceToCapital);
                        }
                        else
                        {
                            throw new FileLoadException("Only files with headers are supported.");
                        }
                    }
                    else
                    {
                        throw new FileLoadException("first line is null or blank. Is content present?");
                    }
                }
                else
                {
                    throw new ArgumentException("Only .csv resources are supported.");
                }
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }//end of method

        public static void DumpActorsInCountry(string kgName, List<string> countries = null)
        {

            DataTable dtActor = Neo4JHelper.GetActorsInCountryAsync(kgName,
                uri: AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                        countries: countries
                ).Result;

            if (dtActor != null && dtActor.Rows.Count > 0)
            {
                DataTableHelper.DataTableToCsv(dtActor, $"{kgName}_Actors.csv");
            }

        }

        public static void DumpPlacesInCountry(string kgName, List<string> countries = null)
        {

            DataTable dtPlace = Neo4JHelper.GetPlacesAsync(kgName,
                uri: AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                        countries: countries
                ).Result;

            if (dtPlace != null && dtPlace.Rows.Count > 0)
            {
                DataTableHelper.DataTableToCsv(dtPlace, $"{kgName}_Places.csv");
            }
        }
        #endregion

        #region "CIA"

        private static void LoadCIAResource(string kgName, string resourcePath)
        {

            if (resourcePath.ToUpper().EndsWith(".CSV"))
            {

                var assembly = Assembly.GetExecutingAssembly();

                using Stream stream = assembly.GetManifestResourceStream(resourcePath)
                    ?? throw new InvalidOperationException($"Resource not found: {resourcePath}");

                using StreamReader reader = new StreamReader(stream);
                string firstLine = reader.ReadLine();

                if (!string.IsNullOrEmpty(firstLine))
                {
                    Console.WriteLine("First line of the resource: " + firstLine);
                    Console.WriteLine("Is this line a header? Enter T if header, any other key if not.");
                    Console.Write("> ");
                    bool isHeaderInput = Console.ReadLine() == "T";

                    if (isHeaderInput)
                    {

                        //Load Facts:
                        CIAFactLoader c = new CIAFactLoader();
                        CIAFactDTO[] facts = c.LoadFactsAsync(new string[] { resourcePath }).Result;

                        Neo4JHelper.LoadFactsAsync(
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
kgName,
facts);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Loaded {facts.Length} facts.");
                        Console.ResetColor();
                    }
                    else
                    {
                        throw new FileLoadException("Only files with headers are supported.");
                    }
                }
                else
                {
                    throw new FileLoadException("first line is null or blank. Is content present?");
                }
            }
            else
            {
                throw new ArgumentException("Only .csv resources are supported.");
            }
        }//end of method
        #endregion

        #region "GeoBorders"
        private static async Task LoadGeoBordersResource(string kgName, string resourcePath)
        {
            GeoBordersLoader loader = new GeoBordersLoader();

            // Wait for borders to load
            CoordinateDTO[] coordinates = await loader.LoadBordersAsync(new string[] { resourcePath });

            if (coordinates != null && coordinates.Length > 0)
            {
                // Wait for borders to be written to Neo4j
                await Neo4JHelper.LoadGeoBordersAsync(
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                    kgName,
                    coordinates);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Loaded {coordinates.Length} coordinates.");
                Console.ResetColor();

                string country = coordinates.First().Country;

                // Only run AFTER the borders have been fully written
                await Neo4JHelper.LoadPlacesWithDistanceToBorder(
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password"),
                    kgName,
                    country);
            }
            else
            {
                throw new Exception("No coordinates loaded from GeoBorders resource.");
            }
        }
        #endregion
    }
}


