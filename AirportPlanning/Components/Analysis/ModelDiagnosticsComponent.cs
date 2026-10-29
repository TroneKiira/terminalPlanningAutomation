using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Components;
using AirportPlanning.Core.Diagnostics;
using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Emigration;
using AirportPlanning.Facilities.Immigration;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facilities.Security;
using AirportPlanning.Facilities.TerminalEntry;

using Grasshopper.Kernel;

namespace AirportPlanning.Components.Analysis
{
    /// <summary>
    /// Reconciles demand, transfer tagging and every wired facility result
    /// without re-running a solver. Feed it the same results the model already
    /// produces; it reports who should have been served, who was served, and
    /// the passenger IDs that differ.
    /// </summary>
    public class ModelDiagnosticsComponent : GH_Component
    {
        public ModelDiagnosticsComponent()
          : base(
                "Model Diagnostics",
                "DIAG",
                "Audits transfers and every wired service result against the model rules. " +
                "Reports expected versus processed counts, missing/extra passenger IDs, " +
                "transfer service gaps and a CSV export.",
                "Airport Planning",
                "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Optional population to audit. Use the generated population or the output of the " +
                "last facility. When empty, the Generation result or the largest service queue is used.",
                GH_ParamAccess.list);
            pManager[0].Optional = true;

            pManager.AddGenericParameter(
                "Schedule", "SCH",
                "Optional Flight Schedule result. Adds schedule parse, leg identity and " +
                "passenger-to-leg checks.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddGenericParameter(
                "Generation", "GEN",
                "Optional Passenger Generation result (including the schedule-driven generator). " +
                "Adds demand, missing-group and late check-in checks.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddGenericParameter(
                "Transfer Demand", "TD",
                "Optional Transfer Demand result. Adds requested/feasible/effective/tagged " +
                "reconciliation and peak windows.",
                GH_ParamAccess.item);
            pManager[3].Optional = true;

            pManager.AddGenericParameter(
                "Service Results", "SVC",
                "Facility results to audit, one per stage: Check-In, Bag Drop, Security, " +
                "Emigration, Immigration, Terminal Entry, Verification or raw Queue Results. " +
                "Dwell is audited automatically from passenger history.",
                GH_ParamAccess.list);
            pManager[4].Optional = true;

            pManager.AddTextParameter(
                "Service Labels", "LBL",
                "Optional labels in the same order as Service Results. When empty, the service " +
                "type is inferred from the facility history keys.",
                GH_ParamAccess.list);
            pManager[5].Optional = true;

            pManager.AddGenericParameter(
                "Facility Sizing", "SIZE",
                "Optional Facility Sizing results. Adds demand, processor and area checks.",
                GH_ParamAccess.list);
            pManager[6].Optional = true;

            pManager.AddTextParameter(
                "Sizing Labels", "SLBL",
                "Optional labels in the same order as Facility Sizing.",
                GH_ParamAccess.list);
            pManager[7].Optional = true;

            pManager.AddGenericParameter(
                "Gate Results", "GATE",
                "Optional Gate Sizing results. Adds stand, lounge and area reconciliation.",
                GH_ParamAccess.list);
            pManager[8].Optional = true;

            pManager.AddBooleanParameter(
                "Include Info Issues", "INFO",
                "Include informational findings in the Issues output. The Report always keeps them.",
                GH_ParamAccess.item, true);

            pManager.AddIntegerParameter(
                "Max Passenger IDs", "MAX",
                "Maximum passenger IDs listed for each numeric finding. The CSV always keeps one " +
                "row per passenger-level issue.",
                GH_ParamAccess.item, 100);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Report", "R",
                "The complete Diagnostics Report object. Read it from a panel or feed it to a script.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Status", "S",
                "Overall status: PASS, WARN, FAIL or NOT WIRED.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Summary", "SUM",
                "Headline counts, service overview and the first places to look.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Issues", "ISS",
                "One block per finding: code, expected/actual, passenger IDs and details.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Service Table", "SVC-T",
                "Every stage: incoming, applicable, expected, processed, warm-up, bypassed, " +
                "missing, extra, missing history, pools and Wait95.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Transfer Table", "TRF-T",
                "Per scenario: requested, feasible, effective, tagged and the outbound/inbound " +
                "record counts.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Transfer Service Table", "TSV-T",
                "Per transfer service: required, bypassed, no-rules, history-backed processed, " +
                "missing and served-anyway counts.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Peak Table", "PEAK",
                "Transfer services at the busiest rolling 60-minute window.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Passenger Issues", "PAX-I",
                "One line per passenger record that needs investigation, grouped by check code.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Definitions", "DEF",
                "What every count means, including the warm-up and processed-population rules.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Gate Checks", "GATE-T",
                "Stand, contact, bus, remote, lounge and area reconciliation.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Sizing Checks", "SIZE-T",
                "Demand, processor and area checks for every wired sizing result.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "CSV", "CSV",
                "Long-format CSV: one row per finding, service, transfer, peak, passenger issue, " +
                "gate check and sizing check.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> rawPassengers = new List<object>();
            object rawSchedule = null;
            object rawGeneration = null;
            object rawTransferDemand = null;
            List<object> rawServices = new List<object>();
            List<string> serviceLabels = new List<string>();
            List<object> rawSizing = new List<object>();
            List<string> sizingLabels = new List<string>();
            List<object> rawGates = new List<object>();
            bool includeInfo = true;
            int maxIds = 100;

