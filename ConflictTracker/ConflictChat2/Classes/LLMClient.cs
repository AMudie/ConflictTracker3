using ConflictChat2.Constants;
using ConflictCommon.Classes.StaticHelpers;
using Microsoft.Recognizers.Text.NumberWithUnit.Chinese;
using Neo4j.Driver;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Reflection.Metadata.Ecma335;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using static ConflictCommon.Classes.StaticHelpers.Neo4jQueryService;
using static System.Net.WebRequestMethods;

namespace ConflictChat2.Classes
{
    public class LLMClient
    {

        private readonly string _modelName;
        private string _systemPrompt = "";
        private readonly ShortTermMemory _shortTermMemory;

        private readonly HttpClient _http = new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = new TimeSpan(0, 2, 0)
        };

        public LLMClient(string modelName, ShortTermMemory stm)
        {
            _modelName = modelName;
            _shortTermMemory = stm;
        }

        public void SetSystemPrompt(string prompt)
        {
            _systemPrompt = prompt;
        }

        /// <summary>
        /// Builds a ChatML prompt for the language model by combining the system prompt, long-term memories, and short-term conversation history. The method constructs a structured prompt that includes the system instructions (if set), any relevant long-term memories, and the recent conversation history with appropriate role tags (e.g., <|user|> and <|assistant|>). This structured format helps guide the model's response generation by providing clear context and instructions. The final prompt ends with an assistant tag to indicate that the model should generate a response as the assistant.
        /// </summary>
        /// <param name="memories"></param>
        /// <returns></returns>
        private string BuildPrompt(bool reduceContextMessages = false, List<string>? facts = null)
        {
            var sb = new System.Text.StringBuilder();

            // System message
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
            {
                sb.AppendLine(_systemPrompt);
                sb.AppendLine();
                sb.AppendLine("The current UTC date is: " + DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
                sb.AppendLine();
            }

            int? lastNMessages = null;
            if (reduceContextMessages)
            {
                lastNMessages = 3; //get fewer messeges from the short term memory, to reduce the prompt size (useful if the model has a small context window)
            }

            if (facts != null && facts.Count > 0)
            {
                sb.AppendLine("Known facts that may help you answer this query:");
                foreach (string fact in facts)
                {
                    sb.AppendLine($"•{fact}");
                }
            }


            if (_shortTermMemory.GetImmutableMessages().Any())
            {
                // Conversation history
                foreach (var (role, content, isSummary) in _shortTermMemory.GetTruncatedMessages(lastNMessages))
                {
                    sb.AppendLine("Previous messages in this conversation follow:");
                    sb.AppendLine();
                    var tag = role switch
                    {
                        "user" => "<|user|>",
                        "assistant" => "<|assistant|>",
                        _ => "<|user|>"
                    };

                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }

            // The model should now respond as assistant
            sb.AppendLine("<|assistant|>\n");
            sb.AppendLine(); //appent empty line, helps some models understand to start generating

            return sb.ToString();
        }

        /// <summary>
        /// Query the SLM with KG derived facts
        /// </summary>
        /// <param name="kgName">name of the knowledge graph to query   </param>
        /// <param name="userPrompt">User's basic string input</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<string> AskModelKGRAG(string kgName, string uri, string username, string password, string userPrompt)
        {

            //add the user's query to the STM:
            await _shortTermMemory.AddAsync("user", userPrompt, this);

            //establish intent:
            string intentPrompt = Constants.Constants.systemPromptIntent;
            SetSystemPrompt(intentPrompt);
            string prompt = BuildPrompt();

            //Make the request to the model:
            var response = await _http.PostAsJsonAsync("/api/generate", new
            {
                model = _modelName,
                prompt = prompt,
                stream = false
            });

            //Process response to get the intent and params:
            var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var reply = json?["response"]?.ToString() ?? "";


            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("Model's response is blank.");
            }
            else
            {

                //Note that we do not add this response to the STM, it doesn't help anything. 

                //output the response to console (for debugging):
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("ConflictChat: " + json);
                Console.ResetColor();
                Console.WriteLine();

                using var doc = JsonDocument.Parse(reply);
                JsonElement root = doc.RootElement;
                string intent = root.GetProperty("intent").GetString();


                if (string.IsNullOrEmpty(intent) || Constants.Constants.QueryTemplates.ContainsKey(intent) == false)
                {
                    throw new InvalidOperationException($"Intent {intent} is empty or unrecognised.");
                }
                else
                {
                    //Now we need to extract facts from the KG:

                    string template = Constants.Constants.QueryTemplates[intent];
                    Dictionary<string, object> parameters = new Dictionary<string, object>();
                    parameters.Add("actors", root.GetProperty("entities")
                 .GetProperty("actors")
                 .EnumerateArray()
                 .Select(x => x.GetString().Replace("the ", "")) //Model likes to prefix "the..." e.g. "the Muslim Brotherhood", but the actor will be "Muslim Brotherhood".
                 .ToList());
                    parameters.Add("places", root.GetProperty("entities")
                 .GetProperty("places")
                 .EnumerateArray()
                 .Select(x => x.GetString())
                 .ToList());
                    parameters.Add("countries", root.GetProperty("entities")
                 .GetProperty("countries")
                 .EnumerateArray()
                 .Select(x => x.GetString())
                 .ToList());
                    parameters.Add("event_ids", root.GetProperty("entities")
                 .GetProperty("event_ids")
                 .EnumerateArray()
                 .Select(x => x.GetString())
                 .ToList());
                    //   parameters.Add("dates", root.GetProperty("entities")
                    //.GetProperty("dates")
                    //.EnumerateArray()
                    //.Select(x => x.GetString())
                    //.ToList());
                    parameters.Add("dates", root.GetProperty("entities")
                 .GetProperty("dates")
                 .EnumerateArray()
                 .Select(x => x.GetString())
                 .ToList());



                    //load the nodes into human (or SLM...) readable "fact" strings:
                    List<Dictionary<string, object>> nodes = await new Neo4jQueryService(uri, username, password).ExecuteQueryAsync(template, parameters, kgName);
                    List<string> facts = ProcessNodesToStringFacts(nodes);
                    string factsPromptComponent =
                      "Here are known facts to help with your answer:\n\n" +
                      string.Join(Environment.NewLine, facts.Select(f => "-" + f));

                    //Output the facts to console (again for debugging)
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"Nodes: {nodes.Count}; Facts: {facts.Count}");
                    foreach (string fact in facts)
                    {
                        Console.WriteLine(Environment.NewLine + fact);
                    }
                    Console.ResetColor();

                    //set the system prompt for the actual query:
                    SetSystemPrompt(Constants.Constants.systemPromptGeneric);

                    string promptForQuery = BuildPrompt(false, facts);

                    //Make the request to the model:
                    var queryPrompt = await _http.PostAsJsonAsync("/api/generate", new
                    {
                        model = _modelName,
                        prompt = promptForQuery,
                        stream = false
                    });

                    var queryJson = await queryPrompt.Content.ReadFromJsonAsync<Dictionary<string, object>>();
                    var queryReply = queryJson?["response"]?.ToString() ?? "";


                    if (string.IsNullOrWhiteSpace(queryReply))
                    {
                        throw new InvalidOperationException("Model's response is blank.");
                    }
                    else
                    {
                        //output the response to console
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("ConflictChat: " + queryReply);
                        Console.ResetColor();
                        Console.WriteLine();

                        //the model does sometimes like to include rogue ChatML tags in its output, so we clean them out here.
                        reply = StringHelper.RemoveAllChatMLTags(StringHelper.CleanModelResponse(reply));

                        //add the reply to the short term memory. 
                        await _shortTermMemory.AddAsync("assistant", reply, this);

                    }

                }
            }

            return reply;
        }

