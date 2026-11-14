using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;

namespace ConflictCommon.Classes.DTOs
{
    public class SLMConflictPredictionDTO
    {
        public string Country;
        public string Place;
        public string SLMConflictPrediction;

        public bool ActualConflictPrediction;

        public string Reasons;
    }
}