            DA.GetDataList(0, rawPassengers);
            DA.GetData(1, ref rawSchedule);
            DA.GetData(2, ref rawGeneration);
            DA.GetData(3, ref rawTransferDemand);
            DA.GetDataList(4, rawServices);
            DA.GetDataList(5, serviceLabels);
            DA.GetDataList(6, rawSizing);
            DA.GetDataList(7, sizingLabels);
            DA.GetDataList(8, rawGates);
            DA.GetData(9, ref includeInfo);
            DA.GetData(10, ref maxIds);

            List<Passenger> passengers = rawPassengers
                .Select(p => ComponentDataHelpers.Unwrap<Passenger>(p))
                .Where(p => p != null)
                .ToList();

            DiagnosticsInput input = new DiagnosticsInput
            {
                Passengers = passengers,
                Schedule = ComponentDataHelpers.Unwrap<FlightScheduleResult>(rawSchedule),
                Generation = ComponentDataHelpers.Unwrap<PassengerGenerationResult>(rawGeneration),
                TransferDemand = ComponentDataHelpers.Unwrap<TransferDemandResult>(rawTransferDemand),
                MaxPassengerIds = maxIds < 1 ? 1 : maxIds
            };

            input.Services = BuildServiceInputs(rawServices, serviceLabels);
            input.SizingResults = BuildSizingInputs(rawSizing, sizingLabels);
            input.GateResults = rawGates
                .Select(g => ComponentDataHelpers.Unwrap<GateSizingResult>(g))
                .Where(g => g != null)
                .ToList();

            AddHistoryOnlyServices(input);

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(input);

            DA.SetData(0, report);
            DA.SetData(1, report.OverallStatus.ToDisplayLabel());
            DA.SetDataList(2, report.SummaryLines());
            DA.SetDataList(3, report.IssueLines(includeInfo));
            DA.SetDataList(4, report.ServiceTableLines());
            DA.SetDataList(5, report.TransferTableLines());
            DA.SetDataList(6, report.TransferServiceTableLines());
            DA.SetDataList(7, report.PeakTableLines());
            DA.SetDataList(8, report.PassengerIssueLines());
            DA.SetDataList(9, report.DefinitionLines());
            DA.SetDataList(10, report.GateCheckLines());
            DA.SetDataList(11, report.SizingCheckLines());
            DA.SetData(12, report.ToCsv());

            AddRuntimeMessages(report, input, rawServices);
        }

        // -------------------------------------------------------------
        // Service results
        // -------------------------------------------------------------

        private static List<DiagnosticsServiceInput> BuildServiceInputs(
            List<object> raw,
            List<string> labels)
        {
            List<DiagnosticsServiceInput> inputs = new List<DiagnosticsServiceInput>();

            if (raw == null)
                return inputs;

            for (int index = 0; index < raw.Count; index++)
            {
                string label = index < labels.Count ? labels[index] : string.Empty;
                DiagnosticsServiceInput service = MapService(raw[index], label);

                if (service != null)
                    inputs.Add(service);
            }

            return inputs;
        }

