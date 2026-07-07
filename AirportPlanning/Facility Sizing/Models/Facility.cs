using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Facility_Sizing.Templates;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class Facility
    {
        public FacilityTemplate Template { get; set; }
        public List<FunctionalZone> FunctionalZones { get; set; }
        public List<Processor> Processors { get; set; }
        public List<Equipment> EquipmentList { get; set; }
        public FacilitySizingResult SizingResult { get; set; }

        public Facility()
        {
            FunctionalZones = new List<FunctionalZone>();
            Processors = new List<Processor>();
            EquipmentList = new List<Equipment>();
        }

        public void Initialize(FacilityTemplate template)
        {
            Template = template;
        }

        public void BuildSpatialProgram(FacilitySizingResult result)
        {
            SizingResult = result;
        }

        public double GetTotalArea()
        {
            if (SizingResult != null)
                return SizingResult.TotalArea;

            return FunctionalZones.Sum(z => z.Area);
        }
    }
}
