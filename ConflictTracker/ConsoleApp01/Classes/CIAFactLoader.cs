using ConflictCommon.Classes.DTOs;
using ConflictConsole.Interfaces;
using Microsoft.Recognizers.Text.Number.Arabic;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace ConflictConsole.Classes
{
    internal class CIAFactLoader : IFactLoader
    {

        private static readonly string[] _relevantSubkeys = new[]
        {
            "Government type",
"Political parties",
"Executive branch",
"Judicial branch",
"Legislative branch",
"Legal system",
"Independence",
"International law organization participation",
"International organization participation",
"Military and security forces",
"Military and security service personnel strengths",
"Military deployments",
"Military equipment inventories and acquisitions",
"Military expenditures",
"Military service age and obligation",
"Disputes - international",
"Land boundaries",
"Maritime claims",
"Ethnic groups",
"Religions",
"Languages",
"Population",
"Population distribution",
"Population growth rate",
"Urbanization",
"Youth unemployment rate (ages 15-24)",
"Population below poverty line",
"GDP (official exchange rate)",
"Real GDP (purchasing power parity)",
"Real GDP per capita",
"Real GDP growth rate",
"Budget",
"Budget surplus (+) or deficit (-)",
"Debt - external",
"Public debt",
"Unemployment rate",
"Natural resources",
"Terrain",
"Refugees and internally displaced persons",
"Illicit drugs",
"Trafficking in persons"

            };

        private static readonly string[] _proseSubKeys = new[]
      {
            "Disputes - international",
"Illicit drugs",
"Refugees and internally displaced persons",
"Trafficking in persons"

            };


        public async Task<CIAFactDTO[]> LoadFactsAsync(string[] args)
        {
            List<CIAFactDTO> facts = new List<CIAFactDTO>();
            if (args.Length != 1)
            {
                throw new ArgumentException($"Invalid args passed. Expected: 1, found: {args.Length}.");
            }
            else if (!args[0].ToUpper().EndsWith(".CSV"))
            {
                throw new ArgumentException($"Passed file or resource is not a .csv file.");
            }
            else if (!File.Exists(args[0]) && !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(args[0]))
            {
                throw new FileNotFoundException("Specified file or resouce is not found.");
            }
            else
            {
                int rowIndex = 0;

                await foreach (var row in ConflictConsole.StaticHelpers.MyCSVHelper.ReadCsvAsync(args[0]))
                {
                    rowIndex++;
                    CIAFactDTO fact = new CIAFactDTO();
                    try
                    {
                        //pull the country and year properties from the file name:
                        string[] pathComponents = args[0].Split(".");
                        string component = pathComponents[pathComponents.Length - 2];
                        string year = component.Substring(component.Length - 4, 4);
                        string country = component.Substring(0, component.IndexOf("_"));


                        fact = new CIAFactDTO
                        {
                            Country = country,
                            Year = int.Parse(year),
                            Key = row["Category"],
                            SubKey = row["Field Name"]
                        };

                        //skip loading this key if not one of the relevant ones. 
                        if (!_relevantSubkeys.Any(sk => sk.ToUpper().Equals(fact.SubKey.ToUpper())))
                        {
                            continue;
                        }

                        //Load values (may be multiple); _proseFields are those where the content is a text passage, not a list fo values so splitting chars are allowed.
                        string[] values = null;
                        if (row["Content"].Contains("|") && !_proseSubKeys.Any(sk => sk.ToUpper().Trim().Equals(fact.SubKey.ToUpper().Trim())))
                        {
                            values = row["Content"].Split("|");
                        }
                        else if (row["Content"].Contains(";") && !_proseSubKeys.Any(sk => sk.ToUpper().Trim().Equals(fact.SubKey.ToUpper().Trim())))
                        {
                            values = row["Content"].Split(";");
                        }
                        else if (row["Content"].Contains(",") && !_proseSubKeys.Any(sk => sk.ToUpper().Trim().Equals(fact.SubKey.ToUpper().Trim())))
                        {
                            values = row["Content"].Split(",");
                        }
                        else
                        {
                            //otherwise load as a single value.
                            values = new string[] { row["Content"] };
                        }

                        //Clean up the values: remove escape chars and 
                        for (int i = 0; i < values.Count(); i++)
                        {
                            values[i] = values[i].Replace("\\", "").Trim('"').Trim();
                        }
                        fact.Values = values.ToList();

                        facts.Add(fact);

                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Failed to load place: {fact.Key}; {fact.SubKey} (row index: {rowIndex}): {ex.Message}");
                        Console.ResetColor();
                    }

                }
            }
            //Return only places where the parent name does not match the name, and the name is not blank.
            //  return facts.Where(x => x.ParentName is null || x.ParentName.ToUpper() != x.Name.ToUpper()).Where(x => !string.IsNullOrWhiteSpace(x.Name)).Distinct().ToArray();

            return facts.ToArray();
        }
    }
}
