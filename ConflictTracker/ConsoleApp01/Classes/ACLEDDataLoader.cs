using ConflictCommon.Classes.DTOs;
using ConflictConsole.Interfaces;
using ConflictConsole.StaticHelpers;
using Neo4j.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;


namespace ConflictConsole.Classes
{
    internal class ACLEDDataLoader : IPlaceLoader, IActorLoader, IEventLoader
    {
        public async Task<GeographicalPlace[]> LoadPlacesAsync(string[] args)
        {
            List<GeographicalPlace> places = new List<GeographicalPlace>();
            if (args.Length != 1)
            {
                throw new ArgumentException($"Invalid args passed. Expected: 1, found: {args.Length}.");
            }
            else if (!args[0].ToUpper().EndsWith(".CSV"))
            {
                throw new ArgumentException($"Passed file or resource is not a .csv file.");
            }
            else if (!File.Exists(args[0]) && !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(args[0]))
            {
                throw new FileNotFoundException("Specified file or resouce is not found.");
            }
            else
            {


                int rowIndex = 0;

                await foreach (var row in ConflictConsole.StaticHelpers.MyCSVHelper.ReadCsvAsync(args[0]))
                {
                    rowIndex++;
                    GeographicalPlace place = new GeographicalPlace();
                    try
                    {

                        if (!string.IsNullOrEmpty(row["country"]))
                        {
                            place = new GeographicalPlace
                            {
                                Name = row["country"],
                                Country = row["country"],
                                Latitude = double.Parse(row["latitude"]),
                                Longitude = double.Parse(row["longitude"])
                            };

                            places.Add(place);
                        }
             

                        if (!string.IsNullOrEmpty(row["admin1"]))
                        {
                            place = new GeographicalPlace
                            {
                                Name = row["admin1"],
                                Country = row["country"],
                                ParentName = row["country"],
                                Latitude = double.Parse(row["latitude"]),
                                Longitude = double.Parse(row["longitude"])
                            };

                            places.Add(place);
                        }

                        if (!string.IsNullOrEmpty(row["admin2"]))
                        {
                            place = new GeographicalPlace
                            {
                                Name = row["admin2"],
                                Country = row["country"],
                                ParentName = row["admin1"],
                                Latitude = double.Parse(row["latitude"]),
                                Longitude = double.Parse(row["longitude"])
                            };

                            places.Add(place);
                        }

                        if (!string.IsNullOrEmpty(row["admin3"]))
                        {
                            place = new GeographicalPlace
                            {
                                Name = row["admin3"],
                                Country = row["country"],
                                ParentName = row["admin2"],
                                Latitude = double.Parse(row["latitude"]),
                                Longitude = double.Parse(row["longitude"])
                            };

                            places.Add(place);
                        }



                        if (!string.IsNullOrEmpty(row["location"]))
                        {
                            place = new GeographicalPlace
                            {
                                Name = row["location"],
                                Country = row["country"],
                                ParentName = new[] { row["admin3"], row["admin2"], row["admin1"], row["country"] }
    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                                Latitude = double.Parse(row["latitude"]),
                                Longitude = double.Parse(row["longitude"])
                            };

                            places.Add(place);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Failed to load place: {place.Name} (row index: {rowIndex}): {ex.Message}");
                        Console.ResetColor();
                    }

                }
            }
            //Return only places where the parent name does not match the name, and the name is not blank.
            return places.Where(x => x.ParentName is null || x.ParentName.ToUpper() != x.Name.ToUpper()).Where(x => !string.IsNullOrWhiteSpace(x.Name)).Distinct().ToArray();
        }

        public async Task<Actor[]> LoadActorsAsync(string[] args)
        {
            List<Actor> actors = new List<Actor>();

            if (args.Length != 1)
                throw new ArgumentException($"Invalid args passed. Expected: 1, found: {args.Length}.");

            if (!args[0].ToUpper().EndsWith(".CSV"))
                throw new ArgumentException($"Passed file or resource is not a .csv file.");

            if (!File.Exists(args[0]) &&
                !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(args[0]))
                throw new FileNotFoundException("Specified file or resource is not found.");

            int rowIndex = 0;

            await foreach (var row in MyCSVHelper.ReadCsvAsync(args[0]))
            {
                rowIndex++;
                Actor actor = new Actor();

                try
                {
                    if (!string.IsNullOrEmpty(row["actor1"]))
                    {
                        actor = new Actor
                        {
                            Name = row["actor1"].Trim(),
                            Type = row["inter1"].Trim()
                        };
                        actors.Add(actor);
                    }

                    if (!string.IsNullOrEmpty(row["assoc_actor_1"]))
                    {
                        string[] associatedActors = row["assoc_actor_1"].Trim().Split(";");
                        foreach (string associatedActor in associatedActors)
                        {
                            actors.Add(new Actor
                            {
                                Name = associatedActor.Trim(),
                                Type = ""
                            });
                        }
                    }

                    if (!string.IsNullOrEmpty(row["actor2"]))
                    {
                        actor = new Actor
                        {
                            Name = row["actor2"].Trim(),
                            Type = row["inter2"].Trim()
                        };
                        actors.Add(actor);
                    }

                    if (!string.IsNullOrEmpty(row["assoc_actor_2"]))
                    {
                        string[] associatedActors = row["assoc_actor_2"].Trim().Split(";");
                        foreach (string associatedActor in associatedActors)
                        {
                            actors.Add(new Actor
                            {
                                Name = associatedActor.Trim(),
                                Type = ""
                            });
                        }
                    }


                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Failed to load actor: {actor.Name} (row index: {rowIndex}): {ex.Message}");
                    Console.ResetColor();
                }
            }

            //update missing type of actors with no set type, what are elsewhere in the dataset:
            int affectedActors = actors.Where(a => string.IsNullOrWhiteSpace(a.Type)).Count();
            int typeIndex = 0;
            foreach (Actor a in actors.Where(a => string.IsNullOrWhiteSpace(a.Type)))
            {
                typeIndex += 1;
                Actor matchedActor = actors.FirstOrDefault(b =>
                    b.Name.Trim().ToUpper() == a.Name.Trim().ToUpper() &&
                    !string.IsNullOrWhiteSpace(b.Type));

                if (matchedActor != null)
                    a.Type = matchedActor.Type;

                if (typeIndex % 100 == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Updated actor type of {typeIndex} of {affectedActors}");
                    Console.ResetColor();
                }
            }

            return actors
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Type))
                .Distinct()
                .ToArray();
        }