        private static DiagnosticsServiceInput MapService(object raw, string label)
        {
            if (raw == null)
                return null;

            CheckInResults checkIn = ComponentDataHelpers.Unwrap<CheckInResults>(raw);
            if (checkIn != null)
            {
                return FromQueue(
                    DiagnosticsServiceKind.CheckIn,
                    label,
                    checkIn.QueueResults,
                    checkIn.PhysicalCounters,
                    checkIn.ActiveCounters);
            }

            BagDropResults bagDrop = ComponentDataHelpers.Unwrap<BagDropResults>(raw);
            if (bagDrop != null)
            {
                return FromQueue(
                    DiagnosticsServiceKind.BagDrop,
                    label,
                    bagDrop.QueueResults,
                    bagDrop.PhysicalCounters,
                    bagDrop.ActiveCounters);
            }

            SecurityResults security = ComponentDataHelpers.Unwrap<SecurityResults>(raw);
            if (security != null)
            {
                return FromQueue(
                    DiagnosticsServiceKind.Security,
                    label,
                    security.QueueResults,
                    security.PhysicalLanes,
                    security.ActiveLanes);
            }

            EmigrationResults emigration = ComponentDataHelpers.Unwrap<EmigrationResults>(raw);
            if (emigration != null)
            {
                return FromQueue(
                    DiagnosticsServiceKind.Emigration,
                    label,
                    emigration.QueueResults,
                    emigration.PhysicalPositions,
                    emigration.ActivePositions);
            }

            ImmigrationResults immigration = ComponentDataHelpers.Unwrap<ImmigrationResults>(raw);
            if (immigration != null)
            {
                return FromQueue(
                    DiagnosticsServiceKind.Immigration,
                    label,
                    immigration.QueueResults,
                    immigration.PhysicalPositions,
                    immigration.ActivePositions);
            }

            TerminalEntryResults entry = ComponentDataHelpers.Unwrap<TerminalEntryResults>(raw);
            if (entry != null)
            {
                DiagnosticsServiceInput service = FromQueue(
                    DiagnosticsServiceKind.TerminalEntry,
                    label,
                    entry.QueueResults,
                    entry.PhysicalPositions,
                    entry.ActivePositions);

                service.Notes.Add(
                    "Transfers bypassing entry: " + entry.TransfersBypassingEntry + ".");

                return service;
            }

            PassengerVerificationResults verification =
                ComponentDataHelpers.Unwrap<PassengerVerificationResults>(raw);

            if (verification != null)
            {
                DiagnosticsServiceInput service = new DiagnosticsServiceInput
                {
                    Kind = DiagnosticsServiceKind.Verification,
                    Label = string.IsNullOrWhiteSpace(label) ? "Verification" : label,
                    EligiblePassengers = verification.EligiblePassengers,
                    RequiredGates = verification.RequiredGates,
                    GateCapacity = verification.GateCapacity,
                    Utilization = verification.Utilization
                };

                if (verification.Passengers != null && verification.Passengers.Count > 0)
                    service.Passengers = verification.Passengers;

                return service;
            }

            QueueResults queue = ComponentDataHelpers.Unwrap<QueueResults>(raw);
            if (queue != null)
            {
                return new DiagnosticsServiceInput
                {
                    Label = label,
                    Queue = queue
                };
            }

            return null;
        }

        private static DiagnosticsServiceInput FromQueue(
            DiagnosticsServiceKind kind,
            string label,
            QueueResults queue,
            int physical,
            int active)
        {
            DiagnosticsServiceInput service = new DiagnosticsServiceInput
            {
                Kind = kind,
                Label = string.IsNullOrWhiteSpace(label) ? kind.ToLabel() : label,
                Queue = queue,
                PhysicalResources = physical,
                ActiveResources = active
            };

            if (queue == null)
            {
                service.Notes.Add(
                    "This facility result has no Queue Results. Only its numeric outputs can be " +
                    "checked; passenger matching is unavailable.");
            }

            return service;
        }

        // -------------------------------------------------------------
        // Sizing and history-only coverage
        // -------------------------------------------------------------

        private static List<DiagnosticsSizingInput> BuildSizingInputs(
            List<object> raw,
            List<string> labels)
        {
            List<DiagnosticsSizingInput> inputs = new List<DiagnosticsSizingInput>();

            if (raw == null)
                return inputs;

            for (int index = 0; index < raw.Count; index++)
            {
                FacilitySizingResult result =
                    ComponentDataHelpers.Unwrap<FacilitySizingResult>(raw[index]);

                if (result == null)
                    continue;

                string label = index < labels.Count && !string.IsNullOrWhiteSpace(labels[index])
                    ? labels[index]
                    : "Sizing result " + (index + 1);

                inputs.Add(new DiagnosticsSizingInput
                {
                    Label = label,
                    PeakDemand = result.PeakDemand,
                    ProcessorCount = result.ProcessorCount,
                    NetArea = result.NetArea,
                    GrossArea = result.TotalArea,
                    Warnings = result.Warnings ?? new List<string>()
                });
            }

            return inputs;
        }

