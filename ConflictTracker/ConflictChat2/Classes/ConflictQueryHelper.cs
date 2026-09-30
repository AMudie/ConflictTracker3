using ConflictCommon.Classes.StaticHelpers;
using Neo4j.Driver;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace ConflictChat2.Classes
{
    /// <summary>
    /// Wrapper class for querying the ConflictQuery server. It provides methods to send requests and receive predictions about conflicts based on the specified parameters.
    /// </summary>
    internal static class ConflictQueryClient
    {
        /// <summary>
        /// Class to define the structure of the request to the conflict query server, must match that in ConflictQuery.py
        /// </summary>
        public class ConflictRequest
        {
            public string Place { get; set; }
            public string Country { get; set; }
            public DateOnly PeriodStart { get; set; }
            public string ModelType { get; set; }   // "chronos2" or "lightgbm"
        }

        /// <summary>
        /// Class to define the structure of the response from the conflict query server, must match that in ConflictQuery.py
        /// </summary>
        public class ConflictResponse
        {
            public string Place { get; set; }
            public string Country { get; set; }
            public string PeriodStart { get; set; }
            public Dictionary<string, int> Predictions { get; set; }
            public string ModelVersion { get; set; }
        }
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Performs the conflict query asynchronously and returns the response.
        /// </summary>
        /// <param name="baseUrl"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        private static async Task<ConflictResponse> QueryConflictAsync(
            string baseUrl,
            ConflictRequest? request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request), "Request cannot be null.");
            }
            else if (string.IsNullOrEmpty(request.ModelType))
            {
                request.ModelType = AppSettingsHelper.LoadAppSetting("ConflictQueryInstanceSettings:PredictionModel");
            }
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = AppSettingsHelper.LoadAppSetting("ConflictQueryInstanceSettings:BaseURL");
            }


            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL cannot be empty.", nameof(baseUrl));

            var endpoint = $"{baseUrl.TrimEnd('/')}{AppSettingsHelper.LoadAppSetting("ConflictQueryInstanceSettings:PredictionEndpoint")}";

            var response = await _http.PostAsJsonAsync(endpoint, request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception(
                    $"ConflictQuery failed with status {response.StatusCode}. Body: {body}");
            }

            var result = await response.Content.ReadFromJsonAsync<ConflictResponse>();

            if (result == null)
                throw new Exception("ConflictQuery returned an empty or invalid response.");

            return result;
        }

        /// <summary>
        /// Queris the ConflictQuery server for a prediction, and returns a string representation of the result.
        /// </summary>
        /// <param name="baseUrl">URL of the conflict query server</param>
        /// <param name="request">The conflict request (country, name, model, period)</param>
        /// <returns>A string representation of the conflict prediction</returns>
        public static string QueryConflict(string baseUrl, ConflictRequest request)
        {
            var result = QueryConflictAsync(baseUrl, request)
                            .GetAwaiter()
                            .GetResult();

            string returnValue = "";

            if (result != null)
            {
                int index = 0;

                //Shortcut if no conflict is predicted:
                if (result.Predictions.All(p => p.Value == 0))
                {
                    return $"REDICTION:{Environment.NewLine}Neither local or regional conflict is predicted in {request.Place} in {request.Country} in 48 weeks from {request.PeriodStart}";
                }
                else
                {
                    //normal path of "some conflict:"
                    foreach (var p in result.Predictions.Keys)
                    {

                        if (index > 0)
                        {
                            returnValue += Environment.NewLine;
                        }
                        else
                        {
                            returnValue += $"PREDICTION:{Environment.NewLine}";
                        }

                        string area = "";
                        string t = p.Substring(p.IndexOf("_") + 1);
                        string pred = result.Predictions[p] == 1 ? "Conflict" : "No conflict";   //in-line if, 1 == conflict else no conflict. 

                        const string conflictprediction = " conflict prediction at period t+ ";

                        if (p.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase))
                            area = "Local";

                        if (p.StartsWith("REGIONAL", StringComparison.OrdinalIgnoreCase))
                            area = "Regional";

                        returnValue += $"{area}{conflictprediction}{t}: {pred}";
                        index += 1;
                    }
                }
            }

            return returnValue;
        }
    }
}
