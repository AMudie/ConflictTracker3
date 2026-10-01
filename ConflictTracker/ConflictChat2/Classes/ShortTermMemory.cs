using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictChat2.Classes
{
    /// <summary>
    /// Exists to allow the model to maintain conversation context between messages. Note that two memories actually exist, one is the "true" (immutable) conversation history and one is subject to summarisation at a particular number of entries, which is used to help performance as the chat goes on.
    /// </summary>
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


        //public string GetMemoryAsString()
        //{
        //    string memory = string.Empty;
        //    foreach (var (role, content, isSummary) in _truncatableMemories)
        //    {
        //        if (isSummary)
        //        {
        //            memory += $"[Summary]: {content}{Environment.NewLine}";
        //        }
        //        else
        //        {
        //            memory += $"{role}: {content}{Environment.NewLine}";
        //        }
        //    }
        //    return memory.Trim();
        //}
    }
}
