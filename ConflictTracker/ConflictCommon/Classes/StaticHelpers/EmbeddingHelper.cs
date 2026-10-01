using System;
using System.Collections.Generic;
using System.Text;
using Ollama;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Text;
using System.Text.Json;

namespace ConflictCommon.Classes.StaticHelpers
{
    /// <summary>
    /// USes the nomic-embed-text model to convert strings into a vector of length 768. If the vectors would be too large, mean-pooling is used to average all the vectors into a single vector. This is desirable as the single vector is a requirement of Neo4J's cosine similarity function.
    /// </summary>
    public class EmbeddingHelper
    {

        private const int _embeddingModelVectorLength = 768;
        private const string _embeddingModelName = "nomic-embed-text";

        private static async Task<double[][]> GetEmbeddings(string content)
        {
            try
            {
                const int MaxChunkSize = 1000; // safe for all-minilm

                List<string> chunks = ChunkString(content, MaxChunkSize);

                OllamaClient client = new OllamaClient(baseUri: new Uri("http://localhost:11434"));

                List<double[]> allEmbeddings = new List<double[]>();

                foreach (var chunk in chunks)
                {
                    // Send ONE chunk per request
                    EmbedResponse response = await client.EmbedAsync(
                        model: _embeddingModelName,
                        input: new List<string> { chunk }
                    );

                    // Should always be exactly one embedding
                    allEmbeddings.Add(response.Embeddings[0].ToArray());
                }

                return allEmbeddings.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        public static async Task<double[]> GenerateSingleEmbedding(string content)
        {
            double[][] chunkEmbeddings = await GetEmbeddings(content);

            if (chunkEmbeddings == null || chunkEmbeddings.Length == 0)
                return Array.Empty<double>();

            int vectorSize = chunkEmbeddings[0].Length;
            double[] averaged = new double[vectorSize];

            foreach (var vec in chunkEmbeddings)
            {
                for (int i = 0; i < vectorSize; i++)
                    averaged[i] += vec[i];
            }

            // Average
            for (int i = 0; i < vectorSize; i++)
                averaged[i] /= chunkEmbeddings.Length;

            return averaged;
        }

        private static List<string> ChunkString(string text, int chunkSize)
        {
            List<string> chunks = new List<string>();

            for (int i = 0; i < text.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, text.Length - i);
                chunks.Add(text.Substring(i, length));
            }

            return chunks;
        }

        /// <summary>
        /// Cosine similarity between two vectors,
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        //[Obsolete("CosineSimilarity() is not required and must not be used as cosine similaritywill be done in the kg for performance.", true)]
        public static double CosineSimilarity(double[][] a, double[][] b)
        {
            // Convert each jagged embedding into a single averaged vector
            double[] vecA = MeanPool(a);
            double[] vecB = MeanPool(b);

            // Now both vectors are guaranteed same length
            double dot = 0.0;
            double magA = 0.0;
            double magB = 0.0;

            for (int i = 0; i < vecA.Length; i++)
            {
                dot += vecA[i] * vecB[i];
                magA += vecA[i] * vecA[i];
                magB += vecB[i] * vecB[i];
            }

            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }

        private static double[] MeanPool(double[][] vectors)
        {
            int dim = vectors[0].Length;
            double[] result = new double[dim];

            foreach (var v in vectors)
            {
                for (int i = 0; i < dim; i++)
                    result[i] += v[i];
            }

            for (int i = 0; i < dim; i++)
                result[i] /= vectors.Length;

            return result;
        }
    }
}