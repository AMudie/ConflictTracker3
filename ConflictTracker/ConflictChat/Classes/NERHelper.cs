using ConflictCommon.Classes.StaticHelpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;


namespace ConflictChat.Classes
{
    internal static class NERHelper
    {


        internal static Dictionary<string, (string Type, string NodeID)> IdentifyNamedEntities(string userPrompt, string kgName)
        {

            Neo4jQueryService service = new Neo4jQueryService(
                AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password")

           );

            Dictionary<string, string> namedPlaces = service.GetAllPlacesForNERAsync(kgName).Result;
            Dictionary<string, string> namedActors = service.GetAllActorsForNERAsync(kgName).Result;

            service.DisposeAsync().AsTask().Wait();

            Dictionary<string, (string Type, string NodeID)> namedEntities = new();

            foreach (var place in namedPlaces)
            {
                namedEntities.TryAdd(
                    StringHelper.CleanStringForNER(place.Key),
                    (Type: "Place", NodeID: place.Value)
                );
            }

            foreach (var actor in namedActors)
            {
                namedEntities.TryAdd(
                    StringHelper.CleanStringForNER(actor.Key),
                    (Type: "Actor", NodeID: actor.Value)
                );
            }

            int foundEntityCount = 0;
            Dictionary<string, (string Type, string NodeID)> foundEntities = new();
            List<string> namedEntitiesKeys = namedEntities.Select(n => n.Key).ToList();
            namedEntitiesKeys.Sort();

            foreach (string key in namedEntities.Keys)
            {
                if (StringHelper.CleanStringForNER(userPrompt)
                    .Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    foundEntityCount++;
                    foundEntities.Add(key, (namedEntities[key].Type, namedEntities[key].NodeID));
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(
                        $"Found {namedEntities[key].Type}: {key}; Node: {namedEntities[key].NodeID}"
                    );
                    Console.ResetColor();
                }
            }


            if (foundEntityCount == 0)
            {
                foundEntityCount += 1;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No named entities found. Check case and abbreviations.");
                Console.ResetColor();
            }

            return foundEntities;

        }
    }
}
