using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Diagnostics
{
    /// <summary>
    /// Reconciles the model's demand, tagging and facility results without
    /// re-running a solver. Every service result is audited against an
    /// independent statement of who the service should have served, and every
    /// transfer service is audited against the scenario rules and the facility
    /// history records actually present on the passengers.
    /// </summary>
    public static class DiagnosticsCalculator
    {
        public static DiagnosticsReport Calculate(DiagnosticsInput input)
        {
            return new Engine(input ?? new DiagnosticsInput()).Run();
        }

        private sealed class Engine
        {
            private const int PassengerIssueLimit = 5000;
            private const int DetailLimit = 20;

            private readonly DiagnosticsInput _input;
            private readonly DiagnosticsReport _report = new DiagnosticsReport();

            private List<Passenger> _population = new List<Passenger>();
            private TransferDemandResult _demand;
            private bool _passengerIssuesTruncated;
            private readonly Dictionary<string, int> _serviceLabels =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public Engine(DiagnosticsInput input)
            {
                _input = input;
            }

            public DiagnosticsReport Run()
            {
                ResolvePopulation();
                CheckSchedule();
                CheckGeneration();
                CheckPopulationIntegrity();
                CheckServices();
                CheckTransfers();
                CheckGates();
                CheckSizing();
                FinalizeReport();
                return _report;
            }

            // ---------------------------------------------------------
            // Population
            // ---------------------------------------------------------

            private void ResolvePopulation()
            {
                List<Passenger> source = null;
                string sourceLabel = "none";

                if (_input.Passengers != null && _input.Passengers.Count > 0)
                {
                    source = _input.Passengers;
                    sourceLabel = _input.Generation?.Passengers != null &&
                                  ReferenceEquals(_input.Generation.Passengers, _input.Passengers)
                        ? "generation"
                        : "passengers";
                }
                else if (_input.Generation?.Passengers != null &&
                         _input.Generation.Passengers.Count > 0)
                {
                    source = _input.Generation.Passengers;
                    sourceLabel = "generation";
                }
                else
                {
                    DiagnosticsServiceInput largest = (_input.Services ?? new List<DiagnosticsServiceInput>())
                        .Where(s => s?.Queue?.Passengers != null && s.Queue.Passengers.Count > 0)
                        .OrderByDescending(s => s.Queue.Passengers.Count)
                        .FirstOrDefault();

                    if (largest != null)
                    {
                        source = largest.Queue.Passengers;
                        sourceLabel = "service results";
                    }
                }

                int nulls = source?.Count(p => p == null) ?? 0;

                _population = (source ?? new List<Passenger>())
                    .Where(p => p != null)
                    .ToList();

                _report.PopulationSource = sourceLabel;
                _report.PopulationCount = _population.Count;
                _report.WarmUpCount = _population.Count(p => p.IsWarmUp);
                _report.DesignStepCount = _population.Count(p => p.IsDesignStep);
                _report.OutboundTransferLegs = _population.Count(p =>
                    IsDeparture(p) && p.Journey == PassengerJourney.Transfer);
                _report.InboundTransferLegs = _population.Count(p =>
                    IsArrival(p) && p.Journey == PassengerJourney.Transfer);

                if (nulls > 0)
                {
                    AddFinding(
                        "POP-001",
                        DiagnosticCategory.Coverage,
                        DiagnosticSeverity.Error,
                        "Population contains null records",
                        nulls + " null passenger record(s) were ignored.",
                        expected: 0,
                        actual: nulls);
                }

                if (_population.Count == 0 && HasAnyWiredInput())
                {
                    AddFinding(
                        "POP-002",
                        DiagnosticCategory.Coverage,
                        DiagnosticSeverity.Warning,
                        "No passenger population",
                        "No Passengers, Generation or service Queue Results were supplied. " +
                        "Only numeric result checks can run.");
                }
            }

            private bool HasAnyWiredInput()
            {
                return (_input.Services != null && _input.Services.Count > 0) ||
                       (_input.GateResults != null && _input.GateResults.Count > 0) ||
                       (_input.SizingResults != null && _input.SizingResults.Count > 0) ||
                       _input.Schedule != null ||
                       _input.Generation != null ||
                       _input.TransferDemand != null;
            }

            // ---------------------------------------------------------
            // Schedule
            // ---------------------------------------------------------

            private void CheckSchedule()
            {
                FlightScheduleResult schedule = _input.Schedule;
                if (schedule == null)
                    return;

                List<LegIssue> globalIssues = schedule.GlobalIssues ?? new List<LegIssue>();
                List<FlightLeg> legs = schedule.Legs ?? new List<FlightLeg>();

                List<string> details = new List<string>();
                foreach (LegIssue issue in globalIssues)
                {
                    if (details.Count >= DetailLimit)
                        break;

                    details.Add("Global " + issue.Severity + " - " + issue.Field + ": " + issue.Message);
                }

                foreach (FlightLeg leg in legs)
                {
                    if (leg == null || leg.Issues == null)
                        continue;

                    foreach (LegIssue issue in leg.Issues)
                    {
                        if (details.Count >= DetailLimit)
                            break;

                        details.Add(DescribeLeg(leg) + " - " + issue.Field + ": " + issue.Message);
                    }
                }

                int errors = schedule.ErrorCount;
                int warnings = schedule.WarningCount;

                if (errors > 0)
                {
                    AddFinding(
                        "SCH-001",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Error,
                        "Schedule has parse errors",
                        errors + " schedule error(s) were reported by the import.",
                        expected: 0,
                        actual: errors,
                        details: details);
                }
                else if (warnings > 0)
                {
                    AddFinding(
                        "SCH-002",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Warning,
                        "Schedule has parse warnings",
                        warnings + " schedule warning(s) were reported by the import.",
                        expected: 0,
                        actual: warnings,
                        details: details);
                }

                List<FlightLeg> passengerLegs = legs
                    .Where(l => l != null && l.ServiceType == LegServiceType.Passenger)
                    .ToList();

                List<FlightLeg> noTime = passengerLegs.Where(l => !l.HasTime).ToList();
                if (noTime.Count > 0)
                {
                    AddFinding(
                        "SCH-003",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Error,
                        "Passenger legs without a time",
                        noTime.Count + " passenger leg(s) have no usable time. " +
                        "They cannot generate passengers or take part in a transfer connection.",
                        expected: 0,
                        actual: noTime.Count,
                        legs: noTime.Select(l => l.Id));
                }

                List<FlightLeg> unknownSector = passengerLegs
                    .Where(l => l.HasTime && l.SectorRaw == LegSector.Unknown)
                    .ToList();
                if (unknownSector.Count > 0)
                {
                    AddFinding(
                        "SCH-004",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Warning,
                        "Passenger legs with an unknown sector",
                        unknownSector.Count + " passenger leg(s) do not state Domestic or International. " +
                        "Transfer demand cannot use them.",
                        expected: 0,
                        actual: unknownSector.Count,
                        legs: unknownSector.Select(l => l.Id));
                }

                List<FlightLeg> missingIds = passengerLegs
                    .Where(l => string.IsNullOrWhiteSpace(l.Id))
                    .ToList();
                if (missingIds.Count > 0)
                {
                    AddFinding(
                        "SCH-005",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Warning,
                        "Passenger legs without an identity",
                        missingIds.Count + " passenger leg(s) have no Id. " +
                        "Generated passengers cannot be reconciled back to those legs.",
                        expected: 0,
                        actual: missingIds.Count);
                }

                HashSet<string> knownIds = new HashSet<string>(
                    legs.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id)).Select(l => l.Id),
                    StringComparer.Ordinal);

                if (knownIds.Count > 0)
                {
                    List<Passenger> unknownReferences = _population
                        .Where(p => !string.IsNullOrWhiteSpace(p.FlightLegId) &&
                                    !knownIds.Contains(p.FlightLegId))
                        .ToList();

                    if (unknownReferences.Count > 0)
                    {
                        AddFinding(
                            "SCH-010",
                            DiagnosticCategory.Schedule,
                            DiagnosticSeverity.Error,
                            "Passengers reference flight legs that are not in the schedule",
                            unknownReferences.Count + " passenger record(s) carry a FlightLegId " +
                            "that the wired schedule does not contain.",
                            expected: 0,
                            actual: unknownReferences.Count,
                            ids: unknownReferences.Select(p => p.Id),
                            legs: unknownReferences.Select(p => p.FlightLegId),
                            details: unknownReferences
                                .Take(DetailLimit)
                                .Select(p => "PAX " + p.Id + " -> leg " + p.FlightLegId));
                    }
                }

                int withoutLeg = _population.Count(p => string.IsNullOrWhiteSpace(p.FlightLegId));
                if (withoutLeg > 0 && _population.Count > 0)
                {
                    AddFinding(
                        "SCH-011",
                        DiagnosticCategory.Schedule,
                        DiagnosticSeverity.Info,
                        "Passengers without a flight-leg reference",
                        withoutLeg + " of " + _population.Count + " passenger record(s) have no FlightLegId. " +
                        "Schedule-driven reconciliation is partial for those records.",
                        expected: _population.Count,
                        actual: _population.Count - withoutLeg);
                }
            }

            // ---------------------------------------------------------
            // Generation
            // ---------------------------------------------------------

            private void CheckGeneration()
            {
                PassengerGenerationResult generation = _input.Generation;
                if (generation == null)
                    return;

                List<Passenger> generated = generation.Passengers ?? new List<Passenger>();

                if (generated.Count == 0)
                {
                    AddFinding(
                        "GEN-001",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Generation produced no passengers",
                        "The wired generation result contains no passengers.",
                        expected: 1,
                        actual: 0);
                }

                if (_input.Passengers != null &&
                    !ReferenceEquals(_input.Passengers, generated) &&
                    _input.Passengers.Count != generated.Count)
                {
                    AddFinding(
                        "GEN-002",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Supplied population differs from generation output",
                        "The Passengers input and the generation result do not contain the same " +
                        "number of records. The supplied population is used for model checks.",
                        expected: generated.Count,
                        actual: _input.Passengers.Count);
                }

                SchedulePassengerGenerationResult scheduleGeneration =
                    generation as SchedulePassengerGenerationResult;

                if (scheduleGeneration == null)
                    return;

                if (scheduleGeneration.GeneratedPassengerCount > 0 &&
                    scheduleGeneration.GeneratedPassengerCount != generated.Count)
                {
                    AddFinding(
                        "GEN-003",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Generated passenger count is stale",
                        "GeneratedPassengerCount does not match the passenger list length.",
                        expected: generated.Count,
                        actual: scheduleGeneration.GeneratedPassengerCount);
                }

                if (_population.Count > 0 && _population.Count == generated.Count)
                {
                    List<string> details = new List<string>();

                    foreach (DHPType type in Enum.GetValues(typeof(DHPType)))
                    {
                        int expected;
                        scheduleGeneration.DemandByType.TryGetValue(type, out expected);
                        int actual = _population.Count(p => p.DHPType == type);

                        if (expected != actual)
                        {
                            details.Add(type + ": demand result " + expected +
                                        ", population " + actual);
                        }
                    }

                    if (details.Count > 0)
                    {
                        AddFinding(
                            "GEN-004",
                            DiagnosticCategory.Generation,
                            DiagnosticSeverity.Warning,
                            "DHPType counts differ from the generation result",
                            "The population does not reproduce the per-type counts recorded at " +
                            "generation. This usually means a different population was wired in.",
                            expected: _population.Count,
                            actual: generated.Count,
                            details: details);
                    }
                }

                List<ScheduleFlightDemand> shortfalls = (scheduleGeneration.FlightDemands ??
                        new List<ScheduleFlightDemand>())
                    .Where(f => f != null && f.Leg != null &&
                                (f.GeneratedCount < f.PassengerCount ||
                                 !string.IsNullOrWhiteSpace(f.Issue)))
                    .ToList();

                if (shortfalls.Count > 0)
                {
                    AddFinding(
                        "GEN-010",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Flight legs generated fewer passengers than demand",
                        shortfalls.Count + " flight leg(s) generated fewer passengers than their " +
                        "demand or carry a generation issue. Missing Passenger Groups/profiles " +
                        "are the usual cause.",
                        expected: shortfalls.Sum(f => f.PassengerCount),
                        actual: shortfalls.Sum(f => f.GeneratedCount),
                        legs: shortfalls.Select(f => f.Leg.Id),
                        details: shortfalls
                            .Take(DetailLimit)
                            .Select(f =>
                            {
                                string text = DescribeLeg(f.Leg) + ": demand " + f.PassengerCount +
                                              ", generated " + f.GeneratedCount;
                                if (!string.IsNullOrWhiteSpace(f.Issue))
                                    text += " - " + f.Issue;
                                return text;
                            }));
                }

                if (scheduleGeneration.GenerationWarnings != null &&
                    scheduleGeneration.GenerationWarnings.Count > 0)
                {
                    AddFinding(
                        "GEN-020",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Generation warnings",
                        scheduleGeneration.GenerationWarnings.Count + " generation warning(s) were reported.",
                        expected: 0,
                        actual: scheduleGeneration.GenerationWarnings.Count,
                        details: Cap(scheduleGeneration.GenerationWarnings));
                }

                int missingGroups = scheduleGeneration.ArrivalLegsWithoutGroup +
                                    scheduleGeneration.DepartureLegsWithoutGroup;

                if (missingGroups > 0)
                {
                    AddFinding(
                        "GEN-021",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Warning,
                        "Legs skipped for a missing Passenger Group",
                        "Some flight legs did not generate passengers because no Passenger Group " +
                        "or profile was assigned.",
                        expected: 0,
                        actual: missingGroups,
                        details: new List<string>
                        {
                            "Departure legs without a group: " +
                            scheduleGeneration.DepartureLegsWithoutGroup,
                            "Arrival legs without a group: " +
                            scheduleGeneration.ArrivalLegsWithoutGroup
                        });
                }

                if (scheduleGeneration.LateCheckInCount > 0)
                {
                    AddFinding(
                        "GEN-030",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Info,
                        "Passengers reach check-in after last admission",
                        scheduleGeneration.LateCheckInCount + " passenger(s) were generated after " +
                        "their flight's last-admission time and will pass through check-in.",
                        expected: scheduleGeneration.GeneratedPassengerCount,
                        actual: scheduleGeneration.GeneratedPassengerCount -
                                scheduleGeneration.LateCheckInCount);
                }

                if (scheduleGeneration.BeforeCheckInOpenCount > 0)
                {
                    AddFinding(
                        "GEN-031",
                        DiagnosticCategory.Generation,
                        DiagnosticSeverity.Info,
                        "Passengers arrive before check-in opens",
                        scheduleGeneration.BeforeCheckInOpenCount + " passenger(s) arrive before " +
                        "the check-in window opens. They wait for service to begin.",
                        expected: scheduleGeneration.GeneratedPassengerCount,
                        actual: scheduleGeneration.BeforeCheckInOpenCount);
                }
            }

            // ---------------------------------------------------------
            // Population integrity
            // ---------------------------------------------------------

            private void CheckPopulationIntegrity()
            {
                if (_population.Count == 0)
                    return;

                List<IGrouping<int, Passenger>> duplicates = _population
                    .GroupBy(p => p.Id)
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicates.Count > 0)
                {
                    int duplicateRecords = duplicates.Sum(g => g.Count() - 1);

                    AddFinding(
                        "PAX-100",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Error,
                        "Duplicate passenger IDs",
                        duplicateRecords + " duplicate passenger record(s) share an Id with another " +
                        "record. Service matching by Id becomes ambiguous.",
                        expected: _population.Select(p => p.Id).Distinct().Count(),
                        actual: _population.Count,
                        ids: duplicates.Select(g => g.Key));
                }

                List<Passenger> sectorMismatch = _population
                    .Where(p =>
                        (IsInternational(p.DHPType) && p.PassengerType == PassengerType.Domestic) ||
                        (IsDomestic(p.DHPType) && p.PassengerType == PassengerType.International))
                    .ToList();

                AddPassengerIssues(
                    sectorMismatch,
                    "PAX-101",
                    DiagnosticSeverity.Error,
                    "DHPType sector and PassengerType disagree. Emigration keys on PassengerType, " +
                    "Immigration keys on DHPType, so the two facilities can disagree about this record.");

                if (sectorMismatch.Count > 0)
                {
                    AddFinding(
                        "PAX-101",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Error,
                        "Passenger sector identity is inconsistent",
                        sectorMismatch.Count + " passenger record(s) have a DHPType sector that " +
                        "disagrees with PassengerType.",
                        expected: 0,
                        actual: sectorMismatch.Count,
                        ids: sectorMismatch.Select(p => p.Id));
                }

                List<Passenger> transferWithoutScenario = _population
                    .Where(p => p.Journey == PassengerJourney.Transfer && !p.TransferScenario.HasValue)
                    .ToList();

                AddPassengerIssues(
                    transferWithoutScenario,
                    "TRF-100",
                    DiagnosticSeverity.Warning,
                    "Transfer record has no TransferScenario. Its transfer service rules cannot be " +
                    "reconciled.");

                if (transferWithoutScenario.Count > 0)
                {
                    AddFinding(
                        "TRF-100",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Warning,
                        "Transfer records without a scenario",
                        transferWithoutScenario.Count + " transfer record(s) have no TransferScenario.",
                        expected: 0,
                        actual: transferWithoutScenario.Count,
                        ids: transferWithoutScenario.Select(p => p.Id));
                }

                List<Passenger> scenarioWithoutJourney = _population
                    .Where(p => p.TransferScenario.HasValue && p.Journey != PassengerJourney.Transfer)
                    .ToList();

                AddPassengerIssues(
                    scenarioWithoutJourney,
                    "TRF-101",
                    DiagnosticSeverity.Warning,
                    "A TransferScenario is set but Journey is not Transfer.");

                if (scenarioWithoutJourney.Count > 0)
                {
                    AddFinding(
                        "TRF-101",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Warning,
                        "Scenario set on a non-transfer record",
                        scenarioWithoutJourney.Count + " record(s) carry a TransferScenario but are " +
                        "not tagged as transfers.",
                        expected: 0,
                        actual: scenarioWithoutJourney.Count,
                        ids: scenarioWithoutJourney.Select(p => p.Id));
                }

                List<Passenger> staleRules = _population
                    .Where(p => p.Journey != PassengerJourney.Transfer && p.TransferProcessing != null)
                    .ToList();

                if (staleRules.Count > 0)
                {
                    AddFinding(
                        "TRF-102",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Warning,
                        "Transfer rules on a non-transfer record",
                        staleRules.Count + " originating/destination record(s) still carry " +
                        "TransferProcessing rules.",
                        expected: 0,
                        actual: staleRules.Count,
                        ids: staleRules.Select(p => p.Id));
                }

                List<Passenger> warmDesign = _population
                    .Where(p => p.IsWarmUp && p.IsDesignStep)
                    .ToList();

                if (warmDesign.Count > 0)
                {
                    AddFinding(
                        "PAX-102",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Error,
                        "Passenger is both warm-up and design-step",
                        warmDesign.Count + " record(s) are tagged as both. Queue metrics exclude " +
                        "warm-up but the design-step report would include them.",
                        expected: 0,
                        actual: warmDesign.Count,
                        ids: warmDesign.Select(p => p.Id));
                }

                List<Passenger> lateNonDeparture = _population
                    .Where(p => p.IsLateForCheckIn && !IsDeparture(p))
                    .ToList();

                if (lateNonDeparture.Count > 0)
                {
                    AddFinding(
                        "PAX-103",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Warning,
                        "Late-for-check-in flag on a non-departure",
                        lateNonDeparture.Count + " arrival/non-departure record(s) are flagged " +
                        "late for check-in.",
                        expected: 0,
                        actual: lateNonDeparture.Count,
                        ids: lateNonDeparture.Select(p => p.Id));
                }

                int emptyChannel = _population.Count(p => string.IsNullOrWhiteSpace(p.Channel));
                if (emptyChannel > 0)
                {
                    AddFinding(
                        "PAX-104",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Info,
                        "Passengers without a service channel",
                        emptyChannel + " record(s) have no Channel. Channel-routed facility pools " +
                        "cannot serve them unless they use a shared pool.",
                        expected: _population.Count,
                        actual: _population.Count - emptyChannel);
                }

                int emptyProfile = _population.Count(p => string.IsNullOrWhiteSpace(p.ProfileName));
                if (emptyProfile > 0)
                {
                    AddFinding(
                        "PAX-105",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Info,
                        "Passengers without a profile name",
                        emptyProfile + " record(s) have no ProfileName. Profile-keyed service times " +
                        "fall back to the facility default.",
                        expected: _population.Count,
                        actual: _population.Count - emptyProfile);
                }
            }

            // ---------------------------------------------------------
            // Services
            // ---------------------------------------------------------

            private void CheckServices()
            {
                List<DiagnosticsServiceInput> services =
                    _input.Services ?? new List<DiagnosticsServiceInput>();

                for (int index = 0; index < services.Count; index++)
                {
                    DiagnosticsServiceInput service = services[index];
                    if (service == null)
                        continue;

                    AuditService(service, index);
                }

                if (_report.ServiceResultCount == 0)
                {
                    AddFinding(
                        "SVC-000",
                        DiagnosticCategory.Coverage,
                        DiagnosticSeverity.Info,
                        "No service results were wired",
                        "Pass facility Queue Results (or typed facility results) to reconcile " +
                        "expected and processed passenger counts.");
                }

                if (!_report.Services.Any(s => s.Kind == DiagnosticsServiceKind.Dwell))
                {
                    AddFinding(
                        "DWL-001",
                        DiagnosticCategory.Coverage,
                        DiagnosticSeverity.Info,
                        "Dwell was not audited",
                        "No DepartureLounge history record is present on the wired population and " +
                        "no dwell result was supplied. Wire the Departure Lounge result, or a " +
                        "population that already carries its history, to audit dwell time.");
                }
            }

            private void AuditService(DiagnosticsServiceInput service, int index)
            {
                string rawLabel = string.IsNullOrWhiteSpace(service.Label)
                    ? string.Empty
                    : service.Label.Trim();

                DiagnosticsServiceKind kind = service.Kind;
                if (kind == DiagnosticsServiceKind.Unknown)
                    kind = DiagnosticsServiceKindParser.Resolve(service.Queue, rawLabel);

                string label = rawLabel;

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = kind == DiagnosticsServiceKind.Unknown
                        ? "Service " + (index + 1).ToString(CultureInfo.InvariantCulture)
                        : kind.ToLabel();
                }

                label = MakeUniqueLabel(label);

                QueueResults queue = service.Queue;

                List<Passenger> incoming = service.Passengers != null &&
                                           service.Passengers.Count > 0
                    ? service.Passengers.Where(p => p != null).ToList()
                    : queue?.Passengers != null && queue.Passengers.Count > 0
                        ? queue.Passengers.Where(p => p != null).ToList()
                        : _population;

                if (kind == DiagnosticsServiceKind.Verification)
                {
                    AuditVerification(service, label, incoming);
                    return;
                }

                List<Passenger> processed = queue?.ProcessedPassengers != null
                    ? queue.ProcessedPassengers.Where(p => p != null).ToList()
                    : new List<Passenger>();

                FacilityType? historyKey = kind.ToFacilityType();
                bool historyDerived = false;

                bool historyOnly = queue == null && processed.Count == 0;

                if (historyKey.HasValue && historyOnly)
                {
                    // A facility can be served inside another stage's result.
                    // Collect its history from every source before deciding
                    // what this stage served.
                    List<Passenger> withHistory =
                        CollectHistoryBacked(historyKey.Value, incoming);

                    if (withHistory.Count > 0)
                    {
                        processed = withHistory;
                        historyDerived = true;
                    }
                }

                HashSet<int> processedIds = new HashSet<int>(processed.Select(p => p.Id));

                int expected = 0;
                int bypassed = 0;
                int alreadyServed = 0;
                int notApplicable = 0;

                List<Passenger> missingExpected = new List<Passenger>();
                List<Passenger> extraProcessed = new List<Passenger>();
                List<Passenger> expectedPassengers = new List<Passenger>();

                bool canCheckRules = kind != DiagnosticsServiceKind.Unknown &&
                                     incoming.Count > 0 &&
                                     (queue != null || processed.Count > 0);

                if (canCheckRules)
                {
                    foreach (Passenger passenger in incoming)
                    {
                        ServiceExpectation expectation =
                            ServiceEligibility.Evaluate(passenger, kind, processedIds);

                        if (expectation.IsEligible)
                        {
                            expected++;
                            expectedPassengers.Add(passenger);
                            if (!processedIds.Contains(passenger.Id))
                                missingExpected.Add(passenger);
                        }
                        else if (expectation.Reason == ServiceEligibilityReason.NotApplicable)
                        {
                            notApplicable++;
                        }
                        else
                        {
                            bypassed++;
                            if (expectation.Reason == ServiceEligibilityReason.AlreadyServed)
                                alreadyServed++;
                        }
                    }

                    foreach (Passenger passenger in processed)
                    {
                        ServiceExpectation expectation =
                            ServiceEligibility.Evaluate(passenger, kind, processedIds);

                        if (!expectation.IsEligible)
                            extraProcessed.Add(passenger);
                    }
                }

                List<Passenger> missingHistory = historyKey.HasValue
                    ? processed
                        .Where(p => p.History == null || !p.History.ContainsKey(historyKey.Value))
                        .ToList()
                    : new List<Passenger>();

                List<Passenger> historyOnUnprocessed = historyKey.HasValue
                    ? incoming
                        .Where(p => !processedIds.Contains(p.Id) &&
                                    p.History != null &&
                                    p.History.ContainsKey(historyKey.Value))
                        .ToList()
                    : new List<Passenger>();

                int duplicateProcessed = processed
                    .GroupBy(p => p.Id)
                    .Where(g => g.Count() > 1)
                    .Sum(g => g.Count() - 1);

                int physical = service.PhysicalResources ?? queue?.PhysicalProcessorCount ?? -1;
                if (physical < 0)
                    physical = 0;

                int active;
                if (service.ActiveResources.HasValue)
                {
                    active = service.ActiveResources.Value;
                }
                else if (queue?.PoolResults != null && queue.PoolResults.Count > 0)
                {
                    active = queue.PoolResults.Sum(p => p.ActiveResources);
                }
                else
                {
                    active = queue?.Resources?.Count ?? 0;
                }

                int resourcesUsed = queue?.ResourcesUsed ??
                                    queue?.Resources?.Count ?? 0;

                int poolCount = queue?.ProcessorPools != null && queue.ProcessorPools.Count > 0
                    ? queue.ProcessorPools.Count
                    : queue?.PoolResults?.Count ?? 0;

                int poolPassengers = queue?.PoolResults != null
                    ? queue.PoolResults.Sum(p => p.PassengerCount)
                    : 0;

                int nonWarmUpProcessed = processed.Count(p => !p.IsWarmUp);
                int warmUpProcessed = processed.Count(p => p.IsWarmUp);

                List<string> plannedChannels = GetPlannedChannels(queue);
                List<Passenger> uncoveredExpected = kind == DiagnosticsServiceKind.BagDrop
                    ? new List<Passenger>()
                    : missingExpected
                        .Where(p => !IsChannelCovered(p.Channel, plannedChannels))
                        .ToList();

                // Check-In and Bag Drop are separate stages. Each row carries
                // its own channel and passenger-mix breakdowns. Bag Drop also
                // reports its visit history; it is no longer a Check-In subset.
                List<DiagnosticsBreakdownRow> channelBreakdown =
                    new List<DiagnosticsBreakdownRow>();
                List<DiagnosticsBreakdownRow> mixBreakdown =
                    new List<DiagnosticsBreakdownRow>();
                List<DiagnosticsBreakdownRow> departureBreakdown =
                    new List<DiagnosticsBreakdownRow>();
                List<DiagnosticsBreakdownRow> journeyBreakdown =
                    new List<DiagnosticsBreakdownRow>();
                List<DiagnosticsBreakdownRow> exceptionBreakdown =
                    new List<DiagnosticsBreakdownRow>();
                DiagnosticsBagDropBreakdown bagDrop = null;

                bool isPhysicalDepartureStage =
                    kind == DiagnosticsServiceKind.CheckIn ||
                    kind == DiagnosticsServiceKind.BagDrop;

                if (isPhysicalDepartureStage && canCheckRules)
                {
                    channelBreakdown = BuildBreakdown(
                        expectedPassengers, processedIds, DescribeChannel);
                    mixBreakdown = BuildBreakdown(
                        expectedPassengers, processedIds, DescribePassengerMix);
                    departureBreakdown = BuildBreakdown(
                        expectedPassengers, processedIds, DescribeDepartureType);
                    journeyBreakdown = BuildBreakdown(
                        expectedPassengers, processedIds, DescribeJourney);
                }

                if (kind == DiagnosticsServiceKind.CheckIn && canCheckRules)
                {
                    exceptionBreakdown = BuildExceptionBreakdown(incoming);
                }

                if (kind == DiagnosticsServiceKind.BagDrop && canCheckRules)
                {
                    bagDrop = BuildBagDropBreakdown(expectedPassengers, incoming);
                }

                List<string> notes = new List<string>();
                if (service.Notes != null)
                    notes.AddRange(service.Notes.Where(n => !string.IsNullOrWhiteSpace(n)));

                if (kind == DiagnosticsServiceKind.Unknown)
                {
                    notes.Add("Service type could not be inferred. Add a label such as " +
                              "Check-In, Security or Dwell, or pass a typed facility result.");
                }

                if (historyDerived)
                {
                    notes.Add("Processed population derived from passenger history because no " +
                              "Queue Results were supplied.");
                }

                if (kind == DiagnosticsServiceKind.TransferDesk ||
                    kind == DiagnosticsServiceKind.BaggageRecheck)
                {
                    notes.Add("Recorded by the Transfer Desk and Baggage Recheck components; " +
                              "wire them on the arrival side so the history-driven processed " +
                              "population matches the scenario-rule expectations.");
                }

                if (queue == null && !historyDerived)
                {
                    notes.Add("No Queue Results or history records are available for this stage.");
                }

                // The output population should preserve the stage population.
                if (service.Passengers != null && service.Passengers.Count > 0 &&
                    queue?.Passengers != null && queue.Passengers.Count > 0)
                {
                    HashSet<int> explicitIds = new HashSet<int>(incoming.Select(p => p.Id));
                    HashSet<int> outputIds = new HashSet<int>(
                        queue.Passengers.Where(p => p != null).Select(p => p.Id));

                    if (!explicitIds.SetEquals(outputIds))
                    {
                        AddFinding(
                            "SVC-010",
                            DiagnosticCategory.Service,
                            DiagnosticSeverity.Error,
                            "Stage population differs from the queue output population",
                            label + ": the explicit stage population and the result's full " +
                            "population do not contain the same passenger IDs.",
                            metric: "population",
                            expected: explicitIds.Count,
                            actual: outputIds.Count,
                            serviceLabel: label,
                            ids: explicitIds.Except(outputIds).Concat(outputIds.Except(explicitIds)));
                    }
                }

                if (queue?.Passengers != null && queue.Passengers.Count > 0 && processed.Count > 0)
                {
                    HashSet<int> outputIds = new HashSet<int>(
                        queue.Passengers.Where(p => p != null).Select(p => p.Id));

                    List<Passenger> orphaned = processed
                        .Where(p => !outputIds.Contains(p.Id))
                        .ToList();

                    if (orphaned.Count > 0)
                    {
                        AddFinding(
                            "SVC-009",
                            DiagnosticCategory.Service,
                            DiagnosticSeverity.Error,
                            "Processed passengers are absent from the output population",
                            label + ": " + orphaned.Count + " processed record(s) are not in the " +
                            "result's full passenger population.",
                            expected: 0,
                            actual: orphaned.Count,
                            serviceLabel: label,
                            ids: orphaned.Select(p => p.Id));
                    }
                }
                else if (queue != null &&
                         (queue.Passengers == null || queue.Passengers.Count == 0))
                {
                    notes.Add("The queue result has no full output population, so processed " +
                              "records cannot be cross-checked against an output set.");
                }

                // Verification is a sizing/throughput stage without QueueResults.
                DiagnosticStatus status = DiagnosticStatus.Pass;

                if (queue == null && processed.Count == 0)
                {
                    status = DiagnosticStatus.NotWired;
                }
                else
                {
                    if (canCheckRules &&
                        (missingExpected.Count > 0 ||
                         extraProcessed.Count > 0 ||
                         duplicateProcessed > 0))
                    {
                        status = DiagnosticStatus.Fail;
                    }

                    // Bag Drop cannot pass while an eligible record has no
                    // Bag Drop history. The generic missing-expected check
                    // normally catches this first; the breakdown keeps the
                    // reason explicit.
                    if (bagDrop != null && bagDrop.Missing > 0)
                    {
                        status = DiagnosticStatus.Fail;
                    }

                    if (status != DiagnosticStatus.Fail &&
                        (missingHistory.Count > 0 || historyOnUnprocessed.Count > 0))
                    {
                        status = DiagnosticStatus.Warn;
                    }

                    if (status == DiagnosticStatus.Pass &&
                        queue != null &&
                        processed.Count > 0 &&
                        active <= 0)
                    {
                        status = DiagnosticStatus.Warn;
                    }

                    if (status == DiagnosticStatus.Pass &&
                        poolCount > 0 &&
                        poolPassengers != nonWarmUpProcessed * StageMultiplier(queue))
                    {
                        status = DiagnosticStatus.Warn;
                    }

                    if (status == DiagnosticStatus.Pass &&
                        processed.Count == 0 &&
                        queue != null &&
                        ((queue.PeakQueueLength > 0) || (queue.AverageWait > 0)))
                    {
                        status = DiagnosticStatus.Warn;
                    }
                }

                if (missingExpected.Count > 0)
                {
                    notes.Add(missingExpected.Count + " expected record(s) were not processed (" +
                              DescribeChannels(missingExpected) + ").");
                }

                if (bagDrop != null && bagDrop.Active)
                {
                    notes.Add("Bag Drop: " + bagDrop.Expected +
                              " expected, " + bagDrop.Recorded + " recorded, " +
                              bagDrop.Missing + " missing. Counter passengers have their bag " +
                              "accepted at check-in; Web and Kiosk bag carriers use this stage.");
                }

                if (extraProcessed.Count > 0)
                {
                    notes.Add(extraProcessed.Count + " processed record(s) were not expected at this " +
                              "stage (" + DescribeChannels(extraProcessed) + ").");
                }

                if (alreadyServed > 0 && historyKey.HasValue)
                {
                    notes.Add(alreadyServed + " record(s) already carried the " +
                              historyKey.Value + " history and were treated as served by another " +
                              "stage or an earlier run, so they are not counted as expected here.");
                }

                if (missingHistory.Count > 0)
                {
                    notes.Add(missingHistory.Count + " processed record(s) lack the " +
                              historyKey?.ToString() + " history record.");
                }

                if (historyOnUnprocessed.Count > 0)
                {
                    notes.Add(historyOnUnprocessed.Count + " unprocessed record(s) already carry a " +
                              historyKey?.ToString() + " history record.");
                }

                if (poolCount > 0 && poolPassengers != nonWarmUpProcessed * StageMultiplier(queue))
                {
                    int stages = StageMultiplier(queue);

                    notes.Add("Pool passenger counts sum to " + poolPassengers +
                              " but the non-warm-up processed list has " + nonWarmUpProcessed +
                              (stages > 1
                                  ? " (x " + stages + " stacked stages in series mode)."
                                  : "."));
                }

                DiagnosticsServiceRow row = new DiagnosticsServiceRow
                {
                    Label = label,
                    Kind = kind,
                    Incoming = incoming.Count,
                    Applicable = expected + bypassed,
                    Expected = expected,
                    Processed = processed.Count,
                    WarmUpProcessed = warmUpProcessed,
                    NonWarmUpProcessed = nonWarmUpProcessed,
                    Bypassed = bypassed,
                    NotApplicable = notApplicable,
                    MissingExpected = missingExpected.Count,
                    ExtraProcessed = extraProcessed.Count,
                    MissingHistory = missingHistory.Count,
                    HistoryOnUnprocessed = historyOnUnprocessed.Count,
                    DuplicateProcessed = duplicateProcessed,
                    PhysicalResources = physical,
                    ActiveResources = active,
                    ResourcesUsed = resourcesUsed,
                    PoolCount = poolCount,
                    PoolPassengers = poolPassengers,
                    PeakQueue = queue?.PeakQueueLength ?? 0,
                    AverageWait = queue?.AverageWait ?? 0,
                    MaxWait = queue?.MaxWait ?? 0,
                    Wait95 = queue?.Wait95 ?? 0,
                    Status = status,
                    Notes = notes,
                    MissingPassengerIds = CapIds(missingExpected.Select(p => p.Id)),
                    ExtraPassengerIds = CapIds(extraProcessed.Select(p => p.Id)),
                    Channels = channelBreakdown,
                    PassengerMix = mixBreakdown,
                    DepartureTypes = departureBreakdown,
                    Journeys = journeyBreakdown,
                    Exceptions = exceptionBreakdown,
                    BagDrop = bagDrop
                };

                _report.Services.Add(row);

                if (queue != null || processed.Count > 0)
                    _report.ServiceResultCount++;

                if (canCheckRules && missingExpected.Count > 0)
                {
                    AddFinding(
                        "SVC-001",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Missing expected service",
                        label + ": " + missingExpected.Count + " of " + expected +
                        " expected record(s) were not processed.",
                        metric: "expected served",
                        expected: expected,
                        actual: Math.Max(0, expected - missingExpected.Count),
                        serviceLabel: label,
                        ids: missingExpected.Select(p => p.Id),
                        details: missingExpected
                            .Take(DetailLimit)
                            .Select(p => DescribePassenger(p) + " - " +
                                         ServiceEligibility.Evaluate(p, kind, processedIds).Note));

                    AddPassengerIssues(
                        missingExpected,
                        "PAX-SVC-001",
                        DiagnosticSeverity.Error,
                        label + " expected but not processed.");
                }

                if (canCheckRules && extraProcessed.Count > 0)
                {
                    AddFinding(
                        "SVC-002",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Unexpected processed passengers",
                        label + ": " + extraProcessed.Count + " processed record(s) were not " +
                        "expected at this stage.",
                        metric: "processed",
                        expected: expected,
                        actual: processed.Count,
                        serviceLabel: label,
                        ids: extraProcessed.Select(p => p.Id),
                        details: extraProcessed
                            .Take(DetailLimit)
                            .Select(p => DescribePassenger(p) + " - " +
                                         ServiceEligibility.Evaluate(p, kind, processedIds).Note));

                    AddPassengerIssues(
                        extraProcessed,
                        "PAX-SVC-002",
                        DiagnosticSeverity.Error,
                        label + " processed although the rules did not expect it.");
                }

                if (bagDrop != null && bagDrop.Missing > 0)
                {
                    HashSet<int> missingBagDropIds =
                        new HashSet<int>(bagDrop.MissingPassengerIds);
                    List<Passenger> missingBagDropProcessed = expectedPassengers
                        .Where(p => missingBagDropIds.Contains(p.Id) &&
                                    processedIds.Contains(p.Id))
                        .ToList();

                    if (missingBagDropProcessed.Count > 0)
                    {
                        AddFinding(
                            "SVC-014",
                            DiagnosticCategory.Service,
                            DiagnosticSeverity.Error,
                            "Bag Drop processed a record without Bag Drop history",
                            label + ": " + missingBagDropProcessed.Count + " processed record(s) " +
                            "require the Bag Drop stage but carry no Bag Drop history.",
                            metric: "bag-drop history",
                            expected: bagDrop.Expected,
                            actual: bagDrop.Recorded,
                            serviceLabel: label,
                            ids: missingBagDropProcessed.Select(p => p.Id),
                            details: DescribeBreakdown(bagDrop.MissingMix));

                        AddPassengerIssues(
                            missingBagDropProcessed,
                            "PAX-SVC-014",
                            DiagnosticSeverity.Error,
                            label + " processed the record but wrote no Bag Drop history.");
                    }
                }

                if (duplicateProcessed > 0)
                {
                    AddFinding(
                        "SVC-003",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Duplicate processed passengers",
                        label + ": " + duplicateProcessed + " duplicate record(s) are in the " +
                        "processed list.",
                        metric: "processed",
                        expected: processed.Select(p => p.Id).Distinct().Count(),
                        actual: processed.Count,
                        serviceLabel: label);
                }

                if (missingHistory.Count > 0)
                {
                    AddFinding(
                        "SVC-004",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Processed passengers lack a facility history record",
                        label + ": " + missingHistory.Count + " processed record(s) have no " +
                        historyKey?.ToString() + " history. The visit cannot be audited on the " +
                        "passenger.",
                        metric: "history records",
                        expected: processed.Count,
                        actual: processed.Count - missingHistory.Count,
                        serviceLabel: label,
                        ids: missingHistory.Select(p => p.Id));
                }

                if (historyOnUnprocessed.Count > 0)
                {
                    AddFinding(
                        "SVC-005",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Facility history exists on unprocessed passengers",
                        label + ": " + historyOnUnprocessed.Count + " record(s) in the stage " +
                        "population already carry a " + historyKey?.ToString() +
                        " history but were not processed in this result. History may be from an " +
                        "earlier run or from a facility that owns this service internally.",
                        metric: "history records",
                        expected: 0,
                        actual: historyOnUnprocessed.Count,
                        serviceLabel: label,
                        ids: historyOnUnprocessed.Select(p => p.Id));
                }

                // A physical Check-In stage must never serve the virtual Web
                // channel. Online check-in without a bag has no counter visit,
                // so those records inflate processor demand without needing it.
                if (kind == DiagnosticsServiceKind.CheckIn)
                {
                    List<Passenger> webProcessed = processed
                        .Where(p => string.Equals(
                            (p.Channel ?? string.Empty).Trim(),
                            DefaultChannelAssigner.WebChannel,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (webProcessed.Count > 0)
                    {
                        notes.Add(webProcessed.Count + " processed record(s) are on the virtual " +
                                  "Web channel and need no physical check-in visit.");

                        AddFinding(
                            "SVC-012",
                            DiagnosticCategory.Service,
                            DiagnosticSeverity.Warning,
                            "Virtual Web channel served at Check-In",
                            label + ": " + webProcessed.Count + " of " + processed.Count +
                            " processed record(s) carry the virtual Web channel. Online check-in " +
                            "without a bag has no counter visit. Remove Web from the Check-In " +
                            "channels or the pool plans.",
                            metric: "processed",
                            expected: 0,
                            actual: webProcessed.Count,
                            serviceLabel: label,
                            ids: webProcessed.Select(p => p.Id));
                    }
                }

                if (canCheckRules && uncoveredExpected.Count > 0)
                {
                    List<string> perChannel = DescribeChannelCounts(uncoveredExpected);

                    notes.Add(uncoveredExpected.Count + " expected record(s) are on channels that " +
                              "no result pool covers (" + string.Join(", ", perChannel) + ").");

                    AddFinding(
                        "SVC-011",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Expected passengers fall outside every planned pool",
                        label + ": " + uncoveredExpected.Count + " of " + expected +
                        " expected record(s) carry a channel that no pool in this result serves. " +
                        "The facility's Channels or Pool Plans must cover the channels listed in " +
                        "the details.",
                        metric: "expected served",
                        expected: expected,
                        actual: expected - uncoveredExpected.Count,
                        serviceLabel: label,
                        ids: uncoveredExpected.Select(p => p.Id),
                        details: perChannel);
                }

                if (status == DiagnosticStatus.Warn &&
                    queue != null &&
                    processed.Count > 0 &&
                    active <= 0)
                {
                    AddFinding(
                        "SVC-006",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Service has no active processors",
                        label + ": passengers were processed but the result reports no staffed " +
                        "processor.",
                        metric: "active processors",
                        expected: 1,
                        actual: active,
                        serviceLabel: label);
                }

                if (poolCount > 0 && poolPassengers != nonWarmUpProcessed * StageMultiplier(queue))
                {
                    int stages = StageMultiplier(queue);

                    AddFinding(
                        "SVC-007",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Pool passenger counts do not match the processed list",
                        stages > 1
                            ? label + ": the pooled results carry " + stages + " stacked stages, but their " +
                              "passenger counts still exclude warm-up, so the totals differ from the " +
                              "non-warm-up processed records."
                            : label + ": pool passenger counts exclude warm-up, but the total differs " +
                              "from the non-warm-up processed records.",
                        metric: "passengers",
                        expected: nonWarmUpProcessed * stages,
                        actual: poolPassengers,
                        serviceLabel: label);
                }

                if (queue != null &&
                    processed.Count == 0 &&
                    ((queue.PeakQueueLength > 0) || (queue.AverageWait > 0)))
                {
                    AddFinding(
                        "SVC-008",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Queue metrics without processed passengers",
                        label + ": the result reports a queue or waiting time but no processed " +
                        "passengers. The counts come from different populations.",
                        metric: "processed",
                        expected: 0,
                        actual: processed.Count,
                        serviceLabel: label,
                        details: new List<string>
                        {
                            "Peak queue: " + queue.PeakQueueLength.ToString(CultureInfo.InvariantCulture),
                            "Average wait: " + queue.AverageWait.ToString("0.###", CultureInfo.InvariantCulture)
                        });
                }
            }

            /// <summary>
            /// Verification has no per-passenger queue. Its result states how
            /// many passengers were eligible and how many gates were sized, so
            /// those two numbers are reconciled instead of a processed list.
            /// </summary>
            private void AuditVerification(
                DiagnosticsServiceInput service,
                string label,
                List<Passenger> incoming)
            {
                int eligible = service.EligiblePassengers ??
                               incoming.Count(p =>
                                   ServiceEligibility.Evaluate(p, DiagnosticsServiceKind.Verification)
                                       .IsEligible);

                int gates = service.RequiredGates ?? 0;
                double throughput = service.GateCapacity ?? 0;
                double utilization = service.Utilization ?? 0;

                DiagnosticStatus status = DiagnosticStatus.Pass;
                List<string> notes = new List<string>();

                if (eligible > 0 && gates <= 0)
                {
                    status = DiagnosticStatus.Fail;
                    notes.Add("Eligible passengers are non-zero but no gates were sized.");

                    AddFinding(
                        "SVC-021",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Verification has demand but no gates",
                        label + ": " + eligible + " eligible passenger(s) but " + gates +
                        " gate(s).",
                        metric: "gates",
                        expected: 1,
                        actual: gates,
                        serviceLabel: label);
                }

                if (gates > 0 && throughput <= 0)
                {
                    status = DiagnosticStatus.Fail;
                    notes.Add("Gates were sized but the per-gate throughput is zero.");

                    AddFinding(
                        "SVC-020",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Error,
                        "Verification gates have no throughput",
                        label + ": gates were sized but the per-gate throughput is zero.",
                        metric: "throughput",
                        expected: 1,
                        actual: 0,
                        serviceLabel: label);
                }

                if (service.Utilization.HasValue &&
                    (utilization <= 0 || utilization > 1.0))
                {
                    if (status == DiagnosticStatus.Pass)
                        status = DiagnosticStatus.Warn;

                    notes.Add("Design utilization is outside the valid 0-100% range.");

                    AddFinding(
                        "SVC-022",
                        DiagnosticCategory.Service,
                        DiagnosticSeverity.Warning,
                        "Verification utilization is outside 0-100%",
                        label + ": design utilization is outside the valid range.",
                        metric: "utilization",
                        expected: 1,
                        actual: utilization,
                        serviceLabel: label);
                }

                if (service.Notes != null)
                    notes.AddRange(service.Notes.Where(n => !string.IsNullOrWhiteSpace(n)));

                _report.Services.Add(new DiagnosticsServiceRow
                {
                    Label = label,
                    Kind = DiagnosticsServiceKind.Verification,
                    Incoming = incoming.Count,
                    Applicable = eligible,
                    Expected = eligible,
                    Processed = eligible,
                    NonWarmUpProcessed = eligible,
                    PhysicalResources = gates,
                    ActiveResources = gates,
                    PoolCount = gates > 0 ? 1 : 0,
                    Status = status,
                    Notes = notes
                });

                _report.ServiceResultCount++;
            }

            private string MakeUniqueLabel(string label)
            {
                if (!_serviceLabels.TryGetValue(label, out int count))
                {
                    _serviceLabels[label] = 1;
                    return label;
                }

                count++;
                _serviceLabels[label] = count;
                return label + " #" + count.ToString(CultureInfo.InvariantCulture);
            }

            // ---------------------------------------------------------
            // Transfers
            // ---------------------------------------------------------

            private void CheckTransfers()
            {
                _demand = _input.TransferDemand ?? _input.Generation?.TransferDemand;

                List<Passenger> transfers = _population
                    .Where(p => p.Journey == PassengerJourney.Transfer)
                    .ToList();

                List<Passenger> outbound = transfers.Where(IsDeparture).ToList();
                List<Passenger> inbound = transfers.Where(IsArrival).ToList();

                if (_demand == null)
                {
                    if (transfers.Count > 0)
                    {
                        AddFinding(
                            "TRF-001",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Info,
                            "No transfer demand result",
                            transfers.Count + " transfer record(s) exist but no Transfer Demand " +
                            "result was wired. Demand cannot be reconciled; only tagging and " +
                            "service history are reported.");
                    }

                    AddTransferRowsWithoutDemand(transfers);
                }
                else
                {
                    AddTransferDemandRows();
                }

                AddTransferServiceRows(transfers);
                AddTransferPeakRows();

                int unspecifiedRules = outbound.Count(p => p.TransferProcessing == null);
                if (unspecifiedRules > 0)
                {
                    AddFinding(
                        "TRF-015",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Info,
                        "Tagged transfers without explicit processing rules",
                        unspecifiedRules + " outbound transfer record(s) keep normal passenger " +
                        "behaviour because their scenario has no processing rules. Their service " +
                        "use is reported as unspecified, not as a rule bypass.",
                        expected: outbound.Count,
                        actual: outbound.Count - unspecifiedRules,
                        ids: outbound.Where(p => p.TransferProcessing == null).Select(p => p.Id));
                }

                if (_demand != null &&
                    _demand.Warnings != null &&
                    _demand.Warnings.Count > 0)
                {
                    AddFinding(
                        "TRF-016",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Info,
                        "Transfer demand warnings",
                        _demand.Warnings.Count + " warning(s) were reported while calculating or " +
                        "tagging transfer demand.",
                        expected: 0,
                        actual: _demand.Warnings.Count,
                        details: Cap(_demand.Warnings));
                }

                bool scheduleBased = _demand != null &&
                                     (_demand.Capacity != null ||
                                      (_demand.ScheduleAssignments != null &&
                                       _demand.ScheduleAssignments.Count > 0));

                if (scheduleBased && outbound.Count > 0 && inbound.Count > 0 &&
                    outbound.Count != inbound.Count)
                {
                    AddFinding(
                        "TRF-014",
                        DiagnosticCategory.Transfer,
                        DiagnosticSeverity.Warning,
                        "Transfer arrival and departure leg counts differ",
                        "A schedule-driven transfer journey produces one arrival record and one " +
                        "departure record. The population does not contain equal numbers.",
                        expected: outbound.Count,
                        actual: inbound.Count,
                        details: new List<string>
                        {
                            "Outbound departure records: " + outbound.Count,
                            "Inbound arrival records: " + inbound.Count
                        });
                }
            }

            private void AddTransferRowsWithoutDemand(List<Passenger> transfers)
            {
                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    int departures = transfers.Count(p =>
                        IsDeparture(p) && p.TransferScenario == scenario);
                    int arrivals = transfers.Count(p =>
                        IsArrival(p) && p.TransferScenario == scenario);

                    if (departures == 0 && arrivals == 0)
                        continue;

                    _report.Transfers.Add(new DiagnosticsTransferRow
                    {
                        Scenario = scenario.ToFlowLabel(),
                        Requested = 0,
                        FeasibleMaximum = 0,
                        Effective = 0,
                        Tagged = departures,
                        TaggedPopulation = departures,
                        ArrivalLegs = arrivals,
                        DepartureLegs = departures,
                        Shortfall = 0,
                        Excess = 0,
                        Status = DiagnosticStatus.NotWired,
                        Notes = new List<string>
                        {
                            "No Transfer Demand result. Tagged counts are computed from the " +
                            "population only."
                        }
                    });
                }
            }

            private void AddTransferDemandRows()
            {
                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    TransferScenarioDemand demandEntry;
                    _demand.ByScenario.TryGetValue(scenario, out demandEntry);

                    int requested = demandEntry?.RequestedPassengers ?? 0;
                    int feasible = demandEntry?.FeasibleMaximum ?? 0;
                    int effective = demandEntry?.EffectivePassengers ?? 0;
                    int demandTagged = demandEntry?.TaggedPassengers ?? 0;

                    int popDeparture = _population.Count(p =>
                        IsDeparture(p) && p.Journey == PassengerJourney.Transfer &&
                        p.TransferScenario == scenario);
                    int popArrival = _population.Count(p =>
                        IsArrival(p) && p.Journey == PassengerJourney.Transfer &&
                        p.TransferScenario == scenario);

                    int tagged = demandTagged;
                    List<string> notes = new List<string>();

                    if (demandTagged == 0 && popDeparture > 0)
                    {
                        tagged = popDeparture;
                        notes.Add("Tagged count was zero on the demand result; the population " +
                                  "count is used.");
                    }

                    int shortfall = Math.Max(0, effective - tagged);
                    int excess = Math.Max(0, tagged - effective);

                    DiagnosticStatus status = DiagnosticStatus.Pass;

                    if (demandTagged > 0 && demandTagged != popDeparture)
                        status = DiagnosticStatus.Fail;
                    else if (shortfall > 0 || excess > 0)
                        status = DiagnosticStatus.Warn;
                    else if (effective < requested)
                        status = DiagnosticStatus.Info;

                    _report.Transfers.Add(new DiagnosticsTransferRow
                    {
                        Scenario = scenario.ToFlowLabel(),
                        Requested = requested,
                        FeasibleMaximum = feasible,
                        Effective = effective,
                        Tagged = tagged,
                        TaggedPopulation = popDeparture,
                        ArrivalLegs = popArrival,
                        DepartureLegs = popDeparture,
                        Shortfall = shortfall,
                        Excess = excess,
                        Status = status,
                        Notes = notes
                    });

                    if (demandTagged > 0 && demandTagged != popDeparture)
                    {
                        AddFinding(
                            "TRF-012",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Error,
                            "Tagged transfer count differs from the population",
                            scenario.ToFlowLabel() + ": the demand result records " + demandTagged +
                            " tagged outbound passengers, but the population contains " +
                            popDeparture + ".",
                            metric: "tagged outbound",
                            expected: popDeparture,
                            actual: demandTagged,
                            scenario: scenario.ToFlowLabel());
                    }

                    if (shortfall > 0)
                    {
                        AddFinding(
                            "TRF-010",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Warning,
                            "Transfer demand was not fully tagged",
                            scenario.ToFlowLabel() + ": " + tagged + " of " + effective +
                            " effective outbound transfer passengers were tagged. This is usually " +
                            "a missing Passenger Group/profile, not a facility capacity issue.",
                            metric: "tagged outbound",
                            expected: effective,
                            actual: tagged,
                            scenario: scenario.ToFlowLabel(),
                            ids: _population
                                .Where(p => IsDeparture(p) &&
                                            p.Journey == PassengerJourney.Transfer &&
                                            p.TransferScenario == scenario)
                                .Select(p => p.Id));
                    }

                    if (excess > 0)
                    {
                        AddFinding(
                            "TRF-011",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Warning,
                            "More transfer passengers tagged than effective demand",
                            scenario.ToFlowLabel() + ": " + tagged + " tagged against " +
                            effective + " effective passengers.",
                            metric: "tagged outbound",
                            expected: effective,
                            actual: tagged,
                            scenario: scenario.ToFlowLabel());
                    }

                    if (effective < requested)
                    {
                        AddFinding(
                            "TRF-013",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Info,
                            "Transfer demand capped by the schedule",
                            scenario.ToFlowLabel() + ": " + requested + " requested but " +
                            effective + " effective after schedule feasibility.",
                            metric: "effective transfer demand",
                            expected: requested,
                            actual: effective,
                            scenario: scenario.ToFlowLabel());
                    }
                }
            }

            private void AddTransferServiceRows(List<Passenger> transfers)
            {
                foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
                {
                    DiagnosticsServiceKind kind = ToServiceKind(service);
                    FacilityType? historyKey = kind.ToFacilityType();

                    List<Passenger> applicable = transfers
                        .Where(p => TransferServiceUsageCalculator.AppliesTo(service, p))
                        .ToList();

                    List<Passenger> required = applicable
                        .Where(p => TransferServiceUsageCalculator.IsRequired(p, service))
                        .ToList();

                    // The Check-In facility skips passengers who reach it after
                    // the flight's last-admission time, so a rule-required
                    // check-in or bag drop cannot be recorded for them. The
                    // audit applies the same exclusion and names the records
                    // instead of failing them.
                    bool lateExcluded = kind == DiagnosticsServiceKind.CheckIn ||
                                        kind == DiagnosticsServiceKind.BagDrop;
                    List<Passenger> requiredLate = lateExcluded
                        ? required.Where(p => p.IsLateForCheckIn).ToList()
                        : new List<Passenger>();

                    List<Passenger> bypassed = applicable
                        .Where(p => TransferServiceUsageCalculator.IsBypassed(p, service))
                        .ToList();

                    List<Passenger> unspecified = applicable
                        .Where(p => TransferServiceUsageCalculator.IsUnspecified(p, service))
                        .ToList();

                    // History may live on a stage output clone used by Check-In,
                    // not on the wired population record, so read every source
                    // and match by passenger Id.
                    HashSet<int> historyIds = historyKey.HasValue
                        ? new HashSet<int>(
                            CollectHistoryBacked(historyKey.Value, transfers)
                                .Select(p => p.Id))
                        : new HashSet<int>();

                    List<Passenger> served = applicable
                        .Where(p => historyIds.Contains(p.Id))
                        .ToList();

                    List<Passenger> missingRequired = required
                        .Where(p => !historyIds.Contains(p.Id) &&
                                    (!lateExcluded || !p.IsLateForCheckIn))
                        .ToList();

                    List<Passenger> servedBypassed = bypassed
                        .Where(p => historyIds.Contains(p.Id))
                        .ToList();

                    List<Passenger> processedUnspecified = unspecified
                        .Where(p => historyIds.Contains(p.Id))
                        .ToList();

                    bool wired = _report.Services.Any(s => s.Kind == kind) ||
                                 served.Count > 0 ||
                                 (kind == DiagnosticsServiceKind.BagDrop &&
                                  ((_input.Services ?? new List<DiagnosticsServiceInput>())
                                       .Any(s =>
                                           s != null &&
                                           (s.Kind == DiagnosticsServiceKind.CheckIn ||
                                            s.Kind == DiagnosticsServiceKind.BagDrop)) ||
                                   _report.Services.Any(s =>
                                       s.Kind == DiagnosticsServiceKind.CheckIn)));

                    // The demand result records the same rule counts it tagged.
                    // If they no longer match the population, the demand result
                    // is stale (or a different population was wired).
                    TransferServiceUsage recorded = _demand?.ServiceUsage?[service];

                    bool usageMismatch = recorded != null &&
                                         recorded.Applicable > 0 &&
                                         (recorded.Required != required.Count ||
                                          recorded.Bypassed != bypassed.Count ||
                                          recorded.Unspecified != unspecified.Count);

                    DiagnosticStatus status;
                    if (!historyKey.HasValue)
                        status = DiagnosticStatus.NotWired;
                    else if (!wired)
                        status = DiagnosticStatus.NotWired;
                    else if (missingRequired.Count > 0 || servedBypassed.Count > 0)
                        status = DiagnosticStatus.Fail;
                    else if (usageMismatch)
                        status = DiagnosticStatus.Warn;
                    else
                        status = DiagnosticStatus.Pass;

                    List<string> notes = new List<string>();
                    if (!historyKey.HasValue)
                    {
                        notes.Add("No facility type models this service; it cannot be audited " +
                                  "from passenger history.");
                    }
                    else if (!wired)
                    {
                        notes.Add("No facility result or history record was found for this service.");
                    }

                    if (!historyKey.HasValue)
                        notes.Add("Required transfer records cannot be checked for service history.");

                    if (requiredLate.Count > 0)
                    {
                        notes.Add(requiredLate.Count + " required transfer(s) reached check-in " +
                                  "after its last-admission time and were not served; excluded " +
                                  "with the Check-In stage (passenger " +
                                  string.Join(" ", CapIds(requiredLate.Select(p => p.Id))) + ").");

                        AddPassengerIssues(
                            requiredLate,
                            "PAX-TRF-002",
                            DiagnosticSeverity.Info,
                            "Transfer rules require " + service.ToLabel() +
                            " but the record reached check-in after its last-admission time " +
                            "and was not served.");
                    }

                    if (usageMismatch)
                    {
                        notes.Add("The demand result records " + recorded.Required +
                                  " required, " + recorded.Bypassed + " bypassed and " +
                                  recorded.Unspecified + " without rules; the population has " +
                                  required.Count + ", " + bypassed.Count + " and " +
                                  unspecified.Count + ".");

                        AddFinding(
                            "TRF-SVC-010",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Warning,
                            "Transfer service usage is stale",
                            service.ToLabel() + ": the demand result and the wired population " +
                            "disagree about the rule counts. Re-run transfer tagging or wire the " +
                            "matching population.",
                            metric: service.ToLabel(),
                            expected: recorded.Applicable,
                            actual: applicable.Count,
                            serviceLabel: service.ToLabel());
                    }

                    _report.TransferServices.Add(new DiagnosticsTransferServiceRow
                    {
                        Service = service.ToLabel(),
                        Kind = kind,
                        Applicable = applicable.Count,
                        Required = required.Count,
                        Bypassed = bypassed.Count,
                        Unspecified = unspecified.Count,
                        Processed = served.Count,
                        MissingRequired = missingRequired.Count,
                        ServedBypassed = servedBypassed.Count,
                        ProcessedUnspecified = processedUnspecified.Count,
                        ResultWired = wired,
                        Status = status,
                        Notes = notes
                    });

                    if (historyKey.HasValue && wired && missingRequired.Count > 0)
                    {
                        AddFinding(
                            "TRF-SVC-001",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Error,
                            "Required transfer service was not recorded",
                            service.ToLabel() + ": " + missingRequired.Count + " of " +
                            required.Count + " required transfer record(s) have no facility " +
                            "history.",
                            metric: service.ToLabel(),
                            expected: required.Count,
                            actual: required.Count - missingRequired.Count,
                            serviceLabel: service.ToLabel(),
                            ids: missingRequired.Select(p => p.Id));

                        AddPassengerIssues(
                            missingRequired,
                            "PAX-TRF-001",
                            DiagnosticSeverity.Error,
                            "Transfer rules require " + service.ToLabel() +
                            " but the record has no service history.");
                    }

                    if (historyKey.HasValue && wired && servedBypassed.Count > 0)
                    {
                        AddFinding(
                            "TRF-SVC-002",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Error,
                            "Bypassed transfer service was still served",
                            service.ToLabel() + ": " + servedBypassed.Count + " transfer " +
                            "record(s) carry service history although the scenario rule bypasses " +
                            "the service.",
                            metric: service.ToLabel(),
                            expected: 0,
                            actual: servedBypassed.Count,
                            serviceLabel: service.ToLabel(),
                            ids: servedBypassed.Select(p => p.Id));

                        AddPassengerIssues(
                            servedBypassed,
                            "PAX-TRF-002",
                            DiagnosticSeverity.Error,
                            "Transfer rules bypass " + service.ToLabel() +
                            " but the record carries a service history.");
                    }

                    if (historyKey.HasValue && !wired && required.Count > 0)
                    {
                        AddFinding(
                            "TRF-SVC-003",
                            DiagnosticCategory.Transfer,
                            DiagnosticSeverity.Info,
                            "Transfer service has demand but no result",
                            service.ToLabel() + ": " + required.Count + " required transfer " +
                            "record(s) exist, but no facility result or history record was wired.",
                            metric: service.ToLabel(),
                            expected: required.Count,
                            actual: 0,
                            serviceLabel: service.ToLabel());
                    }
                }
            }

            private void AddTransferPeakRows()
            {
                if (_population.Count == 0)
                    return;

                List<Passenger> transfers = _population
                    .Where(p => p.Journey == PassengerJourney.Transfer)
                    .ToList();

                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
                    {
                        bool departureService =
                            TransferServiceUsageCalculator.IsDepartureService(service);

                        List<Passenger> directionRecords = transfers
                            .Where(p => p.TransferScenario == scenario &&
                                        (departureService ? IsDeparture(p) : IsArrival(p)))
                            .Where(p => TransferServiceUsageCalculator.AppliesTo(service, p))
                            .ToList();

                        if (directionRecords.Count == 0)
                            continue;

                        PeakWindow window = GetPeakWindow(scenario, departureService);
                        List<Passenger> inWindow = directionRecords
                            .Where(window.Contains)
                            .ToList();

                        if (inWindow.Count == 0)
                            continue;

                        int required = 0;
                        int bypassed = 0;
                        int unspecified = 0;
                        int processed = 0;
                        List<Passenger> missing = new List<Passenger>();
                        List<Passenger> servedBypassed = new List<Passenger>();

                        DiagnosticsServiceKind kind = ToServiceKind(service);
                        FacilityType? historyKey = kind.ToFacilityType();

                        foreach (Passenger passenger in inWindow)
                        {
                            bool served = historyKey.HasValue &&
                                          HasHistory(passenger, historyKey.Value);

                            if (served)
                                processed++;

                            if (TransferServiceUsageCalculator.IsUnspecified(passenger, service))
                            {
                                unspecified++;
                                continue;
                            }

                            if (TransferServiceUsageCalculator.IsBypassed(passenger, service))
                            {
                                bypassed++;
                                if (served)
                                    servedBypassed.Add(passenger);
                                continue;
                            }

                            if (TransferServiceUsageCalculator.IsRequired(passenger, service))
                            {
                                required++;
                                if (!served)
                                    missing.Add(passenger);
                            }
                        }

                        int peak = required + bypassed + unspecified;
                        if (peak == 0)
                            continue;

                        DiagnosticStatus status;
                        if (!historyKey.HasValue || !IsTransferServiceWired(kind))
                            status = DiagnosticStatus.NotWired;
                        else if (missing.Count > 0 || servedBypassed.Count > 0)
                            status = DiagnosticStatus.Fail;
                        else
                            status = DiagnosticStatus.Pass;

                        _report.Peaks.Add(new DiagnosticsPeakRow
                        {
                            Flow = scenario.ToFlowLabel(),
                            Service = service.ToLabel(),
                            Kind = kind,
                            StartMinute = window.StartMinute,
                            Window = window.Label,
                            Peak = peak,
                            Required = required,
                            Bypassed = bypassed,
                            Unspecified = unspecified,
                            Processed = processed,
                            MissingRequired = missing.Count,
                            Status = status,
                            Notes = new List<string>()
                        });

                        if (historyKey.HasValue && missing.Count > 0)
                        {
                            AddFinding(
                                "TRF-PEAK-001",
                                DiagnosticCategory.Transfer,
                                DiagnosticSeverity.Error,
                                "Required transfer service is short at the peak",
                                scenario.ToFlowLabel() + " / " + service.ToLabel() + " at " +
                                window.Label + ": " + missing.Count + " of " + required +
                                " required transfer record(s) have no service history.",
                                metric: service.ToLabel() + " peak",
                                expected: required,
                                actual: required - missing.Count,
                                serviceLabel: service.ToLabel(),
                                scenario: scenario.ToFlowLabel(),
                                ids: missing.Select(p => p.Id),
                                details: new List<string>
                                {
                                    "Window starts at " +
                                    SchedulePassengerGenerationResult.FormatMinute(window.StartMinute)
                                });
                        }

                        if (historyKey.HasValue && servedBypassed.Count > 0)
                        {
                            AddFinding(
                                "TRF-PEAK-002",
                                DiagnosticCategory.Transfer,
                                DiagnosticSeverity.Error,
                                "Bypassed service is served at the transfer peak",
                                scenario.ToFlowLabel() + " / " + service.ToLabel() + " at " +
                                window.Label + ": " + servedBypassed.Count +
                                " record(s) carry history although the rule bypasses the service.",
                                metric: service.ToLabel() + " peak",
                                expected: 0,
                                actual: servedBypassed.Count,
                                serviceLabel: service.ToLabel(),
                                scenario: scenario.ToFlowLabel(),
                                ids: servedBypassed.Select(p => p.Id));
                        }
                    }
                }
            }

            private bool IsTransferServiceWired(DiagnosticsServiceKind kind)
            {
                return _report.Services.Any(s => s.Kind == kind) ||
                       _report.TransferServices.Any(s => s.Kind == kind && s.Processed > 0);
            }

            private PeakWindow GetPeakWindow(TransferScenario scenario, bool departureService)
            {
                if (_demand == null || IsAggregateDemand(_demand))
                    return PeakWindow.All("all records (design cohort)");

                int start;
                int count;

                if (departureService)
                {
                    bool domestic = scenario == TransferScenario.DomesticToDomestic ||
                                    scenario == TransferScenario.InternationalToDomestic;

                    start = domestic
                        ? _demand.DomesticDepartureTransferPeakAt
                        : _demand.InternationalDepartureTransferPeakAt;
                    count = domestic
                        ? _demand.DomesticDepartureTransferPeak
                        : _demand.InternationalDepartureTransferPeak;
                }
                else
                {
                    bool domestic = scenario == TransferScenario.DomesticToDomestic ||
                                    scenario == TransferScenario.DomesticToInternational;

                    start = domestic
                        ? _demand.DomesticArrivalTransferPeakAt
                        : _demand.InternationalArrivalTransferPeakAt;
                    count = domestic
                        ? _demand.DomesticArrivalTransferPeak
                        : _demand.InternationalArrivalTransferPeak;
                }

                if (count <= 0)
                    return PeakWindow.All("all records (no peak window)");

                return new PeakWindow(start, count);
            }

            private static bool IsAggregateDemand(TransferDemandResult demand)
            {
                return demand.Capacity == null &&
                       (demand.ScheduleAssignments == null ||
                        demand.ScheduleAssignments.Count == 0);
            }

            private static DiagnosticsServiceKind ToServiceKind(TransferService service)
            {
                switch (service)
                {
                    case TransferService.TransferDesk:
                        return DiagnosticsServiceKind.TransferDesk;
                    case TransferService.CheckIn:
                        return DiagnosticsServiceKind.CheckIn;
                    case TransferService.BagDrop:
                        return DiagnosticsServiceKind.BagDrop;
                    case TransferService.Security:
                        return DiagnosticsServiceKind.Security;
                    case TransferService.Immigration:
                        return DiagnosticsServiceKind.Immigration;
                    case TransferService.Emigration:
                        return DiagnosticsServiceKind.Emigration;
                    case TransferService.BaggageRecheck:
                        return DiagnosticsServiceKind.BaggageRecheck;
                    default:
                        return DiagnosticsServiceKind.Unknown;
                }
            }

            // ---------------------------------------------------------
            // Gates
            // ---------------------------------------------------------

            private void CheckGates()
            {
                List<GateSizingResult> results = _input.GateResults ?? new List<GateSizingResult>();

                foreach (GateSizingResult result in results)
                {
                    if (result == null)
                        continue;

                    if (result.Warnings != null && result.Warnings.Count > 0)
                    {
                        AddFinding(
                            "GATE-001",
                            DiagnosticCategory.Gate,
                            DiagnosticSeverity.Warning,
                            "Gate sizing warnings",
                            result.Warnings.Count + " gate sizing warning(s) were reported.",
                            expected: 0,
                            actual: result.Warnings.Count,
                            details: Cap(result.Warnings));
                    }

                    int bucketActive = (result.Buckets ?? new List<GateBucketStands>())
                        .Sum(b => b?.Active ?? 0);
                    int bucketContact = (result.Buckets ?? new List<GateBucketStands>())
                        .Sum(b => b?.Contact ?? 0);
                    int bucketBus = (result.Buckets ?? new List<GateBucketStands>())
                        .Sum(b => b?.Bus ?? 0);
                    int bucketRemote = (result.Buckets ?? new List<GateBucketStands>())
                        .Sum(b => b?.Remote ?? 0);

                    AddGateRow(
                        "Active stands (total vs bucket sum)",
                        result.ActiveTotal,
                        bucketActive,
                        result.ActiveTotal > result.MaxStands
                            ? "Active stands exceed the total stand supply."
                            : "The stand total and the bucket sum should agree.",
                        "GATE-004");

                    if (result.ActiveTotal > result.MaxStands)
                    {
                        AddFinding(
                            "GATE-002",
                            DiagnosticCategory.Gate,
                            DiagnosticSeverity.Error,
                            "More active stands than total stands",
                            "The peak requires more stands than the result provides, including " +
                            "overnight parking.",
                            metric: "stands",
                            expected: result.MaxStands,
                            actual: result.ActiveTotal);
                    }

                    AddGateRow(
                        "Contact stands (total vs bucket sum)",
                        result.ContactTotal,
                        bucketContact,
                        "Contact stands should reconcile across the stand groups.");

                    if (!result.ScheduleMode)
                    {
                        // PHP mode: every non-contact stand is a bus gate, so
                        // the gate total is the quantity that must sum.
                        AddGateRow(
                            "Bus gates (total vs group sum)",
                            result.BusTotal,
                            bucketBus,
                            "Bus gates should reconcile across the stand groups.");
                    }
                    else
                    {
                        // Schedule mode: the non-contact stands are remote
                        // stands and the bus gates are derived facility-wide
                        // from the ratio.
                        AddGateRow(
                            "Remote stands (total vs group sum)",
                            result.RemoteActive,
                            bucketRemote,
                            "Non-contact stands are remote stands in schedule mode.");
                    }

                    int expectedRemote = result.MaxStands - result.ContactTotal;
                    AddGateRow(
                        "Net remote stands",
                        expectedRemote,
                        result.NetRemote,
                        "Max stands less contact stands.");

                    int loungeCount = (result.Lounges ?? new List<GateLounge>())
                        .Sum(l => l?.Count ?? 0);

                    AddGateRow(
                        "Gate lounge count",
                        loungeCount,
                        result.LoungeCount,
                        "Total lounges of every kind.");

                    double expectedArea = result.HoldArea + result.PodiumArea + result.QueueArea;
                    double areaVariance = result.NetArea - expectedArea;

                    _report.GateRows.Add(new DiagnosticsGateRow
                    {
                        Label = "Net lounge area (parts vs total)",
                        Expected = (int)Math.Round(expectedArea),
                        Actual = (int)Math.Round(result.NetArea),
                        Status = Math.Abs(areaVariance) > 0.5
                            ? DiagnosticStatus.Fail
                            : DiagnosticStatus.Pass,
                        Note = Math.Abs(areaVariance) > 0.5
                            ? "Net area does not equal hold + podium + queue area."
                            : "Hold, podium and queue areas sum to the net area."
                    });

                    if (Math.Abs(areaVariance) > 0.5)
                    {
                        AddFinding(
                            "GATE-003",
                            DiagnosticCategory.Gate,
                            DiagnosticSeverity.Error,
                            "Gate lounge area does not add up",
                            "Net lounge area does not equal the sum of hold, podium and queue area.",
                            metric: "m2",
                            expected: expectedArea,
                            actual: result.NetArea,
                            unit: "m2");
                    }

                    if (result.LoungeCount == 0 && result.ActiveTotal > 0)
                    {
                        AddFinding(
                            "GATE-011",
                            DiagnosticCategory.Gate,
                            DiagnosticSeverity.Warning,
                            "No gate lounges were sized",
                            "Stands were sized but no gate lounge was produced.",
                            expected: 1,
                            actual: 0);
                    }

                    if (_input.Schedule != null && !result.ScheduleMode)
                    {
                        AddFinding(
                            "GATE-010",
                            DiagnosticCategory.Gate,
                            DiagnosticSeverity.Info,
                            "Gate result is not in schedule mode",
                            "A schedule is wired but the gate result was sized from PHP movement " +
                            "inputs rather than the schedule.",
                            metric: "mode",
                            expected: 1,
                            actual: 0);
                    }
                }
            }

            private void AddGateRow(
                string label,
                int expected,
                int actual,
                string note,
                string code = "GATE-005")
            {
                DiagnosticStatus status = expected == actual
                    ? DiagnosticStatus.Pass
                    : DiagnosticStatus.Fail;

                _report.GateRows.Add(new DiagnosticsGateRow
                {
                    Label = label,
                    Expected = expected,
                    Actual = actual,
                    Status = status,
                    Note = note
                });

                if (status == DiagnosticStatus.Fail)
                {
                    AddFinding(
                        code,
                        DiagnosticCategory.Gate,
                        DiagnosticSeverity.Error,
                        code == "GATE-004"
                            ? "Stand bucket totals do not reconcile"
                            : "Gate count does not reconcile",
                        label + ": expected " + expected + ", actual " + actual + ".",
                        metric: label,
                        expected: expected,
                        actual: actual);
                }
            }

            // ---------------------------------------------------------
            // Sizing
            // ---------------------------------------------------------

            private void CheckSizing()
            {
                List<DiagnosticsSizingInput> results =
                    _input.SizingResults ?? new List<DiagnosticsSizingInput>();

                foreach (DiagnosticsSizingInput result in results)
                {
                    if (result == null)
                        continue;

                    string label = string.IsNullOrWhiteSpace(result.Label)
                        ? "Sizing result"
                        : result.Label.Trim();

                    DiagnosticStatus status = DiagnosticStatus.Pass;
                    List<string> notes = new List<string>();

                    if (result.PeakDemand > 0 && result.ProcessorCount <= 0)
                    {
                        status = DiagnosticStatus.Fail;
                        notes.Add("Peak demand is non-zero but no processors were sized.");

                        AddFinding(
                            "SIZE-001",
                            DiagnosticCategory.Sizing,
                            DiagnosticSeverity.Error,
                            "Sizing has demand but no processors",
                            label + ": " + result.PeakDemand + " peak passenger(s) but " +
                            result.ProcessorCount + " processor(s).",
                            metric: "processors",
                            expected: 1,
                            actual: result.ProcessorCount,
                            serviceLabel: label);
                    }

                    if (result.NetArea > 0 && result.GrossArea > 0 &&
                        result.GrossArea + 0.5 < result.NetArea)
                    {
                        status = DiagnosticStatus.Fail;
                        notes.Add("Gross area is smaller than net area.");

                        AddFinding(
                            "SIZE-002",
                            DiagnosticCategory.Sizing,
                            DiagnosticSeverity.Error,
                            "Gross area is smaller than net area",
                            label + ": net " +
                            result.NetArea.ToString("0.##", CultureInfo.InvariantCulture) +
                            " m2, gross " +
                            result.GrossArea.ToString("0.##", CultureInfo.InvariantCulture) +
                            " m2.",
                            metric: "m2",
                            expected: result.NetArea,
                            actual: result.GrossArea,
                            unit: "m2",
                            serviceLabel: label);
                    }

                    if (status != DiagnosticStatus.Fail &&
                        result.PeakDemand > 0 &&
                        result.NetArea <= 0)
                    {
                        status = DiagnosticStatus.Warn;
                        notes.Add("Peak demand is non-zero but net area is zero.");
                    }

                    if (result.Warnings != null && result.Warnings.Count > 0)
                    {
                        if (status == DiagnosticStatus.Pass)
                            status = DiagnosticStatus.Warn;

                        notes.AddRange(Cap(result.Warnings));

                        AddFinding(
                            "SIZE-003",
                            DiagnosticCategory.Sizing,
                            DiagnosticSeverity.Warning,
                            "Sizing warnings",
                            label + ": " + result.Warnings.Count + " warning(s) were reported.",
                            expected: 0,
                            actual: result.Warnings.Count,
                            serviceLabel: label,
                            details: Cap(result.Warnings));
                    }

                    _report.SizingRows.Add(new DiagnosticsSizingRow
                    {
                        Label = label,
                        PeakDemand = result.PeakDemand,
                        ProcessorCount = result.ProcessorCount,
                        NetArea = result.NetArea,
                        GrossArea = result.GrossArea,
                        Status = status,
                        Note = string.Join(" ", notes)
                    });
                }
            }

            // ---------------------------------------------------------
            // Report assembly
            // ---------------------------------------------------------

            private void FinalizeReport()
            {
                if (_passengerIssuesTruncated)
                {
                    AddFinding(
                        "PAX-999",
                        DiagnosticCategory.Passenger,
                        DiagnosticSeverity.Warning,
                        "Passenger issue list was truncated",
                        "More than " + PassengerIssueLimit + " passenger issues were found. " +
                        "Use the CSV output or narrow the population to see the remainder.",
                        expected: PassengerIssueLimit,
                        actual: _report.PassengerIssues.Count);
                }

                _report.ErrorCount = _report.Findings.Count(f =>
                    f.Severity == DiagnosticSeverity.Error);
                _report.WarningCount = _report.Findings.Count(f =>
                    f.Severity == DiagnosticSeverity.Warning);
                _report.InfoCount = _report.Findings.Count(f =>
                    f.Severity == DiagnosticSeverity.Info);

                // NotWired rows describe missing coverage, not a result. Only
                // Fail and Warn rows drive the headline; the finding counts
                // already include one finding per actionable row.
                bool rowFailed = _report.Services.Any(r => r.Status == DiagnosticStatus.Fail) ||
                                 _report.Transfers.Any(r => r.Status == DiagnosticStatus.Fail) ||
                                 _report.TransferServices.Any(r => r.Status == DiagnosticStatus.Fail) ||
                                 _report.Peaks.Any(r => r.Status == DiagnosticStatus.Fail) ||
                                 _report.GateRows.Any(r => r.Status == DiagnosticStatus.Fail) ||
                                 _report.SizingRows.Any(r => r.Status == DiagnosticStatus.Fail);

                bool rowWarned = _report.Services.Any(r => r.Status == DiagnosticStatus.Warn) ||
                                 _report.Transfers.Any(r => r.Status == DiagnosticStatus.Warn) ||
                                 _report.TransferServices.Any(r => r.Status == DiagnosticStatus.Warn) ||
                                 _report.Peaks.Any(r => r.Status == DiagnosticStatus.Warn) ||
                                 _report.GateRows.Any(r => r.Status == DiagnosticStatus.Warn) ||
                                 _report.SizingRows.Any(r => r.Status == DiagnosticStatus.Warn);

                DiagnosticStatus overall;

                if (_report.ErrorCount > 0 || rowFailed)
                    overall = DiagnosticStatus.Fail;
                else if (_report.WarningCount > 0 || rowWarned)
                    overall = DiagnosticStatus.Warn;
                else if (_report.PopulationCount == 0 &&
                         _report.ServiceResultCount == 0 &&
                         _report.GateRows.Count == 0 &&
                         _report.SizingRows.Count == 0)
                    overall = DiagnosticStatus.NotWired;
                else
                    overall = DiagnosticStatus.Pass;

                _report.OverallStatus = overall;

                _report.CoverageLines = new List<string>
                {
                    "Schedule: " + (_input.Schedule != null
                        ? "wired (" + _input.Schedule.Legs.Count + " legs)"
                        : "not wired"),
                    "Generation: " + (_input.Generation != null
                        ? "wired (" + (_input.Generation.Passengers?.Count ?? 0) + " passengers)"
                        : "not wired"),
                    "Transfer demand: " + (_demand != null
                        ? "wired (" + _demand.EffectiveTransferPassengers + " effective, " +
                          _demand.TaggedTransferPassengers + " tagged)"
                        : "not wired"),
                    "Service results: " + _report.ServiceResultCount + " of " +
                    (_input.Services?.Count ?? 0),
                    "Gate results: " + (_input.GateResults?.Count ?? 0),
                    "Sizing results: " + (_input.SizingResults?.Count ?? 0),
                    "Population: " + _report.PopulationCount + " records from " +
                    _report.PopulationSource
                };

                AddDefinitions();
            }

            private void AddDefinitions()
            {
                AddDefinition(
                    "Overall status",
                    _report.OverallStatus.ToDisplayLabel(),
                    "FAIL outranks WARN. INFO never changes the headline. NOT WIRED means " +
                    "there was nothing to check.");

                AddDefinition(
                    "Population",
                    _report.PopulationCount.ToString(CultureInfo.InvariantCulture),
                    "Passenger records, not journeys. Source: " + _report.PopulationSource + ".");

                AddDefinition(
                    "Warm-up",
                    _report.WarmUpCount.ToString(CultureInfo.InvariantCulture),
                    "Simulated but excluded from pool passenger counts and queue metrics. " +
                    "QueueResults.ProcessedPassengers still includes warm-up.");

                AddDefinition(
                    "Design-step records",
                    _report.DesignStepCount.ToString(CultureInfo.InvariantCulture),
                    "Passengers tagged IsDesignStep in the generated population.");

                AddDefinition(
                    "Transfer legs (out / in)",
                    _report.OutboundTransferLegs + " / " + _report.InboundTransferLegs,
                    "Outbound counts departure legs. Inbound counts separate arrival records; " +
                    "there is no persistent journey link between them.");

                AddDefinition(
                    "Expected",
                    "independent rule",
                    "Recomputed from DHPType, Journey, passenger flags and transfer scenario " +
                    "rules. Solvers are not re-run.");

                AddDefinition(
                    "Processed",
                    "QueueResults.ProcessedPassengers",
                    "Records the stage reported as served, warm-up included.");

                AddDefinition(
                    "Pool passengers",
                    "QueuePoolResult.PassengerCount",
                    "Warm-up excluded, so this should equal non-warm-up processed records.");

                AddDefinition(
                    "Missing / extra",
                    "expected set difference",
                    "Missing = expected but not processed. Extra = processed but not expected by " +
                    "the independent rule.");

                AddDefinition(
                    "Missing history",
                    "facility record absent",
                    "A processed passenger without the facility's History entry. The visit cannot " +
                    "be audited downstream.");

                AddDefinition(
                    "Bag Drop",
                    "separate stage",
                    "Counter passengers have their bag accepted at the check-in desk. Web and Kiosk " +
                    "passengers with a checked bag, and transfers whose rule requires bag drop, are " +
                    "served by the standalone Bag Drop stage, which writes Bag Drop history.");

                AddDefinition(
                    "Transfer service processed",
                    "history presence",
                    "A transfer record counts as served when it carries the corresponding facility " +
                    "history. Bag Drop history is written by the Bag Drop stage.");

                AddDefinition(
                    "Not wired",
                    "no result",
                    "No Queue Results, typed result or passenger history was available for this check.");
            }

            private void AddDefinition(string label, string value, string note)
            {
                _report.Definitions.Add(new DiagnosticsDefinitionRow
                {
                    Label = label,
                    Value = value,
                    Note = note
                });
            }

            // ---------------------------------------------------------
            // Shared helpers
            // ---------------------------------------------------------

            private DiagnosticFinding AddFinding(
                string code,
                DiagnosticCategory category,
                DiagnosticSeverity severity,
                string title,
                string message,
                string metric = "",
                double? expected = null,
                double? actual = null,
                string unit = "",
                string serviceLabel = "",
                string scenario = "",
                IEnumerable<int> ids = null,
                IEnumerable<string> legs = null,
                IEnumerable<string> details = null)
            {
                DiagnosticFinding finding = new DiagnosticFinding
                {
                    Code = code,
                    Category = category,
                    Severity = severity,
                    Title = title,
                    Message = message ?? string.Empty,
                    Metric = metric ?? string.Empty,
                    Expected = expected,
                    Actual = actual,
                    Unit = unit ?? string.Empty,
                    ServiceLabel = serviceLabel ?? string.Empty,
                    Scenario = scenario ?? string.Empty,
                    PassengerIds = CapIds(ids),
                    FlightLegIds = CapStrings(legs),
                    Details = Cap(details)
                };

                if (expected.HasValue && actual.HasValue)
                    finding.Variance = actual.Value - expected.Value;

                _report.Findings.Add(finding);
                return finding;
            }

            private void AddPassengerIssues(
                IEnumerable<Passenger> passengers,
                string code,
                DiagnosticSeverity severity,
                string message)
            {
                foreach (Passenger passenger in passengers)
                {
                    if (_report.PassengerIssues.Count >= PassengerIssueLimit)
                    {
                        _passengerIssuesTruncated = true;
                        return;
                    }

                    _report.PassengerIssues.Add(new DiagnosticsPassengerIssue
                    {
                        PassengerId = passenger.Id,
                        FlightLegId = passenger.FlightLegId ?? string.Empty,
                        Airline = passenger.Airline ?? string.Empty,
                        FlightNumber = passenger.FlightNumber ?? string.Empty,
                        DhpType = passenger.DHPType.ToString(),
                        Journey = passenger.Journey.ToString(),
                        Scenario = DescribeScenario(passenger),
                        Channel = passenger.Channel ?? string.Empty,
                        UsesOnlineCheckIn = passenger.UsesOnlineCheckIn,
                        HasCheckedBag = passenger.HasCheckedBag,
                        Code = code,
                        Severity = severity,
                        Message = message ?? string.Empty
                    });
                }
            }

            private List<int> CapIds(IEnumerable<int> ids)
            {
                if (ids == null)
                    return new List<int>();

                return ids
                    .Distinct()
                    .Take(Math.Max(1, _input.MaxPassengerIds))
                    .ToList();
            }

            private static List<string> CapStrings(IEnumerable<string> values)
            {
                if (values == null)
                    return new List<string>();

                return values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.Ordinal)
                    .Take(DetailLimit)
                    .ToList();
            }

            private static List<string> Cap(IEnumerable<string> values)
            {
                if (values == null)
                    return new List<string>();

                return values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Take(DetailLimit)
                    .ToList();
            }

            private static string DescribePassenger(Passenger passenger)
            {
                string flight = string.Join(" ", new[] { passenger.Airline, passenger.FlightNumber }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

                string text = "PAX " + passenger.Id;

                if (!string.IsNullOrWhiteSpace(flight))
                    text += " " + flight;

                if (!string.IsNullOrWhiteSpace(passenger.FlightLegId))
                    text += " leg " + passenger.FlightLegId;

                text += " (" + passenger.DHPType + ", " + passenger.Journey;

                if (!string.IsNullOrWhiteSpace(passenger.Channel))
                    text += ", channel " + passenger.Channel.Trim();

                text += ")";
                return text;
            }

            private static string DescribeLeg(FlightLeg leg)
            {
                string flight = string.Join(" ", new[] { leg.Airline, leg.FlightNumber }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

                string text = "Leg " + leg.Id;
                if (!string.IsNullOrWhiteSpace(flight))
                    text += " " + flight;
                text += " " + leg.Direction;
                return text;
            }

            private static string DescribeScenario(Passenger passenger)
            {
                return passenger.TransferScenario.HasValue
                    ? passenger.TransferScenario.Value.ToFlowLabel()
                    : string.Empty;
            }

            private static bool HasHistory(Passenger passenger, FacilityType facility)
            {
                return passenger?.History != null &&
                       passenger.History.ContainsKey(facility);
            }

            /// <summary>
            /// Collects the records that already carry a facility history. It
            /// reads the fallback population and every wired stage output
            /// (explicit population, full result population and processed
            /// list) because a facility served inside another stage writes its
            /// history on that stage's passenger clones. Records are deduplicated
            /// by Id, keeping the first occurrence.
            /// </summary>
            private List<Passenger> CollectHistoryBacked(
                FacilityType facility,
                List<Passenger> fallback)
            {
                Dictionary<int, Passenger> byId = new Dictionary<int, Passenger>();

                AddHistoryBacked(byId, fallback, facility);
                AddHistoryBacked(byId, _population, facility);

                foreach (DiagnosticsServiceInput service in _input.Services ??
                    new List<DiagnosticsServiceInput>())
                {
                    if (service == null)
                        continue;

                    AddHistoryBacked(byId, service.Passengers, facility);
                    AddHistoryBacked(byId, service.Queue?.Passengers, facility);
                    AddHistoryBacked(byId, service.Queue?.ProcessedPassengers, facility);
                }

                return byId.Values.ToList();
            }

            private static void AddHistoryBacked(
                Dictionary<int, Passenger> byId,
                List<Passenger> passengers,
                FacilityType facility)
            {
                if (passengers == null)
                    return;

                foreach (Passenger passenger in passengers)
                {
                    if (passenger?.History == null ||
                        !passenger.History.ContainsKey(facility) ||
                        byId.ContainsKey(passenger.Id))
                    {
                        continue;
                    }

                    byId.Add(passenger.Id, passenger);
                }
            }

            private static bool IsDeparture(Passenger passenger)
            {
                return passenger != null &&
                       (passenger.DHPType == DHPType.DomesticDeparture ||
                        passenger.DHPType == DHPType.InternationalDeparture);
            }

            // ---------------------------------------------------------
            // Breakdowns
            // ---------------------------------------------------------

            /// <summary>
            /// Groups the expected set by one passenger attribute and keeps the
            /// missing subset. IDs are stored only for missing records so the
            /// aggregate rows stay short.
            /// </summary>
            private List<DiagnosticsBreakdownRow> BuildBreakdown(
                List<Passenger> expected,
                ISet<int> processedIds,
                Func<Passenger, string> label)
            {
                return expected
                    .GroupBy(label)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        List<Passenger> missing = group
                            .Where(p => !processedIds.Contains(p.Id))
                            .ToList();

                        return new DiagnosticsBreakdownRow
                        {
                            Label = group.Key,
                            Expected = group.Count(),
                            Processed = group.Count() - missing.Count,
                            Missing = missing.Count,
                            PassengerIds = CapIds(missing.Select(p => p.Id))
                        };
                    })
                    .ToList();
            }

            /// <summary>
            /// Groups an already-filtered subset (for example the missing
            /// bag-drop records) by one attribute.
            /// </summary>
            private List<DiagnosticsBreakdownRow> BuildCountBreakdown(
                List<Passenger> records,
                Func<Passenger, string> label)
            {
                return records
                    .GroupBy(label)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new DiagnosticsBreakdownRow
                    {
                        Label = group.Key,
                        Count = group.Count(),
                        Expected = group.Count(),
                        Missing = group.Count(),
                        PassengerIds = CapIds(group.Select(p => p.Id))
                    })
                    .ToList();
            }

            private DiagnosticsBagDropBreakdown BuildBagDropBreakdown(
                List<Passenger> expected,
                List<Passenger> incoming)
            {
                List<Passenger> bagDropExpected = expected
                    .Where(p => p.RequiresSeparateBagDrop)
                    .ToList();

                HashSet<int> recordedIds = new HashSet<int>(
                    CollectHistoryBacked(FacilityType.BagDrop, incoming)
                        .Select(p => p.Id));

                List<Passenger> recorded = bagDropExpected
                    .Where(p => recordedIds.Contains(p.Id))
                    .ToList();

                List<Passenger> missing = bagDropExpected
                    .Where(p => !recordedIds.Contains(p.Id))
                    .ToList();

                List<Passenger> late = incoming
                    .Where(p => IsDeparture(p) &&
                                p.RequiresSeparateBagDrop &&
                                p.IsLateForCheckIn)
                    .ToList();

                return new DiagnosticsBagDropBreakdown
                {
                    Active = bagDropExpected.Count > 0 || recordedIds.Count > 0,
                    Expected = bagDropExpected.Count,
                    // Bag Drop is a separate stage, so every eligible passenger
                    // is correctly placed regardless of their primary channel.
                    OnChannel = bagDropExpected.Count,
                    Recorded = recorded.Count,
                    Missing = missing.Count,
                    RoutingMismatch = 0,
                    LateBypassed = late.Count,
                    MissingPassengerIds = CapIds(missing.Select(p => p.Id)),
                    RecordedPassengerIds = CapIds(recorded.Select(p => p.Id)),
                    RoutingMismatchPassengerIds = new List<int>(),
                    Channels = BuildBreakdown(
                        bagDropExpected, recordedIds, DescribeChannel),
                    RoutingMismatchChannels = new List<DiagnosticsBreakdownRow>(),
                    MissingChannels = BuildCountBreakdown(missing, DescribeChannel),
                    MissingMix = BuildCountBreakdown(missing, DescribePassengerMix),
                    MissingDepartureTypes = BuildCountBreakdown(
                        missing, DescribeDepartureType),
                    MissingJourneys = BuildCountBreakdown(missing, DescribeJourney)
                };
            }

            private List<DiagnosticsBreakdownRow> BuildExceptionBreakdown(
                List<Passenger> incoming)
            {
                List<DiagnosticsBreakdownRow> rows = new List<DiagnosticsBreakdownRow>();

                List<Passenger> late = incoming
                    .Where(p => IsDeparture(p) && p.IsLateForCheckIn)
                    .ToList();

                if (late.Count > 0)
                {
                    rows.Add(new DiagnosticsBreakdownRow
                    {
                        Label = "Late for check-in",
                        Count = late.Count,
                        PassengerIds = CapIds(late.Select(p => p.Id))
                    });
                }

                List<Passenger> ruleBypass = incoming
                    .Where(p => IsDeparture(p) &&
                                !p.IsLateForCheckIn &&
                                p.Journey == PassengerJourney.Transfer &&
                                p.TransferProcessing != null &&
                                TransferServiceUsageCalculator.IsBypassed(
                                    p, TransferService.CheckIn) &&
                                TransferServiceUsageCalculator.IsBypassed(
                                    p, TransferService.BagDrop))
                    .ToList();

                if (ruleBypass.Count > 0)
                {
                    rows.Add(new DiagnosticsBreakdownRow
                    {
                        Label = "Transfer rule bypasses check-in and bag drop",
                        Count = ruleBypass.Count,
                        PassengerIds = CapIds(ruleBypass.Select(p => p.Id))
                    });
                }

                return rows;
            }

            private static string DescribeChannel(Passenger passenger)
            {
                return DescribeChannel(passenger?.Channel);
            }

            private static string DescribeChannel(string channel)
            {
                return string.IsNullOrWhiteSpace(channel)
                    ? "(no channel)"
                    : channel.Trim();
            }

            private static string DescribePassengerMix(Passenger passenger)
            {
                return (passenger.UsesOnlineCheckIn ? "Online" : "Offline") +
                       (passenger.HasCheckedBag ? " + bag" : " + no bag");
            }

            private static string DescribeDepartureType(Passenger passenger)
            {
                switch (passenger.DHPType)
                {
                    case DHPType.DomesticDeparture:
                        return "Domestic departure";
                    case DHPType.InternationalDeparture:
                        return "International departure";
                    default:
                        return passenger.DHPType.ToString();
                }
            }

            private static string DescribeJourney(Passenger passenger)
            {
                return passenger.Journey == PassengerJourney.Transfer
                    ? "Transfer"
                    : "Origin";
            }

            private static List<string> DescribeBreakdown(
                List<DiagnosticsBreakdownRow> rows)
            {
                return (rows ?? new List<DiagnosticsBreakdownRow>())
                    .Where(row => row != null && row.Count > 0)
                    .Select(row => row.Label + ": " + row.Count)
                    .ToList();
            }

            private static bool IsArrival(Passenger passenger)
            {
                return passenger != null &&
                       (passenger.DHPType == DHPType.DomesticArrival ||
                        passenger.DHPType == DHPType.InternationalArrival);
            }

            private static bool IsInternational(DHPType type)
            {
                return type == DHPType.InternationalArrival ||
                       type == DHPType.InternationalDeparture;
            }

            private static bool IsDomestic(DHPType type)
            {
                return type == DHPType.DomesticArrival ||
                       type == DHPType.DomesticDeparture;
            }

            /// <summary>
            /// Channels covered by the result's dedicated pools. An empty list
            /// means the result uses a shared pool (or published no pool
            /// detail), in which case every channel is treated as covered.
            /// </summary>
            private static List<string> GetPlannedChannels(QueueResults queue)
            {
                if (queue?.ProcessorPools == null || queue.ProcessorPools.Count == 0)
                    return new List<string>();

                if (queue.ProcessorPools.Any(p => p != null && p.IsShared))
                    return new List<string>();

                return queue.ProcessorPools
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => ChannelPart(p.Name))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            /// <summary>
            /// How many stacked stage cohorts the result's pools hold. One means
            /// a conventional result: every passenger is served exactly once, so
            /// pool passenger counts reconcile one-to-one with the processed
            /// list. A facility that queues the same passengers through
            /// consecutive stages in series (security's e-gate then ATRS)
            /// reports its stage count here, and the reconciliation multiplies.
            /// </summary>
            private static int StageMultiplier(QueueResults queue)
            {
                return queue != null && queue.SeriesStageCount > 1
                    ? queue.SeriesStageCount
                    : 1;
            }

            private static string ChannelPart(string poolName)
            {
                if (string.IsNullOrWhiteSpace(poolName))
                    return string.Empty;

                int separator = poolName.IndexOf('|');
                return (separator < 0 ? poolName : poolName.Substring(0, separator)).Trim();
            }

            private static bool IsChannelCovered(string channel, List<string> plannedChannels)
            {
                if (plannedChannels == null || plannedChannels.Count == 0)
                    return true;

                string value = (channel ?? string.Empty).Trim();
                return plannedChannels.Any(c =>
                    string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
            }

            private static List<string> DescribeChannelCounts(IEnumerable<Passenger> passengers)
            {
                if (passengers == null)
                    return new List<string>();

                return passengers
                    .GroupBy(
                        p => string.IsNullOrWhiteSpace(p.Channel)
                            ? "(no channel)"
                            : p.Channel.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key + " " + g.Count())
                    .ToList();
            }

            private static string DescribeChannels(
                IEnumerable<Passenger> passengers,
                int take = 4)
            {
                return string.Join(", ", DescribeChannelCounts(passengers).Take(take));
            }

            private sealed class PeakWindow
            {
                private readonly bool _all;

                public PeakWindow(int startMinute, int count)
                {
                    _all = false;
                    StartMinute = startMinute;
                    Count = count;
                    Label = FormatWindow(startMinute);
                }

                private PeakWindow(string label)
                {
                    _all = true;
                    StartMinute = 0;
                    Count = 0;
                    Label = label;
                }

                public static PeakWindow All(string label)
                {
                    return new PeakWindow(label);
                }

                public int StartMinute { get; }

                public int Count { get; }

                public string Label { get; }

                public bool Contains(Passenger passenger)
                {
                    if (_all)
                        return true;

                    if (passenger == null)
                        return false;

                    double minute = IsDeparture(passenger)
                        ? passenger.ScheduledDepartureTime
                        : passenger.ScheduledArrivalTime;

                    if (double.IsNaN(minute) || double.IsInfinity(minute))
                        return false;

                    return minute >= StartMinute && minute < StartMinute + 60.0;
                }

                private static string FormatWindow(int startMinute)
                {
                    return SchedulePassengerGenerationResult.FormatMinute(startMinute) +
                           " + 60 min";
                }
            }
        }
    }
}
