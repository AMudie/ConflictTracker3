using ConflictCalc.Enumerations;
using ConflictCommon.Classes.DTOs;
using ConflictCommon.Classes.StaticHelpers;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using ConflictCalc.StaticHelpers;

// Register commands for this console app
CommandRegistry.Register("builddataset", DatasetAssemblyHelper.BuildDataset, "Starts the process of building a tabular, time series dataset.", new Dictionary<string, string>
{
    ["kgname"] = "name of the Neo4J knowledge graph to use.",
    ["placeName"] = "The place to limit the dataset around. leave blank for all places. Always provide in yyyyMMdd format.",
    ["startDate"] = "The start date of the time period to build the dataset",
    ["frequencyPeriod"] = "Weekly, Monthly, or Quarterly."
});
//Register the common commands (will be pretty standard for most implementations of this console app).
CommandRegistry.Register("help", CommonCommands.Help, "Provides a list of available commands, and specific command parameters if a command is specified.", new Dictionary<string, string>
{
    ["command"] = "The command to show detailed help for (e.g., help -builddataset)."

});
CommandRegistry.Register("exit", CommonCommands.Exit, "Exits the application");
CommandRegistry.Register("buildmodel", BuildModel, "Builds and saves a classical ML model for predicting conflict.", new Dictionary<string, string>
{
    ["filepath"] = "Path to dataset",
    ["placeName"] = "Place and all places within to remove from the dataset, used to prevent data leakage if preducting for that place.",
    ["algorithm"] = $"Algorithm to use, options for classification are {string.Join(", ", Enum.GetNames(typeof(MLAlgorithmBinaryClassification)))} and options for regression are {string.Join(", ", Enum.GetNames(typeof(MLAlgorithmRegression)))}.",
    ["metric"] = "Metric to define conflict- severity, fatalities, etc.",
    ["time"] = $"Timespan to make preductions of conflict within. Valid options are {string.Join(", ", Enum.GetNames(typeof(TimePrediction)))}."
});

Console.WriteLine("ConflictCalc is the assembly of structured data for conflict prediction. Use the Help command to list available functionality.", new Dictionary<string, string>());

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (input == null)
        continue;

    CommandProcessor.Process(input);
}
Console.ReadLine();






///<summary>Builds an ML model</summary>
///<remarks></remarks>
///<example>buildmodel -filepath "C:\Users\andre\source\repos\ConflictTracker2\ConflictTracker\ConflictBI\Sudan_Test2000_18July2026.csv" -placename Khartoum -algorithm SDCARegression -metric LocalTotalFatalities -time OneMonth</example>
void BuildModel(Dictionary<string, string> flags)
{

    try
    {

        // Validate required filepath argument
        if (!flags.TryGetValue("filepath", out var filePath) || string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Required argument filePath has not been supplied, or is empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File {filePath} does not exist.");

        if (!Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .csv files are supported.");



        //// Validate required placename argument
        if (!flags.TryGetValue("placename", out var placeName) || string.IsNullOrWhiteSpace(placeName))
        {
            //  throw new ArgumentException("Required argument placeName has not been supplied, or is empty.");
            //do nothing
        }



        // Validate required algorithm argument → ENUM
        if (!flags.TryGetValue("algorithm", out var algorithmStr) || string.IsNullOrWhiteSpace(algorithmStr))
            throw new ArgumentException("Required argument algorithm has not been supplied, or is empty.");

        algorithmStr = algorithmStr.Trim();

        // Try parse into either enum
        MLAlgorithmBinaryClassification? algoBinary = null;
        MLAlgorithmRegression? algoRegression = null;
        MLAlgorithmTimeSeries? algoTimeSeries = null;

        if (Enum.TryParse<MLAlgorithmBinaryClassification>(algorithmStr, true, out var parsedBinary))
        {
            algoBinary = parsedBinary;
        }
        else if (Enum.TryParse<MLAlgorithmRegression>(algorithmStr, true, out var parsedRegression))
        {
            algoRegression = parsedRegression;
        }
        else if (Enum.TryParse<MLAlgorithmTimeSeries>(algorithmStr, true, out var parsedTimeSeries))
        {
            algoTimeSeries = parsedTimeSeries;
        }
        else
        {
            var validAlgorithms =
                Enum.GetNames(typeof(MLAlgorithmBinaryClassification))
                    .Concat(Enum.GetNames(typeof(MLAlgorithmRegression)))
                    .Concat(Enum.GetNames(typeof(MLAlgorithmTimeSeries)));

            throw new ArgumentException(
                $"Specified algorithm '{algorithmStr}' is not valid. Valid values: {string.Join(", ", validAlgorithms)}");
        }


        // Validate required metric argument
        if (!flags.TryGetValue("metric", out var metricStr) || string.IsNullOrWhiteSpace(metricStr))
            throw new ArgumentException("Required argument metric has not been supplied, or is empty.");

        if (!Enum.TryParse<ConflictDefinition>(metricStr.Trim(), true, out var metric))
        {
            var validMetrics = Enum.GetNames(typeof(ConflictDefinition));
            throw new ArgumentException(
                $"Specified metric '{metricStr}' is not valid. Valid values: {string.Join(", ", validMetrics)}");
        }


        // Validate required time argument
        if (!flags.TryGetValue("time", out var timeStr) || string.IsNullOrWhiteSpace(timeStr))
            throw new ArgumentException("Required argument time has not been supplied, or is empty.");

        if (!Enum.TryParse<TimePrediction>(timeStr.Trim(), true, out var timespan))
        {
            var validTimes = Enum.GetNames(typeof(TimePrediction));
            throw new ArgumentException(
                $"Specified time '{timeStr}' is not valid. Valid values: {string.Join(", ", validTimes)}");
        }


        // Call the model builder using enums
        ConflictCalc.StaticHelpers.RegressionMLHelper.BuildModel(
            filePath,
            placeName,
            algoBinary ?? (object)algoRegression ?? (object)algoTimeSeries,   // whichever enum was parsed
            timespan,
            metric
        );


    }

    catch (ArgumentException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(ex.Message);
    }

    catch (FileNotFoundException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(ex.Message);

    }
    finally
    {
        Console.ResetColor();

    }

}