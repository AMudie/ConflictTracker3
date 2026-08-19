using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ConflictChat.Classes
{

    internal static class StringHelper
    {

        public const string AssistantTag = "<|assistant|>";
        public const string SystemTag = "<|system|>";
        public const string UserTag = "<|user|>";

        public static string RemoveAssistantTags(string content)
        {
            return RemoveChatMLTag(content, AssistantTag);
        }


        public static string RemoveSystemTags(string content)
        {
            return RemoveChatMLTag(content, SystemTag);
        }

        public static string RemoveUserTags(string content)
        {
            return RemoveChatMLTag(content, UserTag);
        }

        public static string RemoveAllChatMLTags(string content)
        {
            return RemoveChatMLTag(RemoveChatMLTag(RemoveChatMLTag(content, AssistantTag), SystemTag), UserTag);
        }


        private static string RemoveChatMLTag(string content, string tag)
        {
            while (content.ToLower().Contains(tag))
            {
                //int index = content.IndexOf(tag);
                content = content.Replace(tag, "");
                content = content.Trim();
            }
            return content;
        }

        public static int CountChatMLTags(string content, string chatMLTag)
        {

            string[] parts = content.ToLowerInvariant().Split(chatMLTag);
            return parts.Count(x => x != string.Empty);
        }


        public static bool ContainsChatMLTag(string content, string chatMLTag)
        {
            return content.ToLower().Contains(chatMLTag);
        }

        #region "Model response cleaning"

        /// <summary>
        /// Models can do some odd things while attempting to stay in character. I've seen rogue pipes, HTML tags, weird memory tags, and excessive new lines. This method attempts to clean up the model's response by removing any memory tags and appending the content of those tags to the end of the response, and removing excessive new lines. The cleaned response is then trimmed and returned. This should help ensure that the model's response is more coherent and easier to read, while still preserving any important information contained within memory tags.
        /// </summary>
        /// <param name="content">string to clean.</param>
        /// <returns>The same string, but cleaned.</returns>
        /// <remarks>Because model responses are not deterministic, additional cleaning will added as required.</remarks>
        public static string CleanModelResponse(string content)
        {
            content = ExtractMemoriesAndAppend(content);
            content = RemoveExcessiveNewLines(content);
            content = RemoveRogueAssistantEndTags(content);
            return content.Trim();
        }

        private static string RemoveExcessiveNewLines(string content)
        {
            while (content.Contains("\n\n\n"))
            {
                content = content.Replace("\n\n\n", "\n\n");
            }
            return content;
        }

        private static string ExtractMemoriesAndAppend(string content)
        {

            var memories = new List<string>();

            while (true)
            {
                int start = content.IndexOf("<memory>", StringComparison.OrdinalIgnoreCase);
                if (start == -1) break;

                int end = content.IndexOf("</memory>", start, StringComparison.OrdinalIgnoreCase);
                if (end == -1) break;

                end += "</memory>".Length;

                // Extract the memory block
                string block = content.Substring(start, end - start).Trim();
                memories.Add(block);

                // Remove the block from content
                content = content.Remove(start, end - start).Trim();
            }

            // Append all memories at the end (or wherever you want)
            if (memories.Count > 0)
            {
                content = content.Trim() + Environment.NewLine + string.Join(Environment.NewLine, memories);
            }

            return content;

        }

        /// <summary>
        /// I've seen the models respond with end tags for the ChatML assistant tag. That's not valid syntax. Removing it helps stopping the model getting confused. 
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private static string RemoveRogueAssistantEndTags(string content)
        {
            const string targetString = "</|assistant|>";
            while (content.ToLowerInvariant().Contains(targetString))
            {
                string c = content.ToLowerInvariant();
                content = content.Remove(c.IndexOf(targetString), targetString.Length);
            }
            return content;
        }

        #endregion

#region String cleaning for NER"
        public static string RemoveBracketAndContent(string input)
        {
            if (input == null) return null;
            int startIndex = input.IndexOf('(');
            int endIndex = input.IndexOf(')');
            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                string toRemove = input.Substring(startIndex, endIndex - startIndex + 1);
                return input.Replace(toRemove, "").Trim();
            }
            return input;
        }

        public static string RemoveSpecialChars(string input)
        {
            if (input != "")
            {
                return input;
            }
            else
            {
                return new string(input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
            }
        }

        public static string CleanStringForNER(
            string input
            )
        {
            return RemoveSpecialChars(RemoveBracketAndContent(input));
        }

        #endregion

    }
}