        #region "Node Processing"
        private List<string> ProcessNodesToStringFacts(List<Dictionary<string, object>> nodes)
        {
            var facts = new List<(DateTime? date, string text)>();

            foreach (var node in nodes)
            {
                // Extract event nodes (may be single or list)
                var events = ExtractNodes(node, "events")
                             ?? ExtractNodes(node, "recent_events")
                             ?? ExtractNodes(node, "prior_events")
                             ?? ExtractSingleNode(node, "e")
                             ?? ExtractSingleNode(node, "target");

                if (events == null || events.Count == 0)
                    continue;

                // Extract actors
                var actors = ExtractNodes(node, "actors")
                             ?? ExtractNodes(node, "involved_actors")
                             ?? ExtractNodes(node, "target_actors")
                             ?? ExtractNodes(node, "prior_actors");

                // Extract places
                var places = ExtractNodes(node, "places")
                             ?? ExtractNodes(node, "place");

                foreach (var ev in events)
                {
                    var props = ev["properties"] as Dictionary<string, object>;
                    if (props == null) continue;

                    // Event fields
                    string summary = props.TryGetValue("summary", out var s) ? s?.ToString() : null;
                    string subtype = props.TryGetValue("subtype", out var st) ? st?.ToString() : null;
                    string type = props.TryGetValue("type", out var t) ? t?.ToString() : null;
                    string disorderType = props.TryGetValue("disorderType", out var dt) ? dt?.ToString() : null;
                    int fatalities = props.TryGetValue("fatalities", out var f) ? Convert.ToInt32(f) : 0;
                    int severity = props.TryGetValue("severity", out var sev) ? Convert.ToInt32(sev) : 0;

                    DateTime? date = null;
                    if (props.TryGetValue("datetime", out var d))
                    {
                        if (DateTime.TryParse(d.ToString(), out var parsed))
                            date = parsed;
                    }

                    // Actor descriptions
                    string actorText = "";
                    if (actors != null)
                    {
                        var actorStrings = actors.Select(a =>
                        {
                            var ap = a["properties"] as Dictionary<string, object>;
                            string name = ap?["name"]?.ToString();
                            string typeA = ap?["type"]?.ToString();
                            return $"{name} ({typeA})";
                        }).ToList();

                        actorText = actorStrings.Count > 0
                            ? $"Actors involved: {string.Join(", ", actorStrings)}. "
                            : "";
                    }

                    // Place descriptions
                    string placeText = "";
                    if (places != null)
                    {
                        var placeStrings = places.Select(p =>
                        {
                            var pp = p["properties"] as Dictionary<string, object>;
                            return pp?["name"]?.ToString();
                        }).Where(x => x != null).ToList();

                        placeText = placeStrings.Count > 0
                            ? $"Location: {string.Join(", ", placeStrings)}. "
                            : "";
                    }

                    // Build fact text
                    var sb = new StringBuilder();

                    if (date != null)
                        sb.Append($"On {date:yyyy-MM-dd}, ");

                    if (!string.IsNullOrEmpty(subtype))
                        sb.Append($"{subtype} event occurred. ");
                    else if (!string.IsNullOrEmpty(type))
                        sb.Append($"{type} event occurred. ");
                    else if (!string.IsNullOrEmpty(disorderType))
                        sb.Append($"{disorderType} event occurred. ");

                    if (!string.IsNullOrEmpty(summary))
                        sb.Append($"Summary: {summary}. ");

                    if (fatalities > 0)
                        sb.Append($"Fatalities: {fatalities}. ");

                    sb.Append($"Severity: {severity}. ");

                    sb.Append(actorText);
                    sb.Append(placeText);

                    facts.Add((date, sb.ToString().Trim()));
                }
            }

            // Sort by date
            return facts
                .OrderBy(f => f.date ?? DateTime.MaxValue)
                .Select(f => f.text)
                .ToList();
        }

