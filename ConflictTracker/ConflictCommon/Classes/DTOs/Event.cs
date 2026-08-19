using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictCommon.Classes.DTOs
{
    public class Event
    {
        public string ID { get; set; }

        public string Summary { get; set; }

        public string DisorderType { get; set; }

        public string Type { get; set; }

        public string Subtype { get; set; }

        public string Source { get; set; }

        public string Location { get; set; }

        public int Fatalities { get; set; }

        public string Country { get; set; }

        public List<string> Actors { get; set; }

        public int? Severity { get; set; }

        public DateTime DateTime { get; set; } 

    }
}
