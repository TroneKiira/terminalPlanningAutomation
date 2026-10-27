using AirportPlanning.Core.Area;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// Authors the standards one facility's area is built from: the footprint
    /// of each kind of installed unit, the queue space its waiting passengers
    /// need, any fixed rooms, and the circulation, MEP and structure
    /// allowances.
    ///
    /// Everything is a plain list or number, so a definition can fill it in
    /// from panels, sliders and value lists and change any figure without
    /// touching the plugin.
    /// </summary>
    public class CreateFacilityAreaStandardsComponent : GH_Component
    {
        public CreateFacilityAreaStandardsComponent()
          : base("Facility Area Standards", "A-STD",
            "Area standards for a facility: m2 per installed unit by facility key, queue space per passenger, fixed rooms, and the circulation, MEP and structure allowances.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Facility Keys", "KEYS",
                "One name per kind of installed unit, e.g. Counter, Kiosk, BagDrop. " +
                "Matched against the facility's pool names, case insensitively.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Area per Unit", "APU",
                "Floor area of one installed unit, m2. Excludes general circulation, " +
                "which is added as an allowance.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Queue Space per Pax", "QSP",
                "Queue space for one waiting passenger at that unit, m2. Leave empty " +
                "to use the default below.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddNumberParameter(
                "Default Queue Space per Pax", "QSP-D",
                "Queue space per waiting passenger used when a unit does not state its own, m2.",
                GH_ParamAccess.item, 1.2);

            pManager.AddNumberParameter(
                "Circulation %", "C%",
                "Circulation as a percentage of the net area.",
                GH_ParamAccess.item, 15.0);

            pManager.AddNumberParameter(
                "MEP %", "MEP%",
                "Mechanical, electrical and plumbing as a percentage of the area " +
                "including circulation: plant rooms, risers and services.",
                GH_ParamAccess.item, 10.0);

            pManager.AddNumberParameter(
                "Structure %", "S%",
                "Structure as a percentage of the area including MEP.",
                GH_ParamAccess.item, 5.0);

            pManager.AddTextParameter(
                "Fixed Area Names", "FAN",
                "Names of areas added as a lump rather than sized from a count, " +
                "e.g. Reconciliation room, Isolation suite.",
                GH_ParamAccess.list);
            pManager[6].Optional = true;

            pManager.AddNumberParameter(
                "Fixed Areas", "FA",
                "Area of each named fixed area, m2, in the same order.",
                GH_ParamAccess.list);
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Area Standards", "STD",
                "The area standards and allowances.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> keys = new List<string>();
            List<double> areaPerUnit = new List<double>();
            List<double> queueSpace = new List<double>();
            double defaultQueueSpace = 1.2;
            double circulation = 15.0;
            double mep = 10.0;
            double structure = 5.0;
            List<string> fixedNames = new List<string>();
            List<double> fixedAreas = new List<double>();

            DA.GetDataList(0, keys);
            DA.GetDataList(1, areaPerUnit);
            DA.GetDataList(2, queueSpace);
            DA.GetData(3, ref defaultQueueSpace);
            DA.GetData(4, ref circulation);
            DA.GetData(5, ref mep);
            DA.GetData(6, ref structure);
            DA.GetDataList(7, fixedNames);
            DA.GetDataList(8, fixedAreas);

            FacilityAreaStandardSet standards = new FacilityAreaStandardSet
            {
                DefaultQueueSpacePerPax = Math.Max(0.0, defaultQueueSpace),
                CirculationPercent = Math.Max(0.0, circulation),
                MepPercent = Math.Max(0.0, mep),
                StructurePercent = Math.Max(0.0, structure)
            };

            int count = Math.Max(keys.Count, areaPerUnit.Count);

            for (int i = 0; i < count; i++)
            {
                string key = i < keys.Count ? keys[i] : string.Empty;

                if (string.IsNullOrWhiteSpace(key))
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Facility key {0} is empty and was skipped.", i + 1));

                    continue;
                }

                double area = i < areaPerUnit.Count ? areaPerUnit[i] : 0.0;

                if (area < 0.0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "'{0}' has a negative area per unit and was read as zero.", key));

                    area = 0.0;
                }

                double space = i < queueSpace.Count ? queueSpace[i] : 0.0;

                if (space < 0.0)
                    space = 0.0;

                if (standards.Find(key) != null)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "'{0}' appears more than once; the last one was kept.", key));
                }

                standards.Units.RemoveAll(u =>
                    string.Equals(u.Key.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));

                standards.Units.Add(new FacilityUnitStandard
                {
                    Key = key.Trim(),
                    AreaPerUnit = area,
                    QueueSpacePerPax = space
                });
            }

            if (keys.Count != areaPerUnit.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} facility key(s) and {1} area value(s) were given; they are read in pairs.",
                        keys.Count,
                        areaPerUnit.Count));
            }

            int fixedCount = Math.Max(fixedNames.Count, fixedAreas.Count);

            for (int i = 0; i < fixedCount; i++)
            {
                string name = i < fixedNames.Count ? fixedNames[i] : string.Empty;
                double area = i < fixedAreas.Count ? fixedAreas[i] : 0.0;

                if (string.IsNullOrWhiteSpace(name) && area <= 0.0)
                    continue;

                standards.FixedAreas.Add(new NamedArea
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim(),
                    Area = Math.Max(0.0, area)
                });
            }

            if (fixedNames.Count != fixedAreas.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} fixed area name(s) and {1} fixed area value(s) were given; they are read in pairs.",
                        fixedNames.Count,
                        fixedAreas.Count));
            }

            DA.SetData(0, standards);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("A8D51C40-7E63-4B2F-9C10-5E7A1D3B6F24"); }
        }
    }
}