        /// <summary>
        /// Adds a history-backed service row for every facility that wrote a
        /// record on the population but has no wired result. Dwell and any
        /// facility whose queue results were not passed are still audited.
        /// </summary>
        private static void AddHistoryOnlyServices(DiagnosticsInput input)
        {
            if (input.Services == null)
                input.Services = new List<DiagnosticsServiceInput>();

            List<Passenger> population = input.Passengers;

            if ((population == null || population.Count == 0) &&
                input.Generation?.Passengers != null)
            {
                population = input.Generation.Passengers;
            }

            if ((population == null || population.Count == 0) &&
                input.Services != null)
            {
                DiagnosticsServiceInput largest = input.Services
                    .Where(s => s?.Queue?.Passengers != null && s.Queue.Passengers.Count > 0)
                    .OrderByDescending(s => s.Queue.Passengers.Count)
                    .FirstOrDefault();

                if (largest != null)
                    population = largest.Queue.Passengers;
            }

            if (population == null || population.Count == 0)
                return;

            HashSet<DiagnosticsServiceKind> covered =
                new HashSet<DiagnosticsServiceKind>(
                    input.Services
                        .Where(s => s != null)
                        .Select(s => s.Kind == DiagnosticsServiceKind.Unknown
                            ? DiagnosticsServiceKindParser.Resolve(s.Queue, s.Label)
                            : s.Kind)
                        .Where(k => k != DiagnosticsServiceKind.Unknown));

            HashSet<FacilityType> present = new HashSet<FacilityType>();

            CollectHistoryKeys(population, present);

            // A facility served inside another stage writes its history on that
            // stage's output. Bag Drop is a channel inside Check-In, so its
            // history lives on Check-In's processed clones rather than on the
            // wired population. Scan every stage output before deciding that a
            // facility was never used.
            if (input.Services != null)
            {
                foreach (DiagnosticsServiceInput service in input.Services)
                {
                    if (service == null)
                        continue;

                    CollectHistoryKeys(service.Passengers, present);
                    CollectHistoryKeys(service.Queue?.Passengers, present);
                    CollectHistoryKeys(service.Queue?.ProcessedPassengers, present);
                }
            }

            foreach (FacilityType facility in present)
            {
                DiagnosticsServiceKind kind = DiagnosticsServiceKindParser.FromFacilityType(facility);

                if (kind == DiagnosticsServiceKind.Unknown || covered.Contains(kind))
                    continue;

                List<string> notes = new List<string>
                {
                    "Derived from passenger history; no facility result was wired for this stage."
                };

                if (facility == FacilityType.BagDrop)
                {
                    notes.Add("Bag Drop is served as a channel inside Check-In; visits are " +
                              "read from BagDrop history.");
                }

                input.Services.Add(new DiagnosticsServiceInput
                {
                    Kind = kind,
                    Label = kind.ToLabel(),
                    Notes = notes
                });

                covered.Add(kind);
            }
        }

        private static void CollectHistoryKeys(
            List<Passenger> passengers,
            HashSet<FacilityType> keys)
        {
            if (passengers == null)
                return;

            foreach (Passenger passenger in passengers)
            {
                if (passenger?.History == null)
                    continue;

                foreach (FacilityType key in passenger.History.Keys)
                    keys.Add(key);
            }
        }

        // -------------------------------------------------------------
        // Messages
        // -------------------------------------------------------------

        private void AddRuntimeMessages(
            DiagnosticsReport report,
            DiagnosticsInput input,
            List<object> rawServices)
        {
            if (report.PopulationCount == 0 && report.ServiceResultCount == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No passengers and no service results were wired. Only numeric checks can run.");
            }
            else if (report.PopulationCount == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No passenger population was wired, so expected-versus-processed matching is " +
                    "incomplete.");
            }

            if (rawServices != null && rawServices.Count > 0 && input.Services != null)
            {
                List<string> unresolved = input.Services
                    .Where(s => s != null && s.Kind == DiagnosticsServiceKind.Unknown)
                    .Select(s => string.IsNullOrWhiteSpace(s.Label) ? "(unlabelled)" : s.Label)
                    .ToList();

                if (unresolved.Count > 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "Could not identify " + unresolved.Count + " service result(s): " +
                        string.Join(", ", unresolved) +
                        ". Add a label or pass a typed facility result.");
                }
            }

            if (report.ErrorCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    report.ErrorCount + " diagnostic error(s). See the Issues output for " +
                    "passenger IDs and expected counts.");
            }
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("B7E4B0B5-3A2F-4C9E-9E4D-2C6F1A8D7C31"); }
        }
    }
}
