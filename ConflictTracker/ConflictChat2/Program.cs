using ConflictChat2.Classes;
using ConflictCommon.Classes.StaticHelpers;
using ConflictChat2.Constants;

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
            if (userPrompt.Contains("TEST", StringComparison.OrdinalIgnoreCase))
            {
                //basic test functionality, useful if the model is returning empty responses.
                reply = await llm.AskModelRaw(userPrompt);
            }
            else
            {
              reply = await llm.AskModelKGRAG(kgName, uri, username, password, userPrompt);
            }

            ////Dump the final output to the console in cyan so it stands out from the user input.
            //Console.ForegroundColor = ConsoleColor.Cyan;
            //Console.WriteLine("ConflictChat: " + reply);
            //Console.ResetColor();
            //Console.WriteLine();
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