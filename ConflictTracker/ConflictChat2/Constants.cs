using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictChat2.Constants
{
    public static class Constants
    {
        /// <summary>
        /// Useful for reference. Expand as required. We may need to put relationships in between allied and opposing actors. 
        /// </summary>
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

    WITH collect(DISTINCT expandedPlace) AS expandedPlaces, $actors AS actors, $places AS places, $countries AS countries

    // Match events occurring in the correct country
    MATCH (e:Event)-[:OCCURRED_AT]->(p:Place)
    WHERE (p.country IN countries or size(countries) = 0)

    // If places were specified, restrict events to expandedPlaces
    AND (
          size(places) = 0
          OR p IN expandedPlaces
        )

    // Match actors involved in those filtered events
    MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    WHERE size(actors) = 0 OR a.name IN actors

    RETURN a,
           collect(DISTINCT e) AS events,
           collect(DISTINCT p) AS places
    
    
    """;

        private const string PlaceCentricTemplate = """
    MATCH (p:Place)
    WHERE p.name IN $places AND p.country IN $countries
    OPTIONAL MATCH (e:Event)-[:OCCURRED_AT]->(p)
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    RETURN p,
           collect(DISTINCT e) AS events,
           collect(DISTINCT a) AS actors
    """;


        private const string EventCentricTemplate = """
    //EventCentricTemplate
    MATCH (e:Event)
    WHERE e.id IN $event_ids
    OPTIONAL MATCH (e)-[:OCCURRED_AT]->(p:Place)
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    RETURN e,
           p AS place,
           collect(DISTINCT a) AS actors
    """;


        private const string PredictivePlaceTemplate = """
    //PredictivePlaceTemplate
    MATCH (p:Place)
    WHERE p.name IN $places AND p.country IN $countries
    MATCH (e:Event)-[:OCCURRED_AT]->(p)
    WHERE e.datetime >= $cutoff_datetime
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    RETURN p,
           collect(DISTINCT e) AS recent_events,
           collect(DISTINCT a) AS involved_actors
    """;

        private const string PredictiveActorTemplate = """
    //PredictiveActorTemplate:
    MATCH (p:Place)
    WHERE p.country IN $countries
    MATCH (e:Event)-[:OCCURRED_AT]->(p)
    WHERE e.datetime >= $cutoff_datetime
    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(e)
    RETURN collect(DISTINCT p) AS places,
           collect(DISTINCT e) AS recent_events,
           collect(DISTINCT a) AS involved_actors
    """;


        private const string CausualTemplate = """
    //CausalTemplate
    MATCH (target:Event)
    WHERE target.id IN $event_ids
    MATCH (target)-[:OCCURRED_AT]->(p:Place)

    MATCH (prev:Event)-[:OCCURRED_AT]->(p)
    WHERE prev.datetime < target.datetime

    OPTIONAL MATCH (a:Actor)-[:INVOLVED_IN]->(target)
    OPTIONAL MATCH (prevActor:Actor)-[:INVOLVED_IN]->(prev)

    RETURN target,
           collect(DISTINCT prev) AS prior_events,
           collect(DISTINCT a) AS target_actors,
           collect(DISTINCT prevActor) AS prior_actors
    ORDER BY target.datetime ASC
    """;


        private const string FallbackTemplate = """
    //FallbackTemplate
    MATCH (e:Event)
    WHERE e.datetime >= $cutoff_datetime
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
            { "causual", CausualTemplate },
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
            - event_centric        → Query focuses on a specific event or event type.
            - predictive_place     → Query asks about geographic place future risk, likelihood, trends.
            - predictive_actor     → Query asks about actor future risk, likelihood, trends.
            - causal               → Query asks “what led to…”, “why did…”, “what caused…”.
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
              - If user refers to an event indirectly (“the bombing last week”), set event_id to null.

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
