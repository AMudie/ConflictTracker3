using ConflictConsole.StaticHelpers;
using ConflictCommon.Classes.StaticHelpers;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.Design;
using System.Data;
using System.Runtime.CompilerServices;
using static System.Runtime.InteropServices.JavaScript.JSType;
Console.WriteLine("ConflictConsole is the initial kickoff for the Conflict Tracker MSc Project");


string environment = AppSettingsHelper.LoadAppSetting("DevEnv");
Console.WriteLine($"Development environment: {environment}");

if (ConflictCommon.Classes.StaticHelpers.Neo4JHelper.IsNeo4jDesktopRunning())
{
    Console.WriteLine("Neo4j Desktop is running.");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Neo4j Desktop is NOT running.");
    Console.ResetColor();
}

while (true)
{

    try
    {

        Console.ResetColor();
        Console.Write("> ");
        string[] userInput = Console.ReadLine().Split(" ");
        switch (userInput.First())
        {
            case "load":

                string filter = "";
                string knowledgeGraphName = "";


                if (userInput.Length > 1)
                {
                    knowledgeGraphName = userInput[1];

                    if (userInput.Length > 2)
                    {
                        filter = userInput[2];
                    }
                }

                ConflictConsole.StaticHelpers.CommandHelper.Load(knowledgeGraphName, filter);


                break;

            case "createkg":

                string kgName = string.Join(" ", userInput.Skip(1));

                if (kgName.Length < 1)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Please provide a name for the knowledge graph.");
                    Console.ResetColor();
                    break;
                }
                else
                {
                    if (CommandHelper.ListDatabases().Result.Any(x => x.ToUpper() == kgName.Replace(" ", "").ToUpper()))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Database {kgName} likely already exists. Press T to confirm overwriting, any other key to abort.");
                        Console.ResetColor();
                        if (Console.ReadLine().ToString().ToUpper() != "T")
                        {
                            break;
                        }
                    }
                }


                ConflictConsole.StaticHelpers.CommandHelper.CreateKG(kgName);

                break;
            case "exit":
                Console.WriteLine("Exiting the application.");
                return;

            case "help":
                Console.WriteLine("Available commands: createkg, help, exit, list, dump, load (databasName)");
                break;
            case "list":

                string[] databases = CommandHelper.ListDatabases().Result;
                if (databases.Length > 0)
                {
                    Console.WriteLine("Available databases:");
                    foreach (var db in databases)
                    {
                        Console.WriteLine("- " + db);
                    }
                }
                else
                {
                    Console.WriteLine("No databases found.");
                }

                break;
            case "dump":

      
                string graphName = "";
                if (userInput.Length > 1)
                {
                    graphName = userInput[1];

                    string[] countryComponents  = new ArraySegment<string>(userInput, 2, userInput.Length - 2).ToArray();
                    string countriesString = string.Join(" ", countryComponents);
                    string[] countries = countriesString.Split(';');
                    List<string> countriesList = countries.Select(x => x.Trim()).ToList();

                   CommandHelper.DumpPlacesInCountry(graphName, countriesList);
                    
                    CommandHelper.DumpActorsInCountry(graphName, countriesList);

                }
                             
                break;
            default:
                Console.WriteLine(userInput + " is not a recognised command. Use the help command to list available commands.");
                break;
        }

    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine("ERROR: " + ex.Message);
        if (ex.InnerException != null)
        {
            Console.WriteLine();
            Console.WriteLine("INNER EXCEPTION: " + ex.InnerException.Message);
        }
        Console.WriteLine();
        Console.WriteLine("STACK TRACE: " + ex.StackTrace);

    }
    finally
    {
        Console.ResetColor();


    }
}
