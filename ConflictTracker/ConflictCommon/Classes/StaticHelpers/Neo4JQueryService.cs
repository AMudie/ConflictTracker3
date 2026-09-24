using Neo4j.Driver;
using System.Data;
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
    string? placeName,
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
with

$radiusKm as radiusKM, 
$periodStart as periodStart, 
$periodEnd as periodEnd,
$placeName as placeName

//start with places as the root, just for the country and name
match (rootPlace:Place)
where (rootPlace.name = placeName or placeName = '' or placeName is null)

//Get events that OCCURRED_AT those places, within the period
with rootPlace, periodStart, periodEnd, radiusKM
Optional match (rootPlace)<-[:OCCURRED_AT]-(rootEvent:Event)
where rootEvent.datetime >= periodStart and rootEvent.datetime < periodEnd


//Disregard those event nodes, and just carry forward the collection of event Ids 
with rootEvent, rootPlace, radiusKM, periodStart, periodEnd, collect (elementid(rootEvent)) as rootEventIds
with rootPlace, radiusKM, periodStart, periodEnd, collect (elementid(rootEvent)) as rootEventIds
Optional MATCH (rootEvents:Event)
where elementId(rootEvents) in rootEventIds


//Get events nearby those events (10KM)
optional match (nearbyEvents:Event)
where nearbyEvents.datetime >= periodStart and nearbyEvents.datetime < periodEnd
and point.distance(nearbyEvents.location, rootEvents.location) < radiusKM * 1000
and (elementid(nearbyEvents) in rootEventIds = FALSE)
with rootPlace, radiusKM, periodStart, periodEnd, rootEventIds, collect(elementid(nearbyEvents)) as nearbyEventIds


//We now have two collections of event Ids in rootEventIds and nearbyEventIds. 
//Need to unwind then collect distinct to remove duplicates to avoid double counting. 

//Now we can load the local events as nodes, and begin querying. 
with 
rootPlace, radiusKM, periodStart, periodEnd,  rootEventIds, nearbyEventIds, coll.distinct(rootEventIds + nearbyEventIds) as allEventIds
optional MATCH(localEvents:Event)
where elementId(localEvents) in allEventIds 

//now load the actors associated with those events
with rootPlace, radiusKM, periodStart, periodEnd,  rootEventIds, nearbyEventIds, allEventIds, localEvents
Optional match (localActors:Actor)-[:INVOLVED_IN]->(localEvents)

//Distinct the actors:
with  rootPlace, periodStart, periodEnd, rootEventIds, nearbyEventIds, allEventIds, localEvents, collect(distinct localActors) as localActors



with
rootPlace, periodStart, periodEnd, 
count(localEvents) as LocalEventCount,
count(localActors) AS LocalDistinctActorCount,
count(DISTINCT localEvents.type) AS LocalDistinctEventTypes,
count(DISTINCT localEvents.subtype) AS LocalDistinctEventSubtypes,
sum(localEvents.fatalities) AS LocalTotalFatalities,
    CASE 
        WHEN count(localEvents.severity) = 0 THEN NULL
       ELSE sum(localEvents.severity) * 1.0 / count(localEvents.severity)
    END AS LocalAvgSeverity,
Sum(CASE 
  WHEN ANY(a IN localActors WHERE a.type = ""Protesters"") 
  THEN 1 
  ELSE 0 
END)AS LocalProtestersEventCount,
Sum(CASE 
  WHEN ANY(a IN localActors WHERE a.type = ""State forces"") 
  THEN 1 
  ELSE 0 
END) AS LocalStateForcesEventCount,
Sum(CASE
  WHEN ANY(a IN localActors WHERE a.type = ""Political militia"") 
  THEN 1 
  ELSE 0 
END) AS LocalPoliticalMilitiaEventCount,
Sum(CASE 
  WHEN ANY(a IN localActors WHERE a.type = ""Identity militia"") 
  THEN 1 
  ELSE 0 
END) AS LocalIdentityMilitiaEventCount,
Sum(CASE
  WHEN ANY(a IN localActors WHERE a.type = ""Rebel group"") 
  THEN 1 
  ELSE 0 
END) AS LocalRebelGroupEventCount,
Sum(CASE
  WHEN ANY(a IN localActors WHERE a.type = ""Rioters"") 
  THEN 1 
  ELSE 0 
END) AS LocalRiotersEventCount,
Sum(CASE
  WHEN ANY(a IN localActors WHERE a.type = ""Civilians"") 
  THEN 1 
  ELSE 0 
END) AS LocalCiviliansEventCount,
Sum(CASE
  WHEN ANY(a IN localActors WHERE a.type = ""External/Other forces"") 
  THEN 1 
  ELSE 0 
END) AS LocalOtherTypeEventCount

