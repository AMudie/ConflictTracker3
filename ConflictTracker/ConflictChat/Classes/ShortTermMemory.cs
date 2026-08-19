using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictChat.Classes
{
    public class ShortTermMemory
    {
        private readonly int _maxMessages;
        private readonly List<(string role, string content, bool isSummary)> _truncatableMemories = new();
        private readonly List<(string role, string content)> _immutableMemories = new();

        private const int _summaryMessageCount = 6;

        public ShortTermMemory(int maxMessages = 5)
        {
            _maxMessages = maxMessages;
        }

        ///// <summary>
        ///// Adds the provided message to the short-term memory. The method checks if the new message is different from the last message in memory to prevent repetition. If the new message is not a repeat, it is added to the list of messages. After adding, the method ensures that the total number of messages does not exceed the specified maximum by removing the oldest messages if necessary. This helps maintain a relevant and concise conversation history for the language model to reference when generating responses.
        ///// </summary>
        ///// <param name="role"></param>
        ///// <param name="content"></param>
        ///// <remarks>Prevents adding a message if the message already exists in the memory to stop the modell getting confused. Make sure the user's initial prompt is never added to the memory; adding the assistant reponse to the memory is encouraged.</remarks>
        //public void Add(string role, string content)
        //{

        //    if (_immutableMemories.Count == 0 || _immutableMemories.Last().content != content)
        //    {

        //        _immutableMemories.Add((role, content));

        //        //only add to the memory if not repeating the last message. This is to prevent the memory from being filled with repeated messages, which can happen with some models when they get stuck in a loop.
        //        _truncatableMemories.Add((role, content, false));


        //    }


        //    // Trim oldest messages
        //    if (_truncatableMemories.Count > _maxMessages)
        //        _truncatableMemories.RemoveAt(0);
        //}

        public async Task AddAsync(string role, string content, LLMClient llm)
        {
            if (_immutableMemories.Count == 0 || _immutableMemories.Last().content != content)
            {
                _immutableMemories.Add((role, content));
                _truncatableMemories.Add((role, content, false));
            }

            // If we exceed the limit, summarise instead of dropping
            if (_truncatableMemories.Count > _maxMessages)
            {
                await SummariseOldMessagesAsync(llm);
            }
        }


        public IEnumerable<(string role, string content, bool isSummary)> GetTruncatedMessages(int? lastNMessages = null)
        {
            if (lastNMessages == null)
            {
                return _truncatableMemories;
            }
            else
            {
                //TODO: Replace with a summary of the first n messages. 
                return _truncatableMemories.Skip(Math.Max(0, _truncatableMemories.Count - lastNMessages.Value));
            }

        }

        public IEnumerable<(string role, string content)> GetImmutableMessages(int? lastNMessages = null)
        {
            if (lastNMessages == null)
            {
                return _immutableMemories;
            }
            else
            {
                return _immutableMemories.Skip(Math.Max(0, _immutableMemories.Count - lastNMessages.Value));
            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="personalityName"></param>
        /// <param name="llm"></param>
        /// <returns></returns>
        public async Task<bool> SummariseOldMessagesAsync(LLMClient llm)
        {
            if (_truncatableMemories.Count <= _summaryMessageCount)
                return false;

            // Select the oldest N messages
            var messagesToSummarise = _truncatableMemories
                .Take(_summaryMessageCount)
                .ToList();

            // Build conversation parts for the summariser
            var conversationParts = messagesToSummarise
                .Select(m => (m.role, m.content))
                .ToList();

            string summarisationPrompt =
                SummarisationHelper.GenerateSummarisationPrompt(conversationParts);

            string summary = await llm.AskModelRaw(summarisationPrompt);
            summary = StringHelper.CleanModelResponse(summary);

            if (string.IsNullOrWhiteSpace(summary))
                return false;

            // Remove the old messages
            _truncatableMemories.RemoveRange(0, _summaryMessageCount);

            // Insert the summary at the start
            _truncatableMemories.Insert(0, ("summary", summary, true));

            return true;
        }


        public string GetMemoryAsString()
        {
            string memory = string.Empty;
            foreach (var (role, content, isSummary) in _truncatableMemories)
            {
                if (isSummary)
                {
                    memory += $"[Summary]: {content}{Environment.NewLine}";
                }
                else
                {
                    memory += $"{role}: {content}{Environment.NewLine}";
                }
            }
            return memory.Trim();
        }
    }
}
