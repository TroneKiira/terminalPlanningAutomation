using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Inputs
{
    public class PassengerGroup
    {
        public DHP DHP { get; set; }

        /// <summary>
        /// Explicit demand type for schedule-driven generation. When set, this
        /// takes precedence over <see cref="DHP"/>.Type. Leave null when the
        /// group is used with a DHP-based generator and the type should come
        /// from the DHP.
        /// </summary>
        public DHPType? Type { get; set; }

        public List<Models.PassengerProfile> Profiles { get; set; } = new List<Models.PassengerProfile>();

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (DHP == null && !Type.HasValue)
                errors.Add("A DHP or an explicit Type is required.");

            if (DHP != null)
                errors.AddRange(DHP.GetValidationErrors());

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

        internal DHPType ResolvedType
        {
            get
            {
                if (Type.HasValue)
                    return Type.Value;

                return DHP?.Type ?? DHPType.DomesticDeparture;
            }
        }
    }
}