//return with aliases:
return rootPlace.country as country, rootPlace.name as name, rootPlace.location.latitude as latitude, rootPlace.location.longitude as longitude, rootPlace.minBorderDistanceKm as minBorderDistanceKm, rootPlace.minCapitalDistanceKm as minCapitalDistanceKm, periodStart, periodEnd, LocalEventCount, LocalDistinctEventTypes, LocalDistinctEventSubtypes, LocalTotalFatalities, LocalDistinctActorCount, LocalAvgSeverity, LocalProtestersEventCount, LocalStateForcesEventCount, LocalPoliticalMilitiaEventCount,LocalIdentityMilitiaEventCount, LocalRebelGroupEventCount, LocalRiotersEventCount, LocalCiviliansEventCount, LocalOtherTypeEventCount

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
                        ["LocalAvgSeverity"] = Normalise(record["LocalAvgSeverity"].As<string>()),

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
    string? placeName,
    DateTime startDate,
    string frequencyPeriod)
        {

            var results = new List<Dictionary<string, string>>();


            List<(DateTime start, DateTime end)> periods = LoadPeriods(startDate, endDate:null, frequencyPeriod);


            foreach (var period in periods)
            {

                int beforeLoadCount = results.Count();
                var parameters = new Dictionary<string, object>
                {
                    ["periodStart"] = new LocalDateTime(period.start),//startDateStr,
                    ["periodEnd"] = new LocalDateTime(period.end),
                    ["placeName"] = string.Empty,
                    ["radiusKm"] = 50 // Convert 50 km to meters for Neo4j distance function
                };

                string cypher = @"

with

$radiusKm as radiusKM, 
$periodStart as periodStart, 
$periodEnd as periodEnd,
$placeName as placeName

//start with places as the root, just for the country and name
match (rootPlace:Place)
where (rootPlace.name = placeName or placeName = '' or placeName is null)

//Get events that OCCURRED_AT those places, within the period
with rootPlace, periodStart, periodEnd, radiusKM
Optional match (rootPlace)<-[:OCCURRED_AT]-(regionalEvents:Event)
where 
(
regionalEvents is null 
or ((regionalEvents.datetime >= periodStart 
and regionalEvents.datetime < periodEnd)
and (point.distance(regionalEvents.location, rootPlace.location) >= 10000 
and 
point.distance(regionalEvents.location, rootPlace.location) < radiusKM * 1000))
)



//now load the regional actors for those events. 
with rootPlace, periodStart, periodEnd, radiusKM, regionalEvents
optional match  (regionalActors:Actor)-[:INVOLVED_IN]->(regionalEvents)

with rootPlace, periodStart, periodEnd, radiusKM, collect(distinct regionalEvents) as regionalEvents, collect(distinct regionalActors) as regionalActors

with rootPlace, periodStart, periodEnd, radiusKM, regionalEvents, regionalActors

with rootPlace, periodStart, periodEnd,  collect(distinct regionalEvents) as regionalEvents, regionalActors,
size(regionalEvents) as RegionalEventCount,
size(regionalActors) AS RegionalDistinctActorCount,
size(coll.distinct([e IN regionalEvents | e.type]))AS RegionalDistinctEventTypes,
size(coll.distinct([e IN regionalEvents | e.subtype])) AS RegionalDistinctEventSubTypes,
reduce(total = 0, e IN regionalEvents | total + coalesce(e.fatalities, 0))
    AS RegionalTotalFatalities,
CASE 
    WHEN size([e IN regionalEvents WHERE e.severity IS NOT NULL]) = 0 THEN 0
    ELSE reduce(total = 0, e IN regionalEvents | total + coalesce(e.severity, 0)) * 1.0 /
         size([e IN regionalEvents WHERE e.severity IS NOT NULL])
END AS RegionalAvgSeverity,
size([a IN regionalActors WHERE a.type = ""Protesters""]) AS RegionalProtestersEventCount,
size([a IN regionalActors WHERE a.type = ""State forces""]) AS RegionalStateForcesEventCount,
size([a IN regionalActors WHERE a.type = ""Political militia""]) AS RegionalPoliticalMilitiaEventCount,
size([a IN regionalActors WHERE a.type = ""Identity militia""]) AS RegionalIdentityMilitiaEventCount,
size([a IN regionalActors WHERE a.type = ""Rebel group""]) AS RegionalRebelGroupEventCount,
size([a IN regionalActors WHERE a.type = ""Rioters""]) AS RegionalRiotersEventCount,
size([a IN regionalActors WHERE a.type = ""Civilians""]) AS RegionalCiviliansEventCount,
size([a IN regionalActors WHERE a.type = ""External/Other forces""]) AS RegionalOtherTypeEventCount


//return with aliases:
return rootPlace.country as country, rootPlace.name as name, rootPlace.location.latitude as latitude, rootPlace.location.longitude as longitude, periodStart, periodEnd, RegionalEventCount, RegionalDistinctEventTypes, RegionalDistinctEventSubTypes, RegionalTotalFatalities, RegionalDistinctActorCount, RegionalAvgSeverity, RegionalProtestersEventCount, RegionalStateForcesEventCount, RegionalPoliticalMilitiaEventCount,RegionalIdentityMilitiaEventCount, RegionalRebelGroupEventCount, RegionalRiotersEventCount, RegionalCiviliansEventCount, RegionalOtherTypeEventCount





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



                     //   ["PlacesWithinRegion"] = Normalise(record["PlacesWithinRegion"].As<string>()),
                        ["RegionalEventCount"] = Normalise(record["RegionalEventCount"].As<string>()),
                        ["RegionalDistinctActorCount"] = Normalise(record["RegionalDistinctActorCount"].As<string>()),
                        ["RegionalDistinctEventTypes"] = Normalise(record["RegionalDistinctEventTypes"].As<string>()),
                        ["RegionalDistinctEventSubTypes"] = Normalise(record["RegionalDistinctEventSubTypes"].As<string>()),
                        ["RegionalTotalFatalities"] = Normalise(record["RegionalTotalFatalities"].As<string>()),
                        ["RegionalAvgSeverity"] = Normalise(record["RegionalAvgSeverity"].As<string>()),
                        // ["RegionalDaysSinceLastEvent"] = Normalise(record["RegionalDaysSinceLastEvent"].As<string>())

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

        public  async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string cypher, Dictionary<string, object> parameters,string kgName)
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
