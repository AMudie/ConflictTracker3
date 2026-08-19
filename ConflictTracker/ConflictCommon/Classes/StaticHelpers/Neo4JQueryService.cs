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

        private List<(DateTime start, DateTime end)> LoadPeriods(DateTime startDate, DateTime endDate, string frequency)
        {
            List<(DateTime start, DateTime end)> periods = new List<(DateTime start, DateTime end)>();
            DateTime current = startDate;

            while (current <= endDate)
            {
                var next = frequency switch
                {
        
                    "Monthly" => current.AddDays(28),
                    "Quarterly" => current.AddMonths(84)
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
        /// <remarks>CRITICAL: When training for classical ML, you MUST remove places within the place being trained for as the dataset will include these and is not aware of Place-WITHIN-PLACE relationships. Note also that Regional... features exlude the local features, and local features will include the WITHIN places. </remarks>
        public async Task<List<Dictionary<string, string>>> BuildBaseLocalDatasetAsync(
    string kgName,
    string? placeName,
    DateTime startDate,
    string frequencyPeriod)
        {

            var results = new List<Dictionary<string, string>>();
           

            List<(DateTime start, DateTime end)> periods = LoadPeriods(startDate, new DateTime(2026, 07, 28), frequencyPeriod);


            foreach (var period in periods)
            {

                int beforeLoadCount = results.Count();
                var parameters = new Dictionary<string, object>
                {
                    ["startDate"] = new LocalDate(period.start),//startDateStr,
                    ["endDate"] = new LocalDate(period.end),
                    ["placeName"] = placeName,//,
                    ["radiusKm"] = 50 * 1000 // Convert 50 km to meters for Neo4j distance function
                };

                string cypher = @"
WITH 
    $startDate AS startDate,
    $endDate AS endDate,
    $placeName AS placeName,
    $radiusKm AS radiusKm

// 1. Determine allowed places
OPTIONAL MATCH (root:Place {name: placeName})
OPTIONAL MATCH (root)-[:WITHIN*0..]->(child:Place)
WITH startDate, endDate, placeName, radiusKm,
     CASE WHEN placeName = '' THEN [] ELSE collect(child) END AS allowedPlaces

// 2. CROSS JOIN places (but filtered)
MATCH (p:Place)
WHERE placeName = '' OR p IN allowedPlaces

WITH p, startDate, endDate, allowedPlaces, radiusKm

// 3. Local events inside the period
OPTIONAL MATCH (e:Event)-[:OCCURRED_AT]->(p)
WHERE date(e.datetime) >= startDate AND date(e.datetime) < endDate

OPTIONAL MATCH (actor:Actor)-[:INVOLVED_IN]->(e)

WITH p, startDate, endDate, allowedPlaces, radiusKm,
     e, actor,
     e.type AS eventType,
     e.subtype AS eventSubtype,
     e.fatalities AS fatalities,
     e.severity AS severity,
     date(e.datetime) AS eventDate
    

// 4. Previous local events
OPTIONAL MATCH (prevEvent:Event)-[:OCCURRED_AT]->(subPlace:Place)
WHERE subPlace IN allowedPlaces
  AND date(prevEvent.datetime) < startDate

WITH p, startDate, endDate, allowedPlaces, radiusKm,
     e, actor, eventType, eventSubtype, fatalities, severity,
     collect(date(prevEvent.datetime)) AS prevDates

WITH p, startDate, endDate, allowedPlaces, radiusKm,
     e, actor, eventType, eventSubtype, fatalities, severity,
     CASE
         WHEN size(prevDates) = 0 THEN NULL
         ELSE reduce(latest = prevDates[0], d IN prevDates |
                     CASE WHEN d > latest THEN d ELSE latest END)
     END AS lastEventDate


RETURN
    p.name AS place, 
    p.country AS country,
    p.location.latitude AS latitude,
    p.location.longitude AS longitude,
    p.minBorderDistanceKm AS minBorderDistanceKm,
    p.minCapitalDistanceKm AS minCapitalDistanceKm, 
    startDate AS periodStart,
    endDate AS periodEnd,
    count(e) AS LocalEventCount,
    count(DISTINCT actor) AS LocalUniqueActorCount,
    count(DISTINCT eventType) AS LocalDistinctEventTypes,
    count(DISTINCT eventSubtype) AS LocalDistinctEventSubtypes,
    sum(fatalities) AS LocalTotalFatalities,
    CASE 
        WHEN count(severity) = 0 THEN NULL
        ELSE sum(severity) * 1.0 / count(severity)
    END AS LocalAvgSeverity,
    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Protesters""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalProtestersEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""State forces""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalStateForcesEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Political militia""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalPoliticalMilitiaEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Identity militia""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalIdentityMilitiaEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Rebel group""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalRebelGroupEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Rioters""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalRiotersEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""Civilians""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalCiviliansEventCount,
	    count(
        DISTINCT CASE 
            WHEN EXISTS {
                MATCH (:Actor {type: ""External/Other forces""})-[:INVOLVED_IN]->(e)
            }
            THEN e
            ELSE NULL
        END
    ) AS LocalOtherTypeEventCount

";


                await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));


                var cursor = await session.RunAsync(cypher, parameters);

                while (await cursor.FetchAsync())
                {
                    var record = cursor.Current;

                    var row = new Dictionary<string, string>
                    {

                        ["place"] = record["place"].As<string>(),
                        ["country"] = record["country"].As<string>(),
                        ["latitude"] = record["latitude"].As<string>(),
                        ["longitude"] = record["longitude"].As<string>(),
                        ["longitude"] = record["longitude"].As<string>(),
                        ["minBorderDistanceKm"] = record["minBorderDistanceKm"].As<string>(),
                        ["minCapitalDistanceKm"] = record["minCapitalDistanceKm"].As<string>(),
               
                        ["periodStart"] = record["periodStart"].As<string>(),
                        ["periodEnd"] = record["periodEnd"].As<string>(),

                        ["LocalEventCount"] = Normalise(record["LocalEventCount"].As<string>()),
                        ["LocalUniqueActorCount"] = Normalise(record["LocalUniqueActorCount"].As<string>()),
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
                
                Console.WriteLine($"Loaded {beforeLoadCount} for {afterLoadCount} results; period: {period.start.ToString()} to {period.end.ToString()}");
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


            List<(DateTime start, DateTime end)> periods = LoadPeriods(startDate, new DateTime(2026, 07, 28), frequencyPeriod);


            foreach (var period in periods)
            {

                int beforeLoadCount = results.Count();
                var parameters = new Dictionary<string, object>
                {
                    ["startDate"] = new LocalDate(period.start),//startDateStr,
                    ["endDate"] = new LocalDate(period.end),
                    ["radiusKm"] = 50 * 1000 // Convert 50 km to meters for Neo4j distance function
                };

                //string cypher = @"
                //WITH 
                //    $startDate AS startDate,
                //    $endDate AS endDate,
                //    $radiusKm AS radiusKm

                //// 1. Get all events in the date range
                //MATCH (e:Event)
                //WHERE date(e.datetime) >= startDate
                //  AND date(e.datetime) <  endDate
                //MATCH (p:Place)<-[:OCCURRED_AT]-(e)

                //// 2. Find regional places for each event's place
                //MATCH (nearby:Place)
                //WHERE nearby <> p
                //  AND point.distance(p.location, nearby.location) <= radiusKm

                //// 3. Regional events (only those in the date range)
                //OPTIONAL MATCH (re:Event)-[:OCCURRED_AT]->(nearby)
                //WHERE date(re.datetime) >= startDate
                //  AND date(re.datetime) <  endDate

                //OPTIONAL MATCH (regionalActor:Actor)-[:INVOLVED_IN]->(re)

                //// 4. Aggregate by place
                //WITH p, startDate, endDate,
                //     count(DISTINCT re) AS RegionalEventCount,
                //     count(DISTINCT regionalActor) AS RegionalActorCount,
                //     count(DISTINCT re.type) AS RegionalDistinctEventTypes,
                //     count(DISTINCT re.subtype) AS RegionalDistinctEventSubTypes,
                //     sum(re.fatalities) AS RegionalTotalFatalities,
                //     CASE
                //         WHEN count(re.severity) = 0 THEN NULL
                //         ELSE sum(re.severity) * 1.0 / count(re.severity)
                //     END AS RegionalAvgSeverity,

                //     // Count events involving different actor types:
                // size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Protesters""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalProtestersEventCount,

                // size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""State forces""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalStateForcesEventCount,

                //  size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Political militia""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalPoliticalMilitiaEventCount,
                //                   size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Identity militia""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalIdentityMilitiaEventCount,
                //  size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Civilians""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalCiviliansEventCount,

                //  size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Rebel group""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalRebelGroupEventCount,

                //  size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""Rioters""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalRiotersEventCount,

                //  size([
                //     ev IN collect(DISTINCT re) WHERE
                //     EXISTS {
                //         MATCH (:Actor {type: ""External/Other forces""})-[:INVOLVED_IN]->(ev)
                //     }
                // ]) AS RegionalOtherTypeEventCount

                //RETURN
                //    p.name AS place,
                //    p.country as country,
                //    startDate AS periodStart,
                //    endDate AS periodEnd,
                //    RegionalEventCount,
                //    RegionalActorCount,
                //    RegionalDistinctEventTypes,
                //    RegionalDistinctEventSubTypes,
                //    RegionalTotalFatalities,
                //    RegionalAvgSeverity,
                //    RegionalProtestersEventCount,
                //    RegionalStateForcesEventCount,
                //    RegionalPoliticalMilitiaEventCount,
                //    RegionalIdentityMilitiaEventCount,
                //    RegionalCiviliansEventCount,
                //    RegionalRebelGroupEventCount,
                //    RegionalRiotersEventCount,
                //    RegionalOtherTypeEventCount
                //ORDER BY place;

                //";

                string cypher = @"
                WITH 
                    $startDate AS startDate,
                    $endDate AS endDate,
                    $radiusKm AS radiusKm

                // 1. Get all events in the date range

MATCH (p:Place)

OPTIONAL MATCH (e:Event)-[:OCCURRED_AT]->(p)
WHERE date(e.datetime) >= startDate 
  AND date(e.datetime) < endDate


                // 2. Find regional places for each event's place
                WITH p, e, startDate, endDate, radiusKm
MATCH (nearby:Place)
                WHERE nearby <> p
                  AND point.distance(p.location, nearby.location) <= radiusKm

                // 3. Regional events (only those in the date range)
                OPTIONAL MATCH (re:Event)-[:OCCURRED_AT]->(nearby)
                WHERE date(re.datetime) >= startDate
                  AND date(re.datetime) <  endDate

                OPTIONAL MATCH (regionalActor:Actor)-[:INVOLVED_IN]->(re)

                // 4. Aggregate by place
                WITH p, startDate, endDate,
                     count(DISTINCT re) AS RegionalEventCount,
                     count(DISTINCT regionalActor) AS RegionalActorCount,
                     count(DISTINCT re.type) AS RegionalDistinctEventTypes,
                     count(DISTINCT re.subtype) AS RegionalDistinctEventSubTypes,
                     sum(re.fatalities) AS RegionalTotalFatalities,
                     CASE
                         WHEN count(re.severity) = 0 THEN NULL
                         ELSE sum(re.severity) * 1.0 / count(re.severity)
                     END AS RegionalAvgSeverity,

                     // Count events involving different actor types:
                 size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Protesters""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalProtestersEventCount,

                 size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""State forces""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalStateForcesEventCount,

                  size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Political militia""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalPoliticalMilitiaEventCount,
                                   size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Identity militia""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalIdentityMilitiaEventCount,
                  size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Civilians""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalCiviliansEventCount,

                  size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Rebel group""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalRebelGroupEventCount,

                  size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""Rioters""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalRiotersEventCount,

                  size([
                     ev IN collect(DISTINCT re) WHERE
                     EXISTS {
                         MATCH (:Actor {type: ""External/Other forces""})-[:INVOLVED_IN]->(ev)
                     }
                 ]) AS RegionalOtherTypeEventCount

                RETURN
                    p.name AS place,
                    p.country as country,
                    startDate AS periodStart,
                    endDate AS periodEnd,
                    p.minBorderDistanceKm AS minBorderDistanceKm,
                    p.minCapitalDistanceKm AS minCapitalDistanceKm, 
                    RegionalEventCount,
                    RegionalActorCount,
                    RegionalDistinctEventTypes,
                    RegionalDistinctEventSubTypes,
                    RegionalTotalFatalities,
                    RegionalAvgSeverity,
                    RegionalProtestersEventCount,
                    RegionalStateForcesEventCount,
                    RegionalPoliticalMilitiaEventCount,
                    RegionalIdentityMilitiaEventCount,
                    RegionalCiviliansEventCount,
                    RegionalRebelGroupEventCount,
                    RegionalRiotersEventCount,
                    RegionalOtherTypeEventCount
                ORDER BY place;

                ";



                await using var session = _driver.AsyncSession(o => o.WithDatabase(kgName));


                var cursor = await session.RunAsync(cypher, parameters);

                while (await cursor.FetchAsync())
                {
                    var record = cursor.Current;

                    var row = new Dictionary<string, string>
                    {

                        ["place"] = record["place"].As<string>(),
                        ["country"] = record["country"].As<string>(),
                        ["minBorderDistanceKm"] = record["minBorderDistanceKm"].As<string>(),
                        ["minCapitalDistanceKm"] = record["minCapitalDistanceKm"].As<string>(),

                        ["periodStart"] = record["periodStart"].As<string>(),
                        ["periodEnd"] = record["periodEnd"].As<string>(),



                     //   ["PlacesWithinRegion"] = Normalise(record["PlacesWithinRegion"].As<string>()),
                        ["RegionalEventCount"] = Normalise(record["RegionalEventCount"].As<string>()),
                        ["RegionalActorCount"] = Normalise(record["RegionalActorCount"].As<string>()),
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

                Console.WriteLine($"Loaded {beforeLoadCount} for {afterLoadCount} results; period: {period.start.ToString()} to {period.end.ToString()}");
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
