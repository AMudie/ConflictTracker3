using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Recognizers.Text.DateTime;
using Microsoft.Recognizers.Text;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;



namespace ConflictChat.Classes
{

    /// <summary>
    /// </summary>
    /// <remarks>AI Generated code; Copilot; Prompt: "Break down the steps for time extraction from the user's prompt into C#  methods and wrap in an static class"</remarks>

    public static class NLPTimeExtractor
    {



        public static List<(DateTime Start, DateTime End)> ExtractDateRanges(string text)
        {
            var results = new List<(DateTime, DateTime)>();
            var today = DateTime.Today;

            var parsed = DateTimeRecognizer.RecognizeDateTime(text, Culture.English);

            foreach (var p in parsed)
            {
                if (!p.Resolution.TryGetValue("values", out var valuesObj))
                    continue;

                if (valuesObj is not List<Dictionary<string, string>> values)
                    continue;

                foreach (var val in values)
                {
                    if (!val.TryGetValue("type", out var type))
                        continue;

                    switch (type)
                    {
                        case "date": // Single date → treat as 1‑day range
                            if (val.TryGetValue("value", out var dateStr))
                            {
                                var date = DateTime.Parse(dateStr);
                                results.Add((date, date));
                            }
                            break;

                        case "daterange": // Explicit range
                            if (val.TryGetValue("start", out var startStr) &&
                                val.TryGetValue("end", out var endStr))
                            {
                                var start = DateTime.Parse(startStr);
                                var end = DateTime.Parse(endStr);
                                results.Add((start, end));
                            }
                            break;

                        case "datetimerange": // Sometimes used for ranges too
                            if (val.TryGetValue("start", out var dtStartStr) &&
                                val.TryGetValue("end", out var dtEndStr))
                            {
                                var start = DateTime.Parse(dtStartStr);
                                var end = DateTime.Parse(dtEndStr);
                                results.Add((start, end));
                            }
                            break;

                        case "duration": // e.g. "last 3 months" → infer range
                            if (val.TryGetValue("value", out var durationStr) &&
                                val.TryGetValue("unit", out var unit))
                            {
                                var amount = int.Parse(durationStr);
                                var start = unit switch
                                {
                                    "D" => today.AddDays(-amount),
                                    "W" => today.AddDays(-7 * amount),
                                    "M" => today.AddMonths(-amount),
                                    "Y" => today.AddYears(-amount),
                                    _ => today
                                };
                                results.Add((start, today));
                            }
                            break;
                    }
                }
            }


            Console.ForegroundColor = ConsoleColor.Yellow;
            if (results == null || results.Count == 0)
            {
                Console.WriteLine("No dates to extract from prompt");
            }
            else
            {


                foreach (var (Start, End) in results)
                {
                    // If it's a single‑day range, print it as a single date
                    if (Start.Date == End.Date)
                    {
                        Console.WriteLine($"• {Start:yyyy-MM-dd}");
                    }
                    else
                    {
                        Console.WriteLine($"• {Start:yyyy-MM-dd} → {End:yyyy-MM-dd}");
                    }
                }
            }
            Console.ResetColor();

            return results;
        }
    }

}
