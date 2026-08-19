using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictChat2.Classes
{
    internal class SummarisationHelper
    {

            private const string _lastNMessagesReplacement = "[INSERT LAST N MESSAGES HERE]";

            private const string _summarisationPrompt = """
            <|system|>
            You are a summariser.Your task is to read the conversation below and produce a concise summary.
            The summary must:
            - be 2–4 sentences
            - be written in the past tense
            - capture the emotional tone
            - capture the main topics discussed
            - avoid quoting long passages
            - avoid role tags
            - avoid instructions
            - be written in neutral third-person

            Here is the conversation to summarise:

            [INSERT LAST N MESSAGES HERE]

            Provide the summary now.

            <|assistant|>
            
            """;

           

            public static string GenerateSummarisationPrompt( List<(string role, string content)> conversationParts)
            {

                string conversation = string.Empty;
                foreach (var part in conversationParts)
                {
                    string role = part.role.ToString();
                    if (role == "assistant")
                    {
                        role = "assistant";
                    }
                    string line = string.Concat(role, ": ", part.content.ToString());
                    if (conversation.Length > 0)
                    {
                        conversation += Environment.NewLine;
                    }
                    conversation += line.Trim();
                    conversation = StringHelper.RemoveAllChatMLTags(conversation);
                }

                return _summarisationPrompt.Replace(_lastNMessagesReplacement, conversation);

            }
 

        }
    
}
