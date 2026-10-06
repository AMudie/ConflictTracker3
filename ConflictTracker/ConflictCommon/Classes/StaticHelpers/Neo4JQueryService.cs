using Neo4j.Driver;
using System.Data;
using System.Diagnostics.Metrics;
using System.Text;
using System.Xml.Linq;

namespace ConflictCommon.Classes.StaticHelpers
{


    public class Neo4jQueryService : IAsyncDisposable
    {
        private readonly IDriver _driver;

        public Neo4jQueryService(string uri, string user, string password)
        {
            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
        }

        public async ValueTask DisposeAsync()
        {
            await _driver.DisposeAsync();
        }

        public async Task<List<(INode e, INode p, INode a)>> ExecuteEventsInvolvingActorQueryAsync(List<int> actorNodeIds, List<long> placeNodeIds, string kgName, List<(DateTime Start, DateTime End)>? dateRanges = null)
        {
            var returnValue = new List<(INode, INode, INode)>();



            var parameters = new Dictionary<string, object>
            {
                ["placeNodeIds"] = placeNodeIds,
                ["actorNodeIds"] = actorNodeIds
            };

            StringBuilder cypher = new StringBuilder();

            cypher.AppendLine(@"WITH $placeNodeIds AS inputPlaces, $actorNodeIds AS inputActors");

            cypher.AppendLine(@"MATCH(e: Event)");
            cypher.AppendLine(@"WHERE TRUE");
            string eventDateFilter = BuildDateTimeFilter(dateRanges, "e");
            cypher.AppendLine(eventDateFilter);

            cypher.AppendLine(@"MATCH(e) - [:OCCURRED_AT]->(p: Place)");
            cypher.AppendLine(@"WHERE(size(inputPlaces) = 0 OR id(p) IN inputPlaces)");

            cypher.AppendLine(@"MATCH(a: Actor) - [:INVOLVED_IN]->(e)");
            cypher.AppendLine(@"WHERE(size(inputActors) = 0 OR id(a) IN inputActors)");

            cypher.AppendLine(@"MATCH(allActors: Actor) - [:INVOLVED_IN]->(e)");
            cypher.AppendLine(@"RETURN DISTINCT e, p, allActors");


            string cypherQuery = cypher.ToString();
            Console.WriteLine($"Executing Cypher Query:\n{cypherQuery}\nWith Parameters: {string.Join(", ", parameters.Select(kv => $"{kv.Key}: {kv.Value}"))}");

            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));
            var cursor = await session.RunAsync(cypherQuery, parameters);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;


                var e = TryGetNode(record, "e");
                var p = TryGetNode(record, "p");
                // var a = TryGetNode(record, "a");
                var a = TryGetNode(record, "allActors");

