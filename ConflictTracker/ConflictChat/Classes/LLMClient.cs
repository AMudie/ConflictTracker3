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
using static ConflictCommon.Classes.StaticHelpers.Neo4jQueryService;
using static System.Net.WebRequestMethods;

namespace ConflictChat.Classes
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

        public async Task<string> AskModelRaw(string userPrompt)
        {

            await _shortTermMemory.AddAsync("user", userPrompt, this);


            List<string> factsFromKG = ProduceFactsToInformResponseAsync(userPrompt).Result;

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


    }
}