        public async Task<Event[]> LoadEventsAsync(string[] args)
        {
            List<Event> events = new List<Event>();

            if (args.Length != 1)
                throw new ArgumentException($"Invalid args passed. Expected: 1, found: {args.Length}.");

            if (!args[0].ToUpper().EndsWith(".CSV"))
                throw new ArgumentException($"Passed file or resource is not a .csv file.");

            if (!File.Exists(args[0]) &&
                !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(args[0]))
                throw new FileNotFoundException("Specified file or resource is not found.");

            int rowIndex = 0;

            await foreach (var row in MyCSVHelper.ReadCsvAsync(args[0]))
            {
                rowIndex++;
                Event @event = new Event();
                try
                {
                    @event.ID = row["event_id_cnty"];
                    @event.DisorderType = row["disorder_type"];
                    @event.Type = row["event_type"];
                    @event.Subtype = row["sub_event_type"];
                    @event.Summary = @row["notes"];
                    @event.Source = row["source"];
                    @event.Fatalities = int.TryParse(row["fatalities"], out int fatalities) ? fatalities : 0;
                    @event.Location = row["location"];
                    @event.Country = row["country"];
                    @event.DateTime = DateTime.TryParse(row["event_date"], out DateTime eventDate) ? eventDate : DateTime.MinValue;

                    List<string> actors = new List<string>();

                    if (row["actor1"] != null && !string.IsNullOrWhiteSpace(row["actor1"]))
                        actors.Add(row["actor1"].Trim().Replace(Environment.NewLine, ""));
                    if (row["actor2"] != null && !string.IsNullOrWhiteSpace(row["actor2"]))
                        actors.Add(row["actor2"].Trim().Replace(Environment.NewLine, ""));

                    if (row["assoc_actor_1"] != null && !string.IsNullOrWhiteSpace(row["assoc_actor_1"]))
                    {
                        string[] assocActors1 = row["assoc_actor_1"].Split(';');
                        actors.AddRange(assocActors1.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().Replace(Environment.NewLine, "")));
                    }

                    if (row["assoc_actor_2"] != null && !string.IsNullOrWhiteSpace(row["assoc_actor_2"]))
                    {
                        string[] assocActors1 = row["assoc_actor_2"].Split(';');
                        actors.AddRange(assocActors1.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().Replace(Environment.NewLine, "")));
                    }
                    @event.Actors = actors;

                    @event.Severity = LookupEventSeverity(@event.Type, @event.Subtype);

                    events.Add(@event);

                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Failed to load actor: {@event.ID} (row index: {rowIndex}): {ex.Message}");
                    Console.ResetColor();
                }
            }


            int eventsCount = events.Distinct().Count();

            return events
                .Distinct()
                .ToArray();

        }

        /// <summary>
        /// Looks up the severity of an event based on its type and subtype from a JSON file.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="subType"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="JsonException"></exception>
        /// <remarks>Severity is a best guess lookup; nullable integer 0-5; 5 being most severe. Null for Strategic developments/Other. </remarks>
        private int? LookupEventSeverity(string type, string subType)
        {

            const string jsonPath = "ConflictConsole.Resources.Severity.ACLED Severity.json";
            if (!File.Exists(jsonPath) && !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(jsonPath))
            {
                throw new FileNotFoundException("Specified file or resouce is not found.");
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();

                using var stream = assembly.GetManifestResourceStream(
                    "ConflictConsole.Resources.Severity.ACLED Severity.json"
                );

                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                List<ACLEDSeverityDTO> records = JsonSerializer.Deserialize<List<ACLEDSeverityDTO>>(json);

                if (records == null || records.Count == 0)
                {
                    throw new JsonException("Failed to deserialize the severity records from the JSON file.");

                }
                else
                { 
                    return records.FirstOrDefault(r => r.EventType.Trim().ToUpper() == type.Trim().ToUpper() && r.SubEventType.Trim().ToUpper() == subType.Trim().ToUpper())?.Severity;
                }
            }
        }

    }
}
