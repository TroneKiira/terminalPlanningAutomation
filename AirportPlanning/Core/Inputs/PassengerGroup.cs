using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Inputs
{
    public class PassengerGroup
    {
        public DHP DHP { get; set; }

        public List<Models.PassengerProfile> Profiles { get; set; } = new List<Models.PassengerProfile>();

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (DHP == null)
                errors.Add("DHP is required.");

            if (Profiles == null || Profiles.Count == 0)
            {
                errors.Add("At least one passenger profile is required.");
            }
            else
            {
                double sum = Profiles.Where(p => p != null).Sum(p => p.Percentage);

                if (System.Math.Abs(sum - 100.0) > 0.001)
                    errors.Add($"Profile percentages must sum to 100% (got {sum:0.###}%).");
            }

            return errors;
        }
    }
}
