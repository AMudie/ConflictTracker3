using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ConflictChat2.Classes
{
    public static class OllamaHelper
    {
        private static Process? _ollamaProcess;
        private static bool _startedByApp = false;

        /// <summary>
        /// Starts Ollama if not already running.
        /// </summary>
        /// <returns>Null</returns>
        public static async Task EnsureOllamaRunning()
        {
            if (await IsOllamaRunning())
            {
                Console.WriteLine("Ollama is already running.");
                return;
            }

            Console.WriteLine("Ollama is not running. Starting ollama serve...");

            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                _ollamaProcess = Process.Start(startInfo);
                _startedByApp = true;

                Console.WriteLine("Ollama started successfully.");

                // Register shutdown hook once
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start Ollama: " + ex.Message);
            }
        }

        /// <summary>
        /// Checks if Ollama is running by making a basic request to the model. 
        /// </summary>
        /// <returns>True if running, false otherwise. </returns>
        private static async Task<bool> IsOllamaRunning()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMilliseconds(500);

                var response = await client.GetAsync("http://localhost:11434/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Supposed to shut down ollama (e.g. on application exit); not entirely convinced this is properly functional. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnProcessExit(object? sender, EventArgs e)
        {
            if (_startedByApp && _ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("Shutting down Ollama...");
                    _ollamaProcess.Kill(true);
                }
                catch
                {
                    // Ignore shutdown errors
                }
            }
        }
    }

}
