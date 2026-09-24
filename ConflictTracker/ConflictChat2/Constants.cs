using ConflictCommon.Classes.DTOs;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace ConflictChat2.Constants
{
    public static class Constants
    {
        /// <summary>
        /// Useful for reference. Expand as required. We may need to put relationships in between allied and opposing actors. 
        /// </summary>
        [Obsolete]
        private const string KGSchema = """
            Nodes:
            Border(location, country) //WGS-84 points on border
            Fact(country, subkey, values, year) //CIA World Factbook data, (Country, Subkey) is the primary key
            Place(name, country, isCapital, location, minBorderDistanceKm, minCapitalDistanceKM) //places, with WGS-84 co-ordinates. Name, Country is the primary key
            Actor(name, type) //actors: state forces, militias, rebel groups, civilians, etc.
            Event(country, datetime, disorderType, fatalities, id, location, severity, type, subtype, summary) //ACLED events
            Relationships:
            Actor -[:INVOLVED_IN]-> Event
            Event -[:OCCURRED_AT]-> Place
            Place -[:WITHIN]-> Place

            No links for facts or borders, as they are not directly related to events or actors in the KG.
            """;

        private const string ActorCentricTemplate = """
    //ActorCentricTemplate:
    // Expand user-specified places into all contained places
    MATCH (root:Place)
    WHERE size($places) = 0 OR root.name IN $places

    MATCH (root)<-[:WITHIN*0..]-(expandedPlace:Place)
    WHERE size($countries) = 0 OR expandedPlace.country IN $countries

    WITH collect(DISTINCT expandedPlace) AS expandedPlaces,
         $actors AS actors,
         $places AS places,
         $countries AS countries,
         $startDate AS startDate,
         $endDate AS endDate

    // Match events occurring in the correct country
    MATCH (e:Event)-[:OCCURRED_AT]->(p:Place)
    WHERE (p.country IN countries OR size(countries) = 0)

    // Restrict events to expandedPlaces if places were specified
    AND (size(places) = 0 OR p IN expandedPlaces)

    // Filter events by datetime range
    AND (datetime(e.datetime) >= datetime(startDate) AND datetime(e.datetime) <= datetime(endDate))

    // Match actors involved in those filtered events
    MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    WHERE size(actors) = 0 OR a.name IN actors

    RETURN DISTINCT place,
           collect(DISTINCT e) AS events
    
    
    """;

        private const string PlaceCentricTemplate = """
    // Place centric template
    MATCH (p:Place)
    WHERE ((p.name IN $places OR size($places) = 0)
       AND (p.country IN $countries OR size($countries) = 0))

    MATCH (child)-[:WITHIN]->(p)
    MATCH (p)<-[:WITHIN]-(parent)

    WITH collect(DISTINCT child) +
         collect(DISTINCT parent) +
         collect(p) AS places,
         $startDate AS startDate,
         $endDate AS endDate

    UNWIND places AS place

    MATCH (e:Event)-[:OCCURRED_AT]->(place)

    // Filter events by datetime range (inclusive)
    WHERE datetime(e.datetime) >= datetime(startDate)
      AND datetime(e.datetime) <= datetime(endDate)

    RETURN DISTINCT place,
           collect(DISTINCT e) AS events
    
    """;


        //    private const string EventCentricTemplate = """
        ////EventCentricTemplate
        //MATCH (e:Event)
        //WHERE e.id IN $event_ids
        //OPTIONAL MATCH (e)-[:OCCURRED_AT]->(p:Place)
        //OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
        //RETURN e,
        //       p AS place,
        //       collect(DISTINCT a) AS actors
        //""";

        private const string EventCentricTemplate = """
        ///Event centric template
        // 1. Select root places based on optional filters
        MATCH (root:Place)
        WHERE (size($places) = 0 OR root.name IN $places)
          AND (size($countries) = 0 OR root.country IN $countries)

        // 2. Traverse hierarchy in BOTH directions using a single pattern
        MATCH (root)-[:WITHIN*0..]-(place:Place)

        // 3. Match events early to eliminate irrelevant places and reduce the result set (fixes memory usage issue)
        MATCH (e:Event)-[:OCCURRED_AT]->(place)
        WHERE datetime(e.datetime) >= datetime($startDate)
          AND datetime(e.datetime) <= datetime($endDate)

        // 4. Filter by summary/type/subtype fragments (case-insensitive)
        AND ANY(fragment IN $event_summary_fragments
                WHERE toLower(e.summary) CONTAINS toLower(fragment)
                   OR toLower(e.type) CONTAINS toLower(fragment)
                   OR toLower(e.subtype) CONTAINS toLower(fragment))

        // 5. Actor filtering (optional)
        OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
        WHERE size($actors) = 0 OR a.name IN $actors

        RETURN DISTINCT e
        
        
        

        """;


        private const string PredictivePlaceTemplate = """
    //PredictivePlaceTemplate
    MATCH (p:Place)
    WHERE (size($places) = 0 OR p.name IN $places)
      AND (size($countries) = 0 OR p.country IN $countries)

    OPTIONAL MATCH (descendant:Place)-[:WITHIN*0..]->(p)

    WITH collect(DISTINCT p) + collect(DISTINCT descendant) AS allPlaces

    UNWIND allPlaces AS place

    OPTIONAL MATCH (e:Event)-[:OCCURRED_AT]->(place)
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)

    RETURN DISTINCT place,
           collect(DISTINCT e) AS recent_events,
           collect(DISTINCT a) AS involved_actors
    """;

        private const string PredictiveActorTemplate = """
    //PredictiveActorTemplate:
    MATCH (p:Place)
    WHERE (size($places) = 0 OR p.name IN $places)
      AND (size($countries) = 0 OR p.country IN $countries)

    OPTIONAL MATCH (descendant:Place)-[:WITHIN*0..]->(p)

    WITH collect(DISTINCT p) + collect(DISTINCT descendant) AS allPlaces

    UNWIND allPlaces AS place

    MATCH (e:Event)-[:OCCURRED_AT]->(place)
    //WHERE e.datetime >= $cutoff_datetime
    MATCH (a:Actor)-[:INVOLVED_IN]->(e)
        WHERE size($actors) = 0 OR a.name IN $actors
    RETURN DISTINCT place,
           collect(DISTINCT e) AS recent_events,
           collect(DISTINCT a) AS involved_actors
    """;


    //    private const string CausualTemplate = """
    ////CausalTemplate
    //MATCH (target:Event)
    //WHERE target.id IN $event_ids
    //MATCH (target)-[:OCCURRED_AT]->(p:Place)

    //MATCH (prev:Event)-[:OCCURRED_AT]->(p)
    //WHERE prev.datetime < target.datetime

    //OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(target)
    //OPTIONAL MATCH (prevActor:Actor)-[:INVOLVED_IN]->(prev)

    //RETURN target,
    //       collect(DISTINCT prev) AS prior_events,
    //       collect(DISTINCT a) AS target_actors,
    //       collect(DISTINCT prevActor) AS prior_actors
    //ORDER BY target.datetime ASC
    //""";


        private const string FallbackTemplate = """
    //FallbackTemplate
    MATCH (e:Event)
    //WHERE e.datetime >= $cutoff_datetime
    OPTIONAL MATCH (e)-[:OCCURRED_AT]->(p:Place)
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    RETURN collect(DISTINCT e) AS events,
           collect(DISTINCT p) AS places,
           collect(DISTINCT a) AS actors
    LIMIT 50
    """;


        public static Dictionary<string, string> QueryTemplates = new Dictionary<string, string>
        {
            { "actor_centric", ActorCentricTemplate },
            { "place_centric", PlaceCentricTemplate },
            { "event_centric", EventCentricTemplate },
            { "predictive_place", PredictivePlaceTemplate },
            { "predictive_actor", PredictiveActorTemplate },
          //  { "causual", CausualTemplate },
            { "fallback", FallbackTemplate }
        };

        public static string systemPromptGeneric = """
                   <|system|>
                   You are a helpful AI assistant. 
                   Never writes messages for the user. 
                   You must respond only as <|assistant|> in ChatML format.
                   The only valid tags are <|system|>, <|user|>, and <|assistant|>.
                   You must ONLY produce content inside a single <|assistant|> message. 
                   You must NEVER generate <|user|> messages, user dialogue, or user actions.
                   You must NEVER continue the conversation on behalf of the user. 
                   You must output exactly one assistant message per turn."
                   """;

        public static string systemPromptIntent = """
            You are an intent‑classification and entity‑extraction model for a Neo4j
            conflict‑analysis knowledge graph. Your job is ONLY to:

            1. Determine the user's intent category.
            2. Extract relevant entities from the query.
            3. Return a strict JSON object.
            4. Never answer the user's question.

            INTENT CATEGORIES (choose exactly one):
            - actor_centric        → Query focuses on an actor or group of actors.
            - place_centric        → Query focuses on a place, city, town, or country.
            - event_centric        → Query focuses on a specific event or event type (e.g. riots, protests, battles, airstrikes, etc).
            - predictive_place     → Query asks about geographic place future risk, likelihood, trends.
            - predictive_actor     → Query asks about actor future risk, likelihood, trends, escalation between actors, etc.
            - fallback             → Query is vague, broad, or lacks identifiable entities.

            ENTITY EXTRACTION RULES:
            Extract only entities relevant to the schema:

            Actors:
              - Match any actor name or group (militia, rebels, army, police, civilians).
              - Use exact string from user query.

            Places:
              - Match any place name or country.
              - Use exact string from user query.

            Countries:
                -Identify the country of a named place. 
                -Use identified places to determine the country.
                -Examples: "Khartoum" -> "Sudan", "Kabul" -> "Afghanistan", "Mogadishu" -> "Somalia".

            Events:
              - Match any explicit event ID if present.
              - If user refers to an event indirectly (“the bombing last week“), set event_id to null.

            Event Summary Fragements:
                - Match to the type of event being searched for.
                - All values must be expressed in singular form (“airstrikes” -> “airstrike”; “protests” -> “protest”).

            Dates / Times:
              - Extract any explicit or implicit time expressions (“last month”, “recently”, “in six months”).
              - Convert nothing; return raw strings.

            OUTPUT FORMAT (strict JSON):
            {
              "intent": "<one_of_seven_intents>",
              "entities": {
                "actors": [ ... ],
                "places": [ ... ],
                "countries": [ ... ],
                "event_ids": [ ... ],
                "event_summary_fragments": [ ... ],
                "dates": [ ... ]
              }
            }

            RESTRICTIONS:
            - Do NOT generate Cypher.
            - Do NOT answer the question.
            - Do NOT invent entities.
            - Do NOT add fields not listed above.
            - If an entity type is absent, return an empty list.
            - Always return valid JSON.

            Your entire output MUST be only the JSON object.
            
            """;

    }


}
