using AirportPlanning.Core.Schedules;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    /// <summary>
    /// Exposes every scalar field of the parsed schedule as a primitive
    /// Grasshopper list. The typed schedule object stays available from the
    /// import component for code-based downstream processes; this component is
    /// for inspection, panels and native Grasshopper data operations.
    /// </summary>
    public class FlightScheduleDeconstructComponent : GH_Component
    {
        public FlightScheduleDeconstructComponent()
          : base("Flight Schedule Deconstruct", "FSD",
            "Deconstructs a parsed flight schedule into primitive lists, one item per leg.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Flight Schedule", "FS",
                "Flight Schedule result from Flight Schedule Import.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "Source Row", "R",
                "Workbook row number for each leg.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Direction", "DIR",
                "Arrival or Departure.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Airline", "AL",
                "Airline code as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Flight Number", "FLT",
                "Flight number as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Rotation ID", "RID",
                "Rotation or turn identifier as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Station", "STN",
                "Origin for an arrival, destination for a departure.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Via", "VIA",
                "Via point as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Sector", "SEC",
                "Domestic, International or Unknown.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Service Type", "SVC",
                "Passenger, Freight or Other.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Date", "DATE",
                "Operating date, when the workbook or Base Date supplies one.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Time", "TIME",
                "Operating time as HH:mm. Empty when the time is missing or invalid.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Minutes From Midnight", "MIN",
                "Minute of the operating day (0 through 1439).",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Day Offset", "DAY",
                "Additional whole days after the operating day.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Total Minutes", "TOT",
                "Continuous schedule clock: Day Offset x 1440 + Minutes From Midnight.",
                GH_ParamAccess.list);

            pManager.AddBooleanParameter(
                "Has Time", "HT",
                "False when the time cell was blank, invalid or an unresolved formula.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Seats", "SEATS",
                "Seat count. -1 when the file does not provide one.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Load Factor", "PLF",
                "Passenger load factor as stored in the file. -1 when missing.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Passengers", "PAX",
                "Passenger count as stored in the file. -1 when missing.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Terminal", "TERM",
                "Resolved terminal. May be a fallback value; check Terminal Source.",
                GH_ParamAccess.list);

            pManager.AddBooleanParameter(
                "Terminal Unresolved", "TUN",
                "True when the terminal could not be resolved and no fallback was applied.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Terminal Source", "TSRC",
                "Workbook, Fallback, Cargo or None. Fallback means the terminal was assumed, not read from the workbook.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Aircraft Type", "AC",
                "Aircraft type as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "ICAO Code", "ICAO",
                "ICAO code letter as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Stand", "STAND",
                "Stand as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Registration", "REG",
                "Aircraft registration as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Remarks", "REM",
                "Remarks as written in the file.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Link ID", "LINK",
                "Shared by the arrival and departure legs of one turnaround.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Leg Summary", "TXT",
                "One readable line per leg, suitable for a Panel.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FlightScheduleResult schedule = null;

            if (!DA.GetData(0, ref schedule) || schedule == null || schedule.Legs == null)
                return;

            List<FlightLeg> legs = schedule.Legs
                .OrderBy(l => l.SourceRow)
                .ThenBy(l => l.Direction == LegDirection.Arrival ? 0 : 1)
                .ToList();

            DA.SetDataList(0, legs.Select(l => l.SourceRow));
            DA.SetDataList(1, legs.Select(l => l.Direction.ToString()));
            DA.SetDataList(2, legs.Select(l => l.Airline ?? string.Empty));
            DA.SetDataList(3, legs.Select(l => l.FlightNumber ?? string.Empty));
            DA.SetDataList(4, legs.Select(l => l.RotationId ?? string.Empty));
            DA.SetDataList(5, legs.Select(l => l.Station ?? string.Empty));
            DA.SetDataList(6, legs.Select(l => l.Via ?? string.Empty));
            DA.SetDataList(7, legs.Select(l => l.SectorRaw.ToString()));
            DA.SetDataList(8, legs.Select(l => l.ServiceType.ToString()));
            DA.SetDataList(9, legs.Select(l => l.Date ?? string.Empty));
            DA.SetDataList(10, legs.Select(FormatTime));
            DA.SetDataList(11, legs.Select(l => l.MinutesFromMidnight));
            DA.SetDataList(12, legs.Select(l => l.DayOffset));
            DA.SetDataList(13, legs.Select(l => l.TotalMinutesFromMidnight));
            DA.SetDataList(14, legs.Select(l => l.HasTime));
            DA.SetDataList(15, legs.Select(l => l.SeatsRaw ?? -1));
            DA.SetDataList(16, legs.Select(l => l.LoadFactor ?? -1.0));
            DA.SetDataList(17, legs.Select(l => l.PaxRaw ?? -1));
            DA.SetDataList(18, legs.Select(l => l.Terminal ?? string.Empty));
            DA.SetDataList(19, legs.Select(l => l.TerminalUnresolved));
            DA.SetDataList(20, legs.Select(l => l.TerminalSource.ToString()));
            DA.SetDataList(21, legs.Select(l => l.AircraftTypeRaw ?? string.Empty));
            DA.SetDataList(22, legs.Select(l => l.IcaoCodeRaw ?? string.Empty));
            DA.SetDataList(23, legs.Select(l => l.Stand ?? string.Empty));
            DA.SetDataList(24, legs.Select(l => l.Registration ?? string.Empty));
            DA.SetDataList(25, legs.Select(l => l.Remarks ?? string.Empty));
            DA.SetDataList(26, legs.Select(l => l.LinkId ?? string.Empty));
            DA.SetDataList(27, legs.Select(FormatSummary));
        }

        private static string FormatTime(FlightLeg leg)
        {
            if (leg == null || !leg.HasTime)
                return string.Empty;

            return $"{leg.MinutesFromMidnight / 60:00}:{leg.MinutesFromMidnight % 60:00}";
        }

        private static string FormatSummary(FlightLeg leg)
        {
            if (leg == null)
                return string.Empty;

            var parts = new List<string>
            {
                $"Row {leg.SourceRow}",
                leg.Direction.ToString(),
                string.Join(" ", new[] { leg.Airline, leg.FlightNumber }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                leg.Station ?? string.Empty,
                FormatTime(leg),
                leg.PaxRaw.HasValue ? $"{leg.PaxRaw.Value} pax" : "pax n/a",
                leg.SectorRaw.ToString(),
                leg.ServiceType.ToString()
            };

            if (leg.TerminalUnresolved)
            {
                parts.Add("terminal unresolved");
            }
            else if (leg.TerminalSource == TerminalSource.Fallback)
            {
                parts.Add($"terminal {leg.Terminal} (fallback)");
            }
            else if (!string.IsNullOrWhiteSpace(leg.Terminal))
            {
                parts.Add($"terminal {leg.Terminal}");
            }

            return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("69191A2F-5EF2-468F-90C6-58559B29CD58");
    }
}
