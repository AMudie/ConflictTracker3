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

        //Latitude and longitude are in decimal degrees, and GeoPrecision is an integer representing the precision of the coordinates (e.g., 1 for country-level, 2 for state-level, 3 for city-level, etc.)
        //Latitude and Longitude are now replicated onto Events as per the ACLED data, this make identifying the actual locality easier as opposed to trying to match up to a Place by Country and Place name. 
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public int GeoPrecision { get; set; }

        public bool CivilainTargetting { get; set; }
    }
}
