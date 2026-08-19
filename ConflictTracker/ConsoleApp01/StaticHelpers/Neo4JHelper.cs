using ConflictCommon.Classes.DTOs;
using ConflictConsole.Classes;
using Neo4j.Driver;
using System.Data;
using System.Diagnostics;
using System.Numerics;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;

namespace ConflictConsole.StaticHelpers
{
    public static class Neo4JHelper
    {
        #region "General"
        public static async Task<bool> CreateEmptyGraphAsync(string uri, string user, string password, string databaseName)
        {
            databaseName = databaseName.Replace(" ", "");
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            // Create the database if it doesn't exist
            await using (var session = driver.AsyncSession())
            {
                await session.RunAsync($"CREATE DATABASE {databaseName.Replace(" ", "")} IF NOT EXISTS");
            }

            // Clear all nodes/relationships to ensure it's empty
            await using (var session = driver.AsyncSession())
            {
                await session.RunAsync("MATCH (n) DETACH DELETE n");
            }

            // Correct way to close the driver in Neo4j 6.x
            await driver.DisposeAsync();

            return true;
        }



        #endregion



        #region Places
        internal static async Task LoadPlacesAsync(
            string uri,
            string user,
            string password,
            string databaseName,
            IEnumerable<GeographicalPlace> places)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            try
            {

                await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));



                int index = 0;

                foreach (GeographicalPlace place in places)
                {
                    // Create or update the place node with a spatial point
                    var cypher = @"
            MERGE (p:Place { name: $name })
            SET p.location = point({ latitude: $lat, longitude: $lon }),
            p.country   = $country,
            p.isCapital = $isCapital    
        ";

                    var parameters = new
                    {
                        name = place.Name,
                        country = place.Country,
                        lat = place.Latitude,
                        lon = place.Longitude,
                        isCapital = place.IsCapital
                    };

                    await session.RunAsync(cypher, parameters);

                    index += 1;

                    if (index % 100 == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Saved place {index} of {places.Count()}");
                        Console.ResetColor();
                    }
                }



                // Ensure spatial index exists
                await EnsureSpatialIndexAsync(session);

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
            finally
            {
                await driver.DisposeAsync();
            }
        }



        /// <summary>
        /// Updates all places with country = X with the minimum distance to that country's border (Border nodes)
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <param name="databaseName"></param>
        /// <param name="country"></param>
        /// <returns></returns>
        public static async Task LoadPlacesWithDistanceToBorder(
string uri,
string user,
string password,
string databaseName,
string country)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

            var cypher = @"
MATCH (p:Place)
MATCH (b:Border)
WHERE p.country = b.country
  AND p.country = $country
