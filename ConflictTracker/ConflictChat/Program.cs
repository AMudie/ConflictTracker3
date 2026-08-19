using ConflictChat.Classes;
using ConflictCommon.Classes.StaticHelpers;


while (true)
{
    try
    {

        Console.WriteLine("ConflictChat is the console interface for talking with the local Ollama model for the Conflict Tracker MSc Project");

        await OllamaHelper.EnsureOllamaRunning(); //Start Ollama if not already running.
        var _stm = new ShortTermMemory(10);

        const string ollamaModelName = "qwen25-7b"; // Replace with your desired Ollama model name
        var llm = new LLMClient(ollamaModelName, _stm);


        if (!ConflictCommon.Classes.StaticHelpers.Neo4JHelper.IsNeo4jDesktopRunning())
        {
            Console.WriteLine("Neo4j Desktop is not running. Please start it and try again.");
            return;
        }
      


        //Allow the user to specify the KG they care about:
        string kgName = "";
        while (string.IsNullOrWhiteSpace(kgName))
        {
           string[] knownKGs = await ConflictCommon.Classes.StaticHelpers.Neo4JHelper.GetDatabaseNamesAsync(
                                  AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_uri"),
                    AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_username"),
                        AppSettingsHelper.LoadAppSetting("Neo4JInstanceSettings:KG_password")
                );

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

  

            const string systemPrompt =
                """
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
            llm.SetSystemPrompt(systemPrompt);

            Console.Write("> ");
            string userPrompt = Console.ReadLine();

            string reply = await llm.AskModelRaw(userPrompt);
            reply = StringHelper.RemoveAllChatMLTags(StringHelper.CleanModelResponse(reply));
            _stm.AddAsync("assistant", reply, llm);


            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("ConflictChat: " + reply);
            Console.ResetColor();
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Unhandled exception: " + ex.ToString());
        Console.WriteLine("Stack trace: " +ex.StackTrace);
        Console.ResetColor();
    }
}