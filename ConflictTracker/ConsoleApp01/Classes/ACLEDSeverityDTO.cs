using System.Text.Json.Serialization;

namespace ConflictConsole.Classes
{
    public class ACLEDSeverityDTO
    {
        [JsonPropertyName("Event type")]
        public string EventType { get; set; }

        [JsonPropertyName("Sub-event type")]
        public string SubEventType { get; set; }

        [JsonPropertyName("Disorder type")]
        public string DisorderType { get; set; }

        [JsonPropertyName("Severity")]
        public int? Severity { get; set; }
    }
}