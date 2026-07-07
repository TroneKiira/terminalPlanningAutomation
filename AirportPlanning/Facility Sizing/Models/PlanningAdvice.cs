using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class PlanningAdvice
    {
        public string Recommendation { get; set; }
        public string Reason { get; set; }
        public string Severity { get; set; }

        public PlanningAdvice(string recommendation, string reason, string severity = "Info")
        {
            Recommendation = recommendation;
            Reason = reason;
            Severity = severity;
        }
    }
}
