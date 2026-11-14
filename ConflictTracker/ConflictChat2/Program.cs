using ConflictChat2.Classes;
using ConflictCommon.Classes.DTOs;
using ConflictCommon.Classes.StaticHelpers;
using Microsoft.VisualBasic;
using System.Data;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

while (true)
{
    try
    {

        Console.WriteLine("ConflictChat2 is the console interface for talking with the local Ollama model for the Conflict Tracker MSc Project");

        await OllamaHelper.EnsureOllamaRunning(); //Start Ollama if not already running.
        var _stm = new ShortTermMemory(10);

        const string ollamaModelName = "qwen25-7b"; // Replace with your desired Ollama model name
        var llm = new LLMClient(ollamaModelName, _stm);


        if (!ConflictCommon.Classes.StaticHelpers.Neo4JHelper.IsNeo4jDesktopRunning())
        {
            Console.WriteLine("Neo4j Desktop is not running. Please start it and try again.");
            return;
        }


        string uri = AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri");
        string username = AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username");
        string password = AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password");

        //Allow the user to specify the KG they care about:
        string kgName = "";
        while (string.IsNullOrWhiteSpace(kgName))
        {

            string[] knownKGs = await ConflictCommon.Classes.StaticHelpers.Neo4JHelper.GetDatabaseNamesAsync(
                            uri, username, password);

            if (knownKGs.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Select a knowledge graph:");
                Console.ResetColor();
                foreach (string knownKG in knownKGs)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(knownKG);
                    Console.ResetColor();
                }
                Console.ResetColor();
                string userinput = Console.ReadLine();
                if (!string.IsNullOrEmpty(userinput) && knownKGs.Any(kg => kg.Trim().ToUpper() == userinput.Trim().ToUpper()))
                {
                    kgName = knownKGs.First(kg => kg.Trim().ToUpper() == userinput.Trim().ToUpper());
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{userinput} does not match the name of a known knowledge graph on this instance.");
                    Console.ResetColor();
                }

            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No knowledge graphs to read from.");
                Console.ResetColor();

            }

        }
        Console.ResetColor();

        while (true)
        {

            Console.Write("> ");
            string userPrompt = Console.ReadLine();

            string reply = "";
            if (userPrompt.StartsWith("TEST", StringComparison.OrdinalIgnoreCase))
            {
                //basic test functionality, useful if the model is returning empty responses.
                reply = await llm.AskModelRaw(userPrompt);
            }
            else if (userPrompt.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
            {
                //command mode, currently used to generate SLM predictions
                if (userPrompt.StartsWith("/GenerateSLMPredictions", StringComparison.InvariantCultureIgnoreCase) && userPrompt.StartsWith("/GenerateSLMPredictionsKGRAG", StringComparison.InvariantCultureIgnoreCase) == false)
                {
                    string[] places = [
"Nairobi - Kenya (Large city / Capital)"
,"Mombasa - Kenya (Large coastal city)"
,"Kisumu - Kenya (Medium city)"
,"Cairo - Egypt (Megacity / Capital)"
,"Luxor - Egypt (Medium city / Historical hub)"
,"Siwa - Egypt (Small oasis town)"
,"Tripoli - Libya (Large city / Capital)"
,"Benghazi - Libya (Large city)"
,"Ghadames - Libya (Small historic oasis town)"
,"Kampala - Uganda (Large city / Capital)"
,"Jinja - Uganda (Medium town)"
,"Mogadishu - Somalia (Large city / Capital)"
,"Hargeisa - Somalia / Somaliland (Large city)"
,"Juba - South Sudan (Large city / Capital)"
,"Wau - South Sudan (Medium town)"
,"Khartoum - Sudan (Large city / Capital)"
,"Port Sudan - Sudan (Large coastal city)"
,"N'Djamena - Chad (Large city / Capital)"
,"Bangui - Central African Republic (Large city / Capital)"
,"Addis Ababa - Ethiopia (Large city / Capital)"
                        ];

                    Dictionary<string, string> results = new Dictionary<string, string>();

                    Console.WriteLine("");
                    foreach (string place in places)
                    {
                        string result = await llm.AskModelRaw($"The current date is July 1, 2024. Will there be conflict, specifically involving fatalities, in {place} within 12 months? Start your response with either 'Yes. ' or 'No. ' or 'Undetermined ' as appropriate. Then state reasons why.", false);
                        result = result.Replace(Environment.NewLine, ""); //remove new line chars: makes formatting easier. 

                        Console.WriteLine($"Result: {place} - {result}");
                        Console.WriteLine("");
                        results.Add(place, result);
                    }
                } //end of action for /GenerateSLMPredictions
                else if (userPrompt.StartsWith("/GenerateSLMPredictionsKGRAG"))
                {

                    //Dictionary<string, string> places = new Dictionary<string, string>()
                    //{
                    //    { "Nairobi", "Kenya" },
                    //    { "Mombasa", "Kenya" },
                    //    { "Kisumu", "Kenya" },
                    //    { "Cairo", "Egypt" },
                    //    { "Luxor", "Egypt" },
                    //    { "Siwa", "Egypt" },
                    //    { "Tripoli", "Libya" },
                    //    { "Benghazi", "Libya" },
                    //    { "Ghadames", "Libya" },
                    //    { "Kampala", "Uganda" },
                    //    { "Jinja", "Uganda" },
                    //    { "Mogadishu", "Somalia" },
                    //    { "Hargeisa", "Somalia" },
                    //    { "Juba", "South Sudan" },
                    //    { "Wau", "South Sudan" },
                    //    { "Khartoum", "Sudan" },
                    //    { "Port Sudan", "Sudan" },
                    //    { "Ndjamena", "Chad" },
                    //    { "Bangui", "Central African Republic" },
                    //    { "Addis Ababa", "Ethiopia" }
                    //};

                    Dictionary<string, string> places = new Dictionary<string, string>()
                    {
                        { "Reifi Wad Elhilaiw", "Sudan"}
                    };

                    List<ConflictCommon.Classes.DTOs.SLMConflictPredictionDTO> results = new List<ConflictCommon.Classes.DTOs.SLMConflictPredictionDTO>();

                    Console.WriteLine("");
                    foreach (string place in places.Keys)
                    {
                        string country = places[place];
                        DateTime endDate = new DateTime(2024, 07, 01);
                        DateTime startDate = new DateTime(2020, 01, 01);
                        string prompt = $"You are a conflict prediction utility. The current date is July 1, 2024. On the balance of probabilities, will there be armed conflict affecting civilians in {place}, {country} or within 10km thereof within 12 months? Start your response with either 'Yes. ' or 'No. ' then state reasons why.";

                        string response = await llm.AskModelForSLMPrediction(prompt, kgName, uri, username, password, placeName: place, countryName: country, startDate, endDate);
                        response = response.Replace(Environment.NewLine, ""); //remove new line chars: makes formatting easier. 

                        string prediction = "Undetermined";
                        if (response.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
                        {
                            prediction = "True";
                        }
                        else if (response.StartsWith("No", StringComparison.OrdinalIgnoreCase))
                        {
                            prediction = "False";
                        }
                        else if (response.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                        {
                            prediction = "Error";
                        }
                        else
                        {
                            prediction = "Undetermined";
                        }

                        Console.WriteLine($"Result: {place} ({country}) - {prediction}");
                        Console.WriteLine("");
                        results.Add(new ConflictCommon.Classes.DTOs.SLMConflictPredictionDTO() { Place = place, Country = country, SLMConflictPrediction = prediction, Reasons = response });
                    }

                    //write the results to the console (commas removed to avoid that pain):
                    List<Dictionary<string, string>> resultsForCSV = new List<Dictionary<string, string>>();
                    foreach (SLMConflictPredictionDTO result in results)
                    {
                        Console.WriteLine($"{result.Place} ({result.Country}): {result.SLMConflictPrediction}");
                        resultsForCSV.Add(new Dictionary<string, string>
                        {
                            { "Place", result.Place.Replace(",", "").Replace(Environment.NewLine,"") },
                            { "Country", result.Country.Replace(",", "").Replace(Environment.NewLine,"")  },
                            { "SLMConflictPrediction", result.SLMConflictPrediction.Replace(",", "").Replace(Environment.NewLine,"")  },
                            { "Reasons", result.Reasons.Replace(",", "").Replace(Environment.NewLine,"")  }

                        });
                    }

                    //write the results to CSV:
                    string filePath = "C:/Users/andre/source/repos/ConflictTracker3/ConflictTracker/ConflictCalc/Python_Any/Data/SLM Prediction/SLMPredictions.csv";
                    ConflictCommon.Classes.StaticHelpers.CsvHelper.WriteToCSV(resultsForCSV, filePath);
                    if (System.IO.File.Exists(filePath))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"SLM predictions written to {filePath}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Failed to write SLM predictions to {filePath}");
                        Console.ResetColor();
                    }

                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("Done.");
                    Console.ResetColor();
                }


                else
                {
                    throw new ArgumentException($"Command {userPrompt} not recognised");
                }
            }
            else
            {
                //normal operations:
                reply = await llm.AskModelKGRAG(kgName, uri, username, password, userPrompt);
            }

        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Unhandled exception: " + ex.ToString());
        Console.WriteLine("Stack trace: " + ex.StackTrace);
        Console.ResetColor();
    }
}