        private List<Dictionary<string, object>> ExtractNodes(Dictionary<string, object> row, string key)
        {
            if (!row.ContainsKey(key)) return null;

            if (row[key] is List<object> list)
                return list.Cast<Dictionary<string, object>>().ToList();

            if (row[key] is List<Dictionary<string, object>> dictList)
                return dictList;

            return null;
        }

        private List<Dictionary<string, object>> ExtractSingleNode(Dictionary<string, object> row, string key)
        {
            if (!row.ContainsKey(key)) return null;

            if (row[key] is Dictionary<string, object> node)
                return new List<Dictionary<string, object>> { node };

            return null;
        }

        /// <summary>
        /// Obsolete method for deriving string facts based on a prompt. Present for reference only. 
        /// </summary>
        /// <param name="userPrompt"></param>
        /// <returns></returns>
        [Obsolete("ProduceFactsToInformResponseAsync() is deprecated and must not be used.", true)]
        private async Task<List<string>> ProduceFactsToInformResponseAsync(string userPrompt)
        {
            List<(DateTime Start, DateTime End)> dateRanges = NLPTimeExtractor.ExtractDateRanges(userPrompt);

            Dictionary<string, (string Type, string NodeID)> namedEntities = NERHelper.IdentifyNamedEntities(userPrompt, kgName: "sudan");

            Neo4jQueryService service = new Neo4jQueryService(
                  AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password")
                );

            //Extract places within the named places:
            List<int> placeNodeIds = new List<int>();
            List<INode> placeNodes = new List<INode>();
            if (namedEntities.Any(x => x.Value.Type == "Place"))
            {
                placeNodeIds = namedEntities.Where(x => x.Value.Type == "Place").Select(x => int.Parse(x.Value.NodeID)).ToList();
                placeNodes = await service.ExecutePlacesWithinPlacesQueryAsync(placeNodeIds: placeNodeIds, kgName: "sudan");

            }

            //Extract all events involving the named actor (only expected to be one node per named actor)
            //if any place nodes are loaded, then we'll filter to only those places. 
            List<int> actorNodeIds = new List<int>();
            if (namedEntities.Any(x => x.Value.Type == "Actor"))
            {

                actorNodeIds = namedEntities.Where(x => x.Value.Type == "Actor").Select(x => int.Parse(x.Value.NodeID)).ToList();
            }
            List<long> placeNodesWithinNodes = placeNodes.Select(x => x.Id).ToList();
            List<(INode e, INode place, INode actor)> results = await service.ExecuteEventsInvolvingActorQueryAsync(actorNodeIds: actorNodeIds, placeNodeIds: placeNodesWithinNodes, kgName: "sudan", dateRanges: dateRanges);

            List<string> facts = new List<string>();
            if (results != null)
            {

                //Process actor nodes:
                List<INode> actorNodes = results.Select(r => r.actor).Distinct().ToList();
                foreach (INode actorNode in actorNodes)
                {
                    string fact = $"{actorNode.Properties["name"]} is a {actorNode.Properties["type"]}.";
                    facts.Add(fact);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{fact}; Actor node Id: {actorNode.Id}");
                    Console.ResetColor();
                }

                //Process event/place/actor node triples:
                foreach (var result in results)
                {
                    List<INode> otherActors = new List<INode>();
                    string otherActorsString = "";
                    if (results.Any(x => x.e.ElementId == result.e.ElementId && x.actor.Id != result.actor.Id))
                    {
                        otherActors = results.Where(x => x.e.ElementId == result.e.ElementId && x.actor.Id != result.actor.Id).Select(x => x.actor).ToList();
                        otherActorsString = string.Join(", ", otherActors.Select(x => x.Properties["name"].ToString()));
                    }

                    string fact = $"On {result.e.Properties["datetime"].ToString()} {result.actor.Properties["name"]} were involved in {result.e.Properties["subtype"]} in {result.place.Properties["name"]} (Source: {result.e.Properties["source"]})";


                    if (otherActors.Count() > 0)
                    {
                        fact += $" Other involved groups were {otherActorsString}.";
                    }

                    if ((long)result.e.Properties["fatalities"] > 0)
                    {
                        fact += $" There were {result.e.Properties["fatalities"]} fatalities.";
                    }



                    facts.Add(fact);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{fact}; Event node Id: {result.e.Id}");
                    Console.ResetColor();
                }

            }


            await service.DisposeAsync();

            return facts;

        }

        #endregion

        public async Task<string> AskModelRaw(string userPrompt)
        {

            await _shortTermMemory.AddAsync("user", userPrompt, this);


            // List<string> factsFromKG = ProduceFactsToInformResponseAsync(userPrompt).Result;
            List<string> factsFromKG = null;
            string prompt = BuildPrompt(false, factsFromKG);



            //Make the request to the model:
            var response = await _http.PostAsJsonAsync("/api/generate", new
            {
                model = _modelName,
                prompt,
                stream = false
            });

            var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var reply = json?["response"]?.ToString() ?? "";


            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("Model's response is blank.");
            }

            else
            {
                //Do nothing
                //TODO: Play a sound here. 
            }


            return reply;
        }




    }
}
