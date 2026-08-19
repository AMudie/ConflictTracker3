using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictCommon.Classes.DTOs
{
    public class GeographicalPlace
    {
        public string Name { get; set; }
        public string? ParentName { get; set; }   // null or empty if top-level
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string Country { get; set; }

        public  bool IsCapital
        {
            get
            {
                Dictionary<string, string> capitals = new Dictionary<string, string>
    {
    { "EGYPT", "Cairo" },
    { "LIBYA", "Tripoli" },
    { "CHAD", "Ndjamena" }, //technically should be N'djamena but the ACLED dataset gives Ndjamena
    { "SUDAN", "Khartoum" },
    { "SOUTH SUDAN", "Juba" },
    { "KENYA", "Nairobi" },
    { "UGANDA", "Kampala" },
    { "ETHIOPIA", "Addis Ababa" },
    { "SOMALIA", "Mogadishu" },
    { "CENTRAL AFRICAN REPUBLIC", "Bangui" } 
        };

                if (capitals.ContainsKey(this.Country.Trim().ToUpper()) && this.Name.Trim().ToUpper() == capitals[this.Country.Trim().ToUpper()].Trim().ToUpper())
                {
                    return true;
                }

                return false;
            }
        }
    }
}