                //results.Add((e, p, a));
                returnValue.Add((e, p, a));
            }

            return returnValue;
        }

        public async Task<List<INode>> ExecutePlacesWithinPlacesQueryAsync(List<int> placeNodeIds, string kgName)
        {
            var results = new List<INode>();


            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));

            var parameters = new Dictionary<string, object>
            {
                ["placeNodeIds"] = placeNodeIds
            };

            string cypher = @"
            WITH $placeNodeIds AS inputPlaces

            MATCH (pRoot:Place)
            WHERE size(inputPlaces) = 0 OR id(pRoot) IN inputPlaces
            MATCH (child:Place)-[:WITHIN*0..]->(pRoot)

            RETURN distinct child as place
            ";

            var cursor = await session.RunAsync(cypher, parameters);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;



                var p = TryGetNode(record, "place");

                results.Add(p);
            }

            return results;
        }

        private INode? TryGetNode(IRecord record, string key)
        {
            return record.Keys.Contains(key) && record[key] is INode node
                ? node
                : null;
        }


        private string BuildDateTimeFilter(List<(DateTime Start, DateTime End)> ranges, string eventVar)
        {
            if (ranges == null || ranges.Count == 0)
                return ""; // no date filtering

            var parts = ranges.Select(r =>
                $"(date({eventVar}.datetime) >= date('{r.Start:yyyy-MM-dd}') AND date({eventVar}.datetime) <= date('{r.End:yyyy-MM-dd}'))"
            );

            return "AND (" + string.Join(" OR ", parts) + ")";
        }


        public async Task<Dictionary<string, string>> GetAllPlacesForNERAsync(string kgName)
        {
            var results = new Dictionary<string, string>();

            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));

            string cypher = @"
            MATCH (p:Place)
            RETURN p.name AS name, id(p) AS id
            ORDER BY id(p)
            ";

            var cursor = await session.RunAsync(cypher);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;

                var name = record["name"].As<string>();
                var id = record["id"].As<long>().ToString();

                // Avoid duplicates if any
                if (!results.ContainsKey(name))
                    results[name] = id;
            }

            return results;
        }

        public async Task<Dictionary<string, string>> GetAllActorsForNERAsync(string kgName)
        {
            var results = new Dictionary<string, string>();

            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));

            string cypher = @"
               MATCH (a:Actor)
               RETURN a.name AS name, id(a) AS id
               ORDER BY id(a)
               ";

            var cursor = await session.RunAsync(cypher);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;

                var name = record["name"].As<string>();
                var id = record["id"].As<long>().ToString();

                // Avoid duplicates if any
                if (!results.ContainsKey(name))
                    results[name] = id;
            }

            return results;
        }

        /// <summary>
        /// Generates a sequence of date ranges starting at <paramref name="startDate"/> and
        /// continuing until <paramref name="endDate"/> (currently overridden for testing).
        /// Each range represents a period whose length is determined by <paramref name="frequency"/>:
        /// "Monthly" creates 28‑day periods, and "Quarterly" creates 84‑day periods.
        /// </summary>
        /// <param name="startDate">The initial date from which period generation begins.</param>
        /// <param name="endDate">The final cutoff date for generating periods (overridden in method).</param>
        /// <param name="frequency">Determines the size of each period: "Monthly" or "Quarterly".</param>
        /// <returns>A list of (start, end) tuples representing each generated period.</returns>
        /// <remarks>The last period shoudl always be removed, as this wil not be a full period of 28 or 84 days.</remarks>
        private List<(DateTime start, DateTime end)> LoadPeriods(DateTime startDate, DateTime? endDate, string frequency)
        {
            List<(DateTime start, DateTime end)> periods = new List<(DateTime start, DateTime end)>();
            DateTime current = startDate;

            if (endDate == null)
            {
                endDate = new DateTime(2025, 08, 29); //hardcoded end date for testing, as ACLED data ends on 09/08/2025. 
            }


            while (current <= endDate)
            {
                var next = frequency switch
                {

                    "Monthly" => current.AddDays(28), //4 weeks
                    "Quarterly" => current.AddDays(3 * 28) //12 weeks.
                };

                periods.Add((current, next));
                current = next;
            }

            return periods;
        }

        /// <summary>
        /// Returns values for a tabular classical ML dataset. 
        /// </summary>
        /// <param name="kgName"></param>
        /// <param name="placeName"></param>
        /// <param name="startDate"></param>
        /// <param name="frequencyPeriod"></param>
        /// <returns>List of dictionary where keys are column names, values are values.</returns>
        /// <remarks> Local events are defined based on places. The local events for place X are all the places OCCURRED_AT place X, plus events near to that place (10KM). That second part uses the event's location to the place, not any OCCURRED_AT place's location. Note also that some places are currently included which have 0 events. This does not automatically make them peaceful places, these are likely admin3-admin1 level places for which events are coded at a lower level.  </remarks>
        public async Task<List<Dictionary<string, string>>> BuildBaseLocalDatasetAsync(
    string kgName,
    string placeName,
    DateTime startDate,
    string frequencyPeriod)
        {

            var results = new List<Dictionary<string, string>>();

            //ACLED data ends on 09/08/2025
            //Need to remove the final period, as likely not a complete 28 days. 
            List<(DateTime start, DateTime end)> periods = LoadPeriods(startDate, endDate: null, frequencyPeriod);


            foreach (var period in periods)
            {

                int beforeLoadCount = results.Count();
                var parameters = new Dictionary<string, object>
                {
                    ["periodStart"] = new LocalDateTime(period.start),//startDateStr,
                    ["periodEnd"] = new LocalDateTime(period.end),
                    ["placeName"] = placeName,//,
                    ["radiusKm"] = 10 // Convert 50 km to meters for Neo4j distance function
                };

                string cypher = @"
WITH
  $radiusKm AS radiusKM, 
  $periodStart AS periodStart, 
  $periodEnd AS periodEnd,
  $placeName AS placeName

// Root places
MATCH (rootPlace:Place)
WHERE ((rootPlace.name = placeName OR placeName = '' OR placeName IS NULL) AND rootPlace.name <> rootPlace.country)

// Events at the root place
OPTIONAL MATCH (rootPlace)<-[:OCCURRED_AT]-(rootEvent:Event)
WHERE rootEvent.datetime >= periodStart AND rootEvent.datetime < periodEnd

WITH rootPlace, radiusKM, periodStart, periodEnd,
     collect(elementId(rootEvent)) AS rootEventIds

// Nearby events
OPTIONAL MATCH (nearbyEvents:Event)
WHERE nearbyEvents.datetime >= periodStart
  AND nearbyEvents.datetime < periodEnd
  AND point.distance(nearbyEvents.location, rootPlace.location) < radiusKM * 1000
  AND NOT elementId(nearbyEvents) IN rootEventIds

WITH rootPlace, radiusKM, periodStart, periodEnd,
     rootEventIds,
     collect(elementId(nearbyEvents)) AS nearbyEventIds

// Combine and dedupe
//WITH rootPlace, radiusKM, periodStart, periodEnd,
//     coll.distinct(rootEventIds + nearbyEventIds) AS allEventIds

WITH rootPlace, radiusKM, periodStart, periodEnd, rootEventIds, nearbyEventIds,
     reduce(acc = [], x IN rootEventIds + nearbyEventIds |
       CASE WHEN x IN acc THEN acc ELSE acc + x END
     ) AS allEventIds

// Load local events
OPTIONAL MATCH (localEvents:Event)
WHERE elementId(localEvents) IN allEventIds

// Load actors per event
OPTIONAL MATCH (actor:Actor)-[:INVOLVED_IN]->(localEvents)

// Build per-event actor list
WITH rootPlace, periodStart, periodEnd,
     localEvents,
     collect(actor) AS actorsForEvent

// Build raw event → actor map
WITH rootPlace, periodStart, periodEnd,
     collect({event: localEvents, actors: actorsForEvent}) AS rawEventActorMap

// Remove null-event entries so places with no events have an empty list
WITH rootPlace, periodStart, periodEnd, 
     [e IN rawEventActorMap WHERE e.event IS NOT NULL] AS eventActorMap

// Flatten actors
WITH rootPlace, periodStart, periodEnd, eventActorMap,
     reduce(acc = [], entry IN eventActorMap | acc + entry.actors) AS allActorsFlat

// Distinct actors via manual dedupe
WITH rootPlace, periodStart, periodEnd, eventActorMap, allActorsFlat,
     reduce(acc = [], x IN allActorsFlat |
       CASE WHEN x IN acc THEN acc ELSE acc + x END
     ) AS distinctActors

// Distinct event types/subtypes via manual dedupe
WITH rootPlace, periodStart, periodEnd, eventActorMap, distinctActors,
     reduce(acc = [], entry IN eventActorMap |
       CASE WHEN entry.event.type IN acc THEN acc ELSE acc + entry.event.type END
     ) AS eventTypes,
     reduce(acc = [], entry IN eventActorMap |
       CASE WHEN entry.event.subtype IN acc THEN acc ELSE acc + entry.event.subtype END
     ) AS eventSubtypes

// Compute aggregates
WITH rootPlace, periodStart, periodEnd, eventActorMap, distinctActors, eventTypes, eventSubtypes,

     size(eventActorMap) AS LocalEventCount,
     size(distinctActors) AS LocalDistinctActorCount,
     size(eventTypes) AS LocalDistinctEventTypes,
     size(eventSubtypes) AS LocalDistinctEventSubtypes,

     reduce(total = 0, entry IN eventActorMap |
       total + coalesce(entry.event.fatalities, 0)
     ) AS LocalTotalFatalities,

     [entry IN eventActorMap WHERE entry.event.severity IS NOT NULL | entry.event.severity] AS severityList,

     // Actor-type event counts (max 1 per event per type)
     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Protesters"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalProtestersEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""State forces"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalStateForcesEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Political militia"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalPoliticalMilitiaEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Identity militia"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalIdentityMilitiaEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Rebel group"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalRebelGroupEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Rioters"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalRiotersEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Civilians"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalCiviliansEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""External/Other forces"")
            THEN cnt + 1 ELSE cnt END
     ) AS LocalOtherTypeEventCount

RETURN 
  rootPlace.country AS country,
  rootPlace.name AS name,
  rootPlace.location.latitude AS latitude,
  rootPlace.location.longitude AS longitude,
  rootPlace.minBorderDistanceKm AS minBorderDistanceKm,
  rootPlace.minCapitalDistanceKm AS minCapitalDistanceKm,
  periodStart,
  periodEnd,
  LocalEventCount,
  LocalDistinctEventTypes,
  LocalDistinctEventSubtypes,
  LocalTotalFatalities,
  LocalDistinctActorCount,
  CASE WHEN size(severityList) = 0
       THEN NULL
       ELSE reduce(s = 0, v IN severityList | s + v) * 1.0 / size(severityList)
  END AS LocalMeanSeverity,
  CASE WHEN size(severityList) = 0
     THEN NULL
     ELSE reduce(m = severityList[0], v IN severityList |
                 CASE WHEN v > m THEN v ELSE m END)
   END AS LocalMaxSeverity,
  LocalProtestersEventCount,
  LocalStateForcesEventCount,
  LocalPoliticalMilitiaEventCount,
  LocalIdentityMilitiaEventCount,
  LocalRebelGroupEventCount,
  LocalRiotersEventCount,
  LocalCiviliansEventCount,
  LocalOtherTypeEventCount;

";


                await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));


                var cursor = await session.RunAsync(cypher, parameters);

                while (await cursor.FetchAsync())
                {
                    var record = cursor.Current;

                    var row = new Dictionary<string, string>
                    {

                        ["name"] = record["name"].As<string>(),
                        ["country"] = record["country"].As<string>(),
                        ["latitude"] = record["latitude"].As<string>(),
                        ["longitude"] = record["longitude"].As<string>(),
                        ["longitude"] = record["longitude"].As<string>(),
                        ["minBorderDistanceKm"] = record["minBorderDistanceKm"].As<string>(),
                        ["minCapitalDistanceKm"] = record["minCapitalDistanceKm"].As<string>(),

                        ["periodStart"] = record["periodStart"].As<string>(),
                        ["periodEnd"] = record["periodEnd"].As<string>(),

                        ["LocalEventCount"] = Normalise(record["LocalEventCount"].As<string>()),
                        ["LocalDistinctActorCount"] = Normalise(record["LocalDistinctActorCount"].As<string>()),
                        ["LocalDistinctEventTypes"] = Normalise(record["LocalDistinctEventTypes"].As<string>()),
                        ["LocalDistinctEventSubtypes"] = Normalise(record["LocalDistinctEventSubtypes"].As<string>()),
                        ["LocalTotalFatalities"] = Normalise(record["LocalTotalFatalities"].As<string>()),
                        ["LocalMeanSeverity"] = Normalise(record["LocalMeanSeverity"].As<string>()),
                        ["LocalMaxSeverity"] = Normalise(record["LocalMaxSeverity"].As<string>()),

                        ["LocalProtestersEventCount"] = Normalise(record["LocalProtestersEventCount"].As<string>()),
                        ["LocalStateForcesEventCount"] = Normalise(record["LocalStateForcesEventCount"].As<string>()),
                        ["LocalPoliticalMilitiaEventCount"] = Normalise(record["LocalPoliticalMilitiaEventCount"].As<string>()),
                        ["LocalIdentityMilitiaEventCount"] = Normalise(record["LocalIdentityMilitiaEventCount"].As<string>()),
                        ["LocalRebelGroupEventCount"] = Normalise(record["LocalRebelGroupEventCount"].As<string>()),
                        ["LocalRiotersEventCount"] = Normalise(record["LocalRiotersEventCount"].As<string>()),
                        ["LocalCiviliansEventCount"] = Normalise(record["LocalCiviliansEventCount"].As<string>()),
                        ["LocalOtherTypeEventCount"] = Normalise(record["LocalOtherTypeEventCount"].As<string>()),

                    };

                    results.Add(row);

                }
                int afterLoadCount = results.Count();

                Console.WriteLine($"Loaded {afterLoadCount - beforeLoadCount} for {afterLoadCount} local results; period: {period.start.ToString()} to {period.end.ToString()}");
                session.Dispose();
            }


            return results;
        }


        /// <summary>
        /// Returns values for a tabular classical ML dataset. 
        /// </summary>
        /// <param name="kgName"></param>
        /// <param name="placeName"></param>
        /// <param name="startDate"></param>
        /// <param name="frequencyPeriod"></param>
        /// <returns>List of dictionary where keys are column names, values are values.</returns>
        /// <remarks>CRITICAL: When training for classical ML, you MUST remove places within the place being trained for as the dataset will include these and is not aware of Place-WITHIN-PLACE relationships. Note also that Regional... features exlude the local features, and local features will include the WITHIN places. </remarks>
        public async Task<List<Dictionary<string, string>>> BuildBaseRegionalDatasetAsync(
    string kgName,
    string placeName,
    DateTime startDate,
    string frequencyPeriod)
        {

            var results = new List<Dictionary<string, string>>();


            List<(DateTime start, DateTime end)> periods = LoadPeriods(startDate, endDate: null, frequencyPeriod);


            foreach (var period in periods)
            {

                int beforeLoadCount = results.Count();
                var parameters = new Dictionary<string, object>
                {
                    ["periodStart"] = new LocalDateTime(period.start),//startDateStr,
                    ["periodEnd"] = new LocalDateTime(period.end),
                    ["placeName"] = string.Empty,
                    ["radiusKm"] = 150 // Convert 50 km to meters for Neo4j distance function
            
                };

                string cypher = @"

WITH
  $radiusKm AS radiusKM, 
  $periodStart AS periodStart, 
  $periodEnd AS periodEnd,
  $placeName AS placeName

// Root places
MATCH (rootPlace:Place)
WHERE ((rootPlace.name = placeName OR placeName = '' OR placeName IS NULL) AND rootPlace.name <> rootPlace.country)

// Regional events: 10–50 km from place
OPTIONAL MATCH (regionalEvents:Event)
WHERE regionalEvents.datetime >= periodStart
  AND regionalEvents.datetime < periodEnd
  AND point.distance(regionalEvents.location, rootPlace.location) >= 10000
  AND point.distance(regionalEvents.location, rootPlace.location) < radiusKM * 1000

// Load actors per event
OPTIONAL MATCH (regionalActors:Actor)-[:INVOLVED_IN]->(regionalEvents)

// Build per-event actor list
WITH rootPlace, periodStart, periodEnd,
     regionalEvents,
     collect(regionalActors) AS actorsForEvent

// Build event→actor map
WITH rootPlace, periodStart, periodEnd,
     collect({
       event: regionalEvents,
       actors: actorsForEvent
     }) AS rawEventActorMap

// Remove null-event entries
WITH rootPlace, periodStart, periodEnd,
     [e IN rawEventActorMap WHERE e.event IS NOT NULL] AS eventActorMap

// Flatten actors
WITH rootPlace, periodStart, periodEnd, eventActorMap,
     reduce(acc = [], entry IN eventActorMap | acc + entry.actors) AS allActorsFlat

// Distinct actors
WITH rootPlace, periodStart, periodEnd, eventActorMap, allActorsFlat,
     reduce(acc = [], x IN allActorsFlat |
       CASE WHEN x IN acc THEN acc ELSE acc + x END
     ) AS distinctActors

// Distinct event types/subtypes
WITH rootPlace, periodStart, periodEnd, eventActorMap, distinctActors,
     reduce(acc = [], entry IN eventActorMap |
       CASE WHEN entry.event.type IN acc THEN acc ELSE acc + entry.event.type END
     ) AS eventTypes,
     reduce(acc = [], entry IN eventActorMap |
       CASE WHEN entry.event.subtype IN acc THEN acc ELSE acc + entry.event.subtype END
     ) AS eventSubtypes

// Compute aggregates
WITH rootPlace, periodStart, periodEnd, eventActorMap, distinctActors, eventTypes, eventSubtypes,

     size(eventActorMap) AS RegionalEventCount,
     size(distinctActors) AS RegionalDistinctActorCount,
     size(eventTypes) AS RegionalDistinctEventTypes,
     size(eventSubtypes) AS RegionalDistinctEventSubTypes,

     reduce(total = 0, entry IN eventActorMap |
       total + coalesce(entry.event.fatalities, 0)
     ) AS RegionalTotalFatalities,

     [entry IN eventActorMap WHERE entry.event.severity IS NOT NULL | entry.event.severity] AS severityList,

     // Actor-type event counts (max 1 per event per type)
     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Protesters"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalProtestersEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""State forces"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalStateForcesEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Political militia"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalPoliticalMilitiaEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Identity militia"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalIdentityMilitiaEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Rebel group"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalRebelGroupEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Rioters"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalRiotersEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""Civilians"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalCiviliansEventCount,

     reduce(cnt = 0, entry IN eventActorMap |
       CASE WHEN ANY(a IN entry.actors WHERE a.type = ""External/Other forces"")
            THEN cnt + 1 ELSE cnt END
     ) AS RegionalOtherTypeEventCount

RETURN
  rootPlace.country AS country,
  rootPlace.name AS name,
  rootPlace.location.latitude AS latitude,
  rootPlace.location.longitude AS longitude,
  periodStart,
  periodEnd,
  RegionalEventCount,
  RegionalDistinctEventTypes,
  RegionalDistinctEventSubTypes,
  RegionalTotalFatalities,
  RegionalDistinctActorCount,
  CASE WHEN size(severityList) = 0
       THEN NULL
       ELSE reduce(s = 0, v IN severityList | s + v) * 1.0 / size(severityList)
  END AS RegionalMeanSeverity,
CASE WHEN size(severityList) = 0
     THEN NULL
     ELSE reduce(m = severityList[0], v IN severityList |
                 CASE WHEN v > m THEN v ELSE m END)
END AS RegionalMaxSeverity,
  RegionalProtestersEventCount,
  RegionalStateForcesEventCount,
  RegionalPoliticalMilitiaEventCount,
  RegionalIdentityMilitiaEventCount,
  RegionalRebelGroupEventCount,
  RegionalRiotersEventCount,
  RegionalCiviliansEventCount,
  RegionalOtherTypeEventCount;

                ";



                await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));


                var cursor = await session.RunAsync(cypher, parameters);

                while (await cursor.FetchAsync())
                {
                    var record = cursor.Current;

                    var row = new Dictionary<string, string>
                    {

                        ["name"] = record["name"].As<string>(),
                        ["country"] = record["country"].As<string>(),
                        //["minBorderDistanceKm"] = record["minBorderDistanceKm"].As<string>(),
                        // ["minCapitalDistanceKm"] = record["minCapitalDistanceKm"].As<string>(),

                        ["periodStart"] = record["periodStart"].As<string>(),
                        ["periodEnd"] = record["periodEnd"].As<string>(),

                        ["RegionalEventCount"] = Normalise(record["RegionalEventCount"].As<string>()),
                        ["RegionalDistinctActorCount"] = Normalise(record["RegionalDistinctActorCount"].As<string>()),
                        ["RegionalDistinctEventTypes"] = Normalise(record["RegionalDistinctEventTypes"].As<string>()),
                        ["RegionalDistinctEventSubTypes"] = Normalise(record["RegionalDistinctEventSubTypes"].As<string>()),
                        ["RegionalTotalFatalities"] = Normalise(record["RegionalTotalFatalities"].As<string>()),
                        ["RegionalMeanSeverity"] = Normalise(record["RegionalMeanSeverity"].As<string>()),
                        ["RegionalMaxSeverity"] = Normalise(record["RegionalMaxSeverity"].As<string>()),
                       
                        ["RegionalProtestersEventCount"] = Normalise(record["RegionalProtestersEventCount"].As<string>()),
                        ["RegionalStateForcesEventCount"] = Normalise(record["RegionalStateForcesEventCount"].As<string>()),
                        ["RegionalPoliticalMilitiaEventCount"] = Normalise(record["RegionalPoliticalMilitiaEventCount"].As<string>()),
                        ["RegionalIdentityMilitiaEventCount"] = Normalise(record["RegionalIdentityMilitiaEventCount"].As<string>()),
                        ["RegionalCiviliansEventCount"] = Normalise(record["RegionalCiviliansEventCount"].As<string>()),
                        ["RegionalRebelGroupEventCount"] = Normalise(record["RegionalRebelGroupEventCount"].As<string>()),
                        ["RegionalRiotersEventCount"] = Normalise(record["RegionalRiotersEventCount"].As<string>()),
                        ["RegionalOtherTypeEventCount"] = Normalise(record["RegionalOtherTypeEventCount"].As<string>())
                    };

                    results.Add(row);

                }
                int afterLoadCount = results.Count();

                Console.WriteLine($"Loaded {afterLoadCount - beforeLoadCount} for {afterLoadCount} regional results; period: {period.start.ToString()} to {period.end.ToString()}");
                session.Dispose();
            }


            return results;
        }


        /// <summary>
        /// Sets values to string "0" if they are null or empty, otherwise returns the original value. This is useful for ensuring that numeric fields in the dataset are not null or empty, which can cause issues during machine learning model training.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private string Normalise(string value)
        {
            return string.IsNullOrEmpty(value) ? "0" : value;
        }

        /// <summary>
        /// Returns values for a tabular classical ML dataset. 
        /// </summary>
        /// <param name="kgName"></param>
        /// <param name="placeName"></param>
        /// <param name="startDate"></param>
        /// <param name="frequencyPeriod"></param>
        /// <returns>List of dictionary where keys are column names, values are values.</returns>
        /// <remarks>CRITICAL: When training for classical ML, you MUST remove places within the place being trained for as the dataset will include these and is not aware of Place-WITHIN-PLACE relationships. Note also that Regional... features exlude the local features, and local features will include the WITHIN places. </remarks>
        public async Task<List<Dictionary<string, string>>> BuildFactDataset(
    string kgName,
    string? country,
    int startYear,
    int endYear)
        {


            var parameters = new Dictionary<string, object>
            {
                ["startYear"] = startYear > 0 ? startYear : null,
                ["endYear"] = endYear > 0 ? endYear : null,
                ["country"] = string.IsNullOrWhiteSpace(country) ? null : country
            };


            string cypher = @"
// Get all top-level places (countries)
MATCH (country:Place)
WHERE NOT (country)-[:WITHIN]->(:Place)

// Optional country filter
AND ($country IS NULL OR country.name = $country)

// Get all facts for each country
MATCH (country)-[:HAS_FACT]->(fact:Fact)

// Optional year filters
WHERE ($startYear IS NULL OR fact.year >= $startYear)
  AND ($endYear   IS NULL OR fact.year <= $endYear)

// Group facts by country + year + subkey
WITH 
    country.name AS country,
    fact.year AS year,
    fact.subkey AS subkey,
    collect(fact.values) AS values

RETURN
    country,
    year,
    subkey,
    values
ORDER BY country, year, subkey;


";

            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));

            var results = new List<Dictionary<string, string>>();
            var cursor = await session.RunAsync(cypher, parameters);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;

                var row = new Dictionary<string, string>
                {
                    ["country"] = record["country"].As<string>(),
                    ["year"] = record["year"].As<string>(),
                    ["subkey"] = record["subkey"].As<string>(),
                    ["values"] = ReadValuesField(record),
                };

                results.Add(row);
            }


            return results;
        }
        private static string ReadValuesField(IRecord record)
        {
            var raw = record["values"];

            // Case 1: null
            if (raw == null)
                return "";

            // Case 2: simple string
            if (raw is string s)
                return s;

            // Case 3: list of lists (nested list)
            if (raw is IList<object> outerList &&
                outerList.Count > 0 &&
                outerList[0] is IList<object>)
            {
                // Flatten nested lists: [["a","b"],["c"]] → "a|b|c"
                var flattened = outerList
                    .Cast<IList<object>>()
                    .SelectMany(inner => inner.Select(v => v?.ToString() ?? ""))
                    .ToList();

                return string.Join("|", flattened);
            }

            // Case 4: list of objects (normal list)
            if (raw is IList<object> list)
            {
                var converted = list.Select(v =>
                {
                    if (v == null) return "";
                    if (v is string vs) return vs;
                    return v.ToString();
                });

                return string.Join("|", converted);
            }

            // Case 5: scalar number or other type
            return raw.ToString();
        }

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string cypher, Dictionary<string, object> parameters, string kgName)
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));
            var results = new List<Dictionary<string, object>>();


            var cursor = await session.RunAsync(cypher, parameters);

            while (await cursor.FetchAsync())
            {
                var record = cursor.Current;
                var row = new Dictionary<string, object>();

                foreach (var key in record.Keys)
                {
                    row[key] = ConvertValue(record[key]);
                }

                results.Add(row);
            }

            return results;
        }


        /// <summary>
        /// Uses cosine similarity on event summaries to find events that are similar to the user's prompt based on the provided embedding. Returns a list of event summaries that have a cosine similarity greater than 0.7 with the user's prompt embedding, ordered by similarity. The number of results returned can be limited by the optional topN parameter.
        /// </summary>
        /// <param name="embeddingFromUserPrompt"></param>
        /// <param name="kgName"></param>
        /// <param name="topN"></param>
        /// <returns>List of string.</returns>
        public async Task<List<string>> LoadSimilarEvents(double[] embeddingFromUserPrompt, string kgName, int topN = 10, string placeName = "", double? requiredCosineSimilarity = 0.7)
        {
            List<string> results = new List<string>();

            try
            {
                await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));

                string cypher = @"
                MATCH (e:Event)-[:OCCURRED_AT]->(p:Place)
                WHERE ($placeName = '' OR toUpper(p.name) CONTAINS toUpper($placeName))
                  AND vector.similarity.cosine(e.embedding, $userPromptEmbedding) >= $requiredCosineSimilarity
                RETURN e.summary AS summary, 
                    vector.similarity.cosine(e.embedding, $userPromptEmbedding) as similarity,
                    p.name AS placeName,
                    e.datetime AS eventDate 
                ORDER BY vector.similarity.cosine(e.embedding, $userPromptEmbedding) DESC
                LIMIT $topN
            ";

                Dictionary<string, object> parameters = new Dictionary<string, object>
                {

                    ["userPromptEmbedding"] = embeddingFromUserPrompt,
                    ["placeName"] = !string.IsNullOrWhiteSpace(placeName) ? placeName : string.Empty,   //inline if, placeName or empty string
                    ["requiredCosineSimilarity"] = requiredCosineSimilarity ?? 0.7,
                    ["topN"] = topN
                };

                var cursor = await session.RunAsync(cypher, parameters);

    
                while (await cursor.FetchAsync())
                {
                    var record = cursor.Current;
                    var row = new Dictionary<string, object>();

                    foreach (var key in record.Keys)
                    {
                        row[key] = ConvertValue(record[key]);
                    }
                    results.Add($"{row["eventDate"].ToString()}: {row["summary"].ToString()} (similarity: {row["similarity"]})");

                }

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
            return results;
        }


        #region "Conversion Methods"
        private object ConvertValue(object value)
        {
            switch (value)
            {
                case INode node:
                    return ConvertNode(node);

                case IRelationship rel:
                    return ConvertRelationship(rel);

                case IPath path:
                    return ConvertPath(path);

                case IList<object> list:
                    return list.Select(ConvertValue).ToList();

                default:
                    return value; // primitives (string, int, double, bool, etc.)
            }
        }

        private Dictionary<string, object> ConvertNode(INode node)
        {
            return new Dictionary<string, object>
            {
                ["id"] = node.Id,
                ["labels"] = node.Labels.ToList(),
                ["properties"] = node.Properties.ToDictionary(k => k.Key, v => v.Value)
            };
        }

        private Dictionary<string, object> ConvertRelationship(IRelationship rel)
        {
            return new Dictionary<string, object>
            {
                ["id"] = rel.Id,
                ["type"] = rel.Type,
                ["startNodeId"] = rel.StartNodeId,
                ["endNodeId"] = rel.EndNodeId,
                ["properties"] = rel.Properties.ToDictionary(k => k.Key, v => v.Value)
            };
        }

        private Dictionary<string, object> ConvertPath(IPath path)
        {
            return new Dictionary<string, object>
            {
                ["nodes"] = path.Nodes.Select(ConvertNode).ToList(),
                ["relationships"] = path.Relationships.Select(ConvertRelationship).ToList()
            };
        }

        #endregion


    }

}