WITH p,point.distance(p.location, b.location) / 1000.0 AS dKm
WITH p, min(dKm) AS minDistKm
SET p.minBorderDistanceKm = minDistKm
";

            var parameters = new
            {
                country = country
            };
            await session.RunAsync(cypher, parameters);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Updated {country} Place nodes with distance to border.");
            Console.ResetColor();
        }

        internal static async Task LoadRelationshipsFromPlaces(
string uri,
string user,
string password,
string databaseName,
GeographicalPlace[] places)
        {

            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

            int index = 0;

            for (int i = 0; i < places.Count(); i++)
            {

                GeographicalPlace place = places[i];

                // Create parent relationship if needed
                if (!string.IsNullOrWhiteSpace(place.ParentName))
                {
                    var relCypher = @"
           MERGE (child:Place { name: $childName, country: $childCountry })
MERGE (parent:Place { name: $parentName, country: $parentCountry })
MERGE (child)-[:WITHIN]->(parent)
                ";

                    var relParams = new
                    {
                        childName = place.Name.Trim(),
                        childCountry = place.Country.Trim(),
                        parentName = place.ParentName.Trim(),
                        parentCountry = place.Country.Trim()
                    };

                    await session.RunAsync(relCypher, relParams);
                }

                if (i % 100 == 0)
                {
                    Console.WriteLine($"Applied relationships between child and parent places {i} of {places.Count()}. ");
                }

            }

        }

        public static async Task<DataTable> GetPlacesAsync(string kgName,
    string uri,
string user,
string password, List<string> countries)
        {

            try
            {

                var cypher = @"
                    MATCH (top:Place)
                    WHERE top.name IN $countries
                    MATCH (place:Place)-[:WITHIN*0..]->(top)
                    RETURN id(place) as ID, place.name AS Place, top.name AS Country
                    ";

                var parameters = new
                {
                    countries = countries
                };

                var table = new DataTable();
                table.Columns.Add("NodeID", typeof(string));
                table.Columns.Add("Place", typeof(string));
                table.Columns.Add("Country", typeof(string));
                var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

                await using var session = driver.AsyncSession(o => o.WithDatabase(kgName));

                var result = await session.RunAsync(cypher, parameters);

                await foreach (var record in result)
                {
                    table.Rows.Add(
                           record["ID"].As<string>(),
                        record["Place"].As<string>(),
                           record["Country"].As<string>()
                    );
                }

                DataTableHelper.PrintDataTable(table);



                return table;

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return new DataTable();
            }
        }
        /// <summary>
        /// Create the WGS‑84 spatial index
        /// </summary>
        /// <param name="session"></param>
        public static async Task EnsureSpatialIndexAsync(
    IAsyncSession session)
        {
            var cypher = @"
CREATE POINT INDEX place_location_index
FOR (p:Place)
ON (p.location);
    ";

            await session.RunAsync(cypher);
        }

        #endregion

        #region Actors

        internal static async Task LoadActorsAsync(
    string uri,
    string user,
    string password,
    string databaseName,
    IEnumerable<Actor> actors)
        {



            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            try
            {


                await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

                int index = 0;

                foreach (var actor in actors)
                {
                    var cypher = @"
            MERGE (a:Actor {name: $name})
            SET a.type = $type
        ";

                    var parameters = new
                    {
                        name = actor.Name.Trim(),
                        type = actor.Type
                    };

                    await session.RunAsync(cypher, parameters);

                    index++;

                    if (index % 100 == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Saved actor {index} of {actors.Count()}");
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
            finally
            {
                await driver.DisposeAsync();
            }
        }

        public static async Task<DataTable> GetActorsInCountryAsync(string kgName,
            string uri,
    string user,
    string password, List<string> countries)
        {

            try
            {

                var cypher = @"
    MATCH (a:Actor)-[:INVOLVED_IN]->(e:Event)
    MATCH (e)-[:OCCURRED_AT]->(p:Place)
    MATCH (p)-[:WITHIN*0..]->(country:Place)
    WHERE country.name IN $countries
    RETURN DISTINCT id(a) as ID, a.name AS Actor, e.id AS EventId, p.name AS Place, country.name AS Country
";

                var parameters = new
                {
                    countries = countries
                };

                var table = new DataTable();
                table.Columns.Add("NodeID", typeof(string));
                table.Columns.Add("Actor", typeof(string));
                table.Columns.Add("EventId", typeof(string));
                table.Columns.Add("Place", typeof(string));
                table.Columns.Add("Country", typeof(string));

                var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

                await using var session = driver.AsyncSession(o => o.WithDatabase(kgName));

                var result = await session.RunAsync(cypher, parameters);

                await foreach (var record in result)
                {
                    table.Rows.Add(
                        record["ID"].As<string>(),
                        record["Actor"].As<string>(),
                        record["EventId"].As<string>(),
                        record["Place"].As<string>(),
                        record["Country"].As<string>()
                    );
                }

                DataTableHelper.PrintDataTable(table);

                return table;

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return new DataTable();
            }
        }



        #endregion


        #region Events
        internal static async Task LoadEventsAsync(
            string uri,
            string user,
            string password,
            string databaseName,
            IEnumerable<Event> events)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            try
            {

                await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

                int index = 0;

                foreach (Event ev in events)
                {
                    // 1. Create or update the Event node (unique by ID)
                    var eventCypher = @"
            MERGE (e:Event { id: $id })
            SET e.summary      = $summary,
                e.disorderType = $disorderType,
                e.type         = $type,
                e.subtype      = $subtype,
                e.source       = $source,
                e.location     = $location,
                e.fatalities   = $fatalities,
                e.datetime     = $datetime,
                e.severity     = $severity,
                e.country      = $country
        ";

                    var eventParams = new
                    {
                        id = ev.ID,
                        summary = ev.@Summary,
                        disorderType = ev.DisorderType,
                        type = ev.Type,
                        subtype = ev.Subtype,
                        source = ev.Source,
                        location = ev.Location,
                        fatalities = ev.Fatalities,
                        datetime = ev.DateTime,
                        severity = ev.Severity,
                        country = ev.Country
                    };

                    await session.RunAsync(eventCypher, eventParams);

                    //    // 2. Link Event -> Place (existing Place by name)
                    //    if (!string.IsNullOrWhiteSpace(ev.Location))
                    //    {
                    //        var placeCypher = @"
                    //    MATCH (e:Event { id: $eventId })
                    //    MATCH (p:Place { name: $placeName })
                    //    MERGE (e)-[:OCCURRED_AT]->(p)
                    //";

                    //        var placeParams = new
                    //        {
                    //            eventId = ev.ID,
                    //            placeName = ev.Location.Trim()
                    //        };

                    //        await session.RunAsync(placeCypher, placeParams);
                    //    }

                    //    // 3. Link Event -> Actors (existing Actor by name)
                    //    if (ev.Actors != null)
                    //    {
                    //        foreach (var actorName in ev.Actors.Where(a => !string.IsNullOrWhiteSpace(a)))
                    //        {
                    //            var actorCypher = @"
                    //        MATCH (e:Event { id: $eventId })
                    //        MATCH (a:Actor { name: $actorName })
                    //        MERGE (a)-[:INVOLVED_IN]->(e)
                    //    ";

                    //            var actorParams = new
                    //            {
                    //                eventId = ev.ID,
                    //                actorName = actorName.Trim()
                    //            };

                    //            await session.RunAsync(actorCypher, actorParams);
                    //        }
                    //    }

                    index++;

                    if (index % 100 == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Saved event {index} of {events.Count()}");
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
            finally
            {
                await driver.DisposeAsync();
            }
        }





        internal static async Task LoadRelationshipsFromEvents(
        string uri,
        string user,
        string password,
        string databaseName,
        Event[] events)
        {

            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

            int index = 0;

            for (int i = 0; i < events.Count(); i++)
            {
                Event ev = events[i];

                // 2. Link Event -> Place (existing Place by name and country)
                if (!string.IsNullOrWhiteSpace(ev.Location))
                {
                    var placeCypher = @"
                MATCH (e:Event { id: $eventId })
                MATCH (p:Place { name: $placeName, country: $country })
                MERGE (e)-[:OCCURRED_AT]->(p)
            ";

                    var placeParams = new
                    {
                        eventId = ev.ID,
                        country = ev.Country,
                        placeName = ev.Location.Trim()
                    };

                    await session.RunAsync(placeCypher, placeParams);
                }

                // 3. Link Event -> Actors (existing Actor by name)
                if (ev.Actors != null)
                {
                    foreach (var actorName in ev.Actors.Where(a => !string.IsNullOrWhiteSpace(a)))
                    {
                        var actorCypher = @"
                    MATCH (e:Event { id: $eventId })
                    MATCH (a:Actor { name: $actorName })
                    MERGE (a)-[:INVOLVED_IN]->(e)
                ";

                        var actorParams = new
                        {
                            eventId = ev.ID,
                            actorName = actorName.Trim()
                        };

                        await session.RunAsync(actorCypher, actorParams);
                    }
                }

                if (i % 100 == 0)
                {
                    Console.WriteLine($"Applied relationships between events and places and events and actors and events {i} of {events.Count()}. ");
                }

            }

        }

        internal static async Task LoadFactsAsync(
    string uri,
    string user,
    string password,
    string kgName,
    IEnumerable<CIAFactDTO> facts)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            await using var session = driver.AsyncSession(o => o.WithDatabase(kgName));

            int index = 0;

            foreach (var fact in facts)
            {
                var cypher = @"
            // Merge the Fact node using composite identity
            MERGE (f:Fact {
                country: $country,
                year: $year,
                subkey: $subkey
            })
            SET f.values = $values

            // Link Fact to Place
            WITH f
            MATCH (p:Place { name: $country })
            MERGE (p)-[:HAS_FACT]->(f)
        ";

                var parameters = new
                {
                    country = fact.Country,
                    year = fact.Year,
                    subkey = fact.SubKey,
                    values = fact.Values
                };

                await session.RunAsync(cypher, parameters);

                index += 1;

                if (index % 100 == 0)
                {
                    Console.WriteLine($"Saved fact {index} of {facts.Count()}");
                }
            }

            await driver.DisposeAsync();
        }



        #endregion

        #region "Borders"

        public static async Task LoadGeoBordersAsync(
            string uri,
            string user,
            string password,
            string databaseName,
            IEnumerable<CoordinateDTO> borders)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));
            int index = 0;
            foreach (var border in borders)
            {
                var cypher = @"
   MERGE (b:Border {
    country: $country,
    location: point({longitude: $lon, latitude: $lat})
});
";
                var parameters = new
                {
                    country = border.Country,
                    lon = border.Longitude,
                    lat = border.Latitude
                };
                await session.RunAsync(cypher, parameters);
                index += 1;
                if (index % 100 == 0)
                {
                    Console.WriteLine($"Saved border {index} of {borders.Count()}");
                }
            }

        #endregion
        }

        /// <summary>
        /// updates all places with country = X with the minimum distance to that country's capital (Place nodes where isCapital = true)
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <param name="databaseName"></param>
        /// <param name="countries"></param>
        /// <returns></returns>
        public static async Task LoadPlacesWithDistanceToCapital(string uri,
string user,
string password,
string databaseName,
List<string> countries)
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(databaseName));

            foreach (string country in countries)
            {

                var cypher = @"
                //match all places in the country and the capital of that country
                MATCH (p:Place), (c:Place)
                WHERE p.country = $country

                //Now identify the capital of that country (usually one, edge cases have more)
                MATCH  (c:Place)
                WHERE c.country = $country AND c.isCapital = TRUE

                WITH p, c, point.distance(p.location, c.location) / 1000.0 AS dKm

                WITH p, min(dKm) AS minDistKm

                SET p.minCapitalDistanceKm = minDistKm  
";

                var parameters = new
                {
                    country = country
                };
                await session.RunAsync(cypher, parameters);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Updated {country} Place nodes with distance to capital.");
                Console.ResetColor();
            }
        }
    }
}