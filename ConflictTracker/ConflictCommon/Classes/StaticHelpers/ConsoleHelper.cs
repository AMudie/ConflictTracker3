using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictCommon.Classes.StaticHelpers
{
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, CommandInfo> _commands =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a command with description and optional parameter metadata.
        /// </summary>
        public static void Register(
            string name,
            Action<Dictionary<string, string>> handler,
            string description,
            Dictionary<string, string>? parameters = null)
        {
            _commands[name] = new CommandInfo(handler, description, parameters);
        }

        /// <summary>
        /// Try to get a command by name.
        /// </summary>
        public static bool TryGet(string name, out CommandInfo info)
            => _commands.TryGetValue(name, out info);

        /// <summary>
        /// List all registered command names.
        /// </summary>
        public static IEnumerable<string> GetRegisteredCommands()
            => _commands.Keys;

        /// <summary>
        /// Get full metadata for a command (handler, description, parameters).
        /// </summary>
        public static CommandInfo? GetInfo(string name)
        {
            _commands.TryGetValue(name, out var info);
            return info;
        }
    }

    public static class CommandProcessor
    {
        public static void Process(string input)
        {
            var tokens = ConsoleHelper.Tokenize(input);

            if (tokens.Count == 0)
                return;

            var command = tokens[0].ToLowerInvariant();
            var args = tokens.Skip(1).ToList();

            // NEW: retrieve CommandInfo instead of handler
            if (CommandRegistry.TryGet(command, out var info))
            {
                var flags = ConsoleHelper.ParseFlags(args);
                info.Handler(flags);   // NEW: call the handler from CommandInfo
            }
            else
            {
                Console.WriteLine($"Unknown command: {command}");
            }
        }
    }

    public class CommandInfo
    {
        public Action<Dictionary<string, string>> Handler { get; }
        public string Description { get; }
        public Dictionary<string, string> Parameters { get; }

        public CommandInfo(
            Action<Dictionary<string, string>> handler,
            string description,
            Dictionary<string, string>? parameters = null)
        {
            Handler = handler;
            Description = description;
            Parameters = parameters ?? new Dictionary<string, string>();
        }
    }


    public static class CommonCommands
    {
        public static void Help(Dictionary<string, string> flags)
        {

            if (flags.Count == 0)
            {
                Console.WriteLine("Available commands:");
                {
                    foreach (var cmd in CommandRegistry.GetRegisteredCommands())
                    {
                        Console.WriteLine($"  {cmd}");
                    }
                }
            }
            else
            { 
                
                string matchedCommand = CommandRegistry.GetRegisteredCommands().SingleOrDefault(cmd => cmd.Equals(flags.Values.First(), StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(matchedCommand))
                {
                    Console.WriteLine($"{flags.First().Value} is not a registered command.");
                }
                else
                {
                    CommandInfo? info = CommandRegistry.GetInfo(matchedCommand);
                    if (info != null) 
                    {
                        Console.WriteLine($"{info.Description} ");
                        if (info.Parameters.Count > 0)
                        {
                            Console.WriteLine("Parameters: ");
                            foreach (KeyValuePair<string, string> parameter in info.Parameters)
                            {
                                Console.WriteLine($"-{parameter.Key} : {parameter.Value}");
                            }
                        }
                    }

                }
            }



        }
        public static void Exit(Dictionary<string, string> flags)
        {
            Environment.Exit(0);
        }
    }
    public static class ConsoleHelper
    {
        public static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }


        public static Dictionary<string, string> ParseFlags(List<string> tokens)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? currentFlag = null;
            var valueBuilder = new List<string>();

            foreach (var token in tokens)
            {
                if (token.StartsWith("-"))
                {
                    // Save previous flag/value pair
                    if (currentFlag != null && valueBuilder.Count > 0)
                        dict[currentFlag] = string.Join(" ", valueBuilder);

                    currentFlag = token.TrimStart('-');
                    valueBuilder.Clear();
                }
                else
                {
                    valueBuilder.Add(token);
                }
            }

            // Save last flag/value pair
            if (currentFlag != null && valueBuilder.Count > 0)
                dict[currentFlag] = string.Join(" ", valueBuilder);

            return dict;
        }
    }
}
