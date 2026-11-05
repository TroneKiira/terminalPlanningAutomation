using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Diagnostics;
using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunModelDiagnosticsTests()
        {
            Console.WriteLine("== Model diagnostics ==");

            RunDiagnosticsCleanCheckIn();
            RunDiagnosticsMissingExpected();
            RunDiagnosticsMissingHistory();
            RunDiagnosticsTransferShortfall();
            RunDiagnosticsTransferServiceRules();
            RunDiagnosticsTransferLateRequired();
            RunDiagnosticsVerification();
            RunDiagnosticsGatesAndSizing();
            RunDiagnosticsChannelCoverage();
            RunDiagnosticsVirtualWebChannel();
            RunDiagnosticsIntegratedBagDrop();
            RunDiagnosticsCsvAndEmpty();
        }

        // -------------------------------------------------------------
        // Services
        // -------------------------------------------------------------

        private static void RunDiagnosticsCleanCheckIn()
        {
            List<Passenger> passengers = DiagOriginPassengers(3);
            List<Passenger> processed = passengers
                .Select(p => DiagWithHistory(p, FacilityType.CheckIn))
                .ToList();

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.Expected == 3 && row.Processed == 3,
                $"Diagnostics finds 3 expected and 3 processed check-in records ({row.Expected}/{row.Processed})");
            Check(row.MissingExpected == 0 && row.ExtraProcessed == 0,
                "Diagnostics reports no missing or extra check-in records");
            Check(row.Status == DiagnosticStatus.Pass,
                "A consistent check-in result is PASS");
            Check(report.Findings.Any(f => f.Code == "DWL-001"),
                "A report without lounge history says dwell was not audited");
            Check(report.OverallStatus == DiagnosticStatus.Pass,
                "A consistent report is PASS overall");
        }

        private static void RunDiagnosticsMissingExpected()
        {
            List<Passenger> passengers = DiagOriginPassengers(3);
            List<Passenger> processed = passengers
                .Take(2)
                .Select(p => DiagWithHistory(p, FacilityType.CheckIn))
                .ToList();

            int missingId = passengers[2].Id;

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.Status == DiagnosticStatus.Fail,
                "A missing expected record fails the service row");
            Check(row.MissingPassengerIds.Contains(missingId),
                $"Diagnostics names the missing passenger ({missingId})");
            Check(report.Findings.Any(f =>
                    f.Code == "SVC-001" && f.PassengerIds.Contains(missingId)),
                "SVC-001 carries the missing passenger ID");
            Check(report.PassengerIssues.Any(p =>
                    p.PassengerId == missingId && p.Code == "PAX-SVC-001"),
                "The passenger issue list carries the missing record");
        }

        private static void RunDiagnosticsMissingHistory()
        {
            List<Passenger> passengers = DiagOriginPassengers(2);
            List<Passenger> processed = passengers
                .Select(p => DiagWithHistory(p, FacilityType.CheckIn))
                .ToList();

            // Strip history from one processed record. The count still matches,
            // but the visit cannot be audited downstream.
            processed[1] = processed[1].Clone();
            processed[1].History.Remove(FacilityType.CheckIn);

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.MissingHistory == 1,
                "Diagnostics finds the processed record without check-in history");
            Check(row.Status == DiagnosticStatus.Warn,
                "A missing history record warns rather than fails");
            Check(report.Findings.Any(f => f.Code == "SVC-004"),
                "SVC-004 explains the missing facility history");
        }

        // -------------------------------------------------------------
        // Transfers
        // -------------------------------------------------------------

        private static void RunDiagnosticsTransferShortfall()
        {
            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(DiagTransferPassenger(
                    3000 + i,
                    DHPType.DomesticDeparture,
                    PassengerType.Domestic,
                    TransferScenario.DomesticToDomestic,
                    DiagRules(checkIn: true),
                    departureMinute: 600.0 + i));
            }

            TransferDemandResult demand = new TransferDemandResult
            {
                ByScenario = new Dictionary<TransferScenario, TransferScenarioDemand>
                {
                    {
                        TransferScenario.DomesticToDomestic,
                        new TransferScenarioDemand
                        {
                            Scenario = TransferScenario.DomesticToDomestic,
                            RequestedPassengers = 5,
                            FeasibleMaximum = 4,
                            EffectivePassengers = 4,
                            TaggedPassengers = 2
                        }
                    }
                }
            };

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                TransferDemand = demand
            });

            DiagnosticsTransferRow row = report.Transfers
                .Single(r => r.Scenario == TransferScenario.DomesticToDomestic.ToFlowLabel());

            Check(row.Effective == 4 && row.Tagged == 2 && row.Shortfall == 2,
                $"Transfer row shows 2 of 4 effective passengers tagged ({row.Tagged}/{row.Effective})");
            Check(row.Status == DiagnosticStatus.Warn,
                "A tagged shortfall warns");
            Check(report.Findings.Any(f => f.Code == "TRF-010"),
                "TRF-010 explains the tagged shortfall");
            Check(!report.Findings.Any(f => f.Code == "TRF-012"),
                "A shortfall is not mistaken for a tagged-count mismatch");
        }

        private static void RunDiagnosticsTransferServiceRules()
        {
            TransferScenarioRules required = DiagRules(security: true);
            TransferScenarioRules bypassed = DiagRules(security: false);

            Passenger requiredOne = DiagTransferPassenger(
                4000, DHPType.DomesticDeparture, PassengerType.Domestic,
                TransferScenario.DomesticToDomestic, required, 600.0);

            Passenger requiredMissing = DiagTransferPassenger(
                4001, DHPType.DomesticDeparture, PassengerType.Domestic,
                TransferScenario.DomesticToDomestic, required, 605.0);

            Passenger bypassedServed = DiagTransferPassenger(
                4002, DHPType.DomesticDeparture, PassengerType.Domestic,
                TransferScenario.DomesticToDomestic, bypassed, 610.0);

            List<Passenger> passengers = new List<Passenger>
            {
                requiredOne, requiredMissing, bypassedServed
            };

            List<Passenger> processed = new List<Passenger>
            {
                DiagWithHistory(requiredOne, FacilityType.Security),
                DiagWithHistory(bypassedServed, FacilityType.Security)
            };

            // The audited population is the post-facility output: the served
            // records carry their history there, exactly as the facility
            // components emit them.
            List<Passenger> output = new List<Passenger>
            {
                DiagWithHistory(requiredOne, FacilityType.Security),
                requiredMissing.Clone(),
                DiagWithHistory(bypassedServed, FacilityType.Security)
            };

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = output,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.Security,
                        Label = "Security",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            DiagnosticsTransferServiceRow row = report.TransferServices
                .Single(r => r.Service == "Security");

            Check(row.Required == 2 && row.Bypassed == 1,
                $"Transfer service rules split 2 required and 1 bypassed ({row.Required}/{row.Bypassed})");
            Check(row.MissingRequired == 1,
                "Diagnostics finds the required transfer without security history");
            Check(row.ServedBypassed == 1,
                "Diagnostics finds the bypassed transfer that was still served");
            Check(row.Status == DiagnosticStatus.Fail,
                "A transfer service rule violation fails the row");
            Check(report.Findings.Any(f => f.Code == "TRF-SVC-001"),
                "TRF-SVC-001 reports the required-but-missing service");
            Check(report.Findings.Any(f => f.Code == "TRF-SVC-002"),
                "TRF-SVC-002 reports the served-but-bypassed service");
        }

        private static void RunDiagnosticsTransferLateRequired()
        {
            // A rule-required transfer that reaches check-in after the
            // flight's last-admission time is not served by the facility and
            // cannot carry CheckIn history. The audit applies the same
            // late-exclusion policy as the Check-In stage and names the
            // record instead of failing it.
            Passenger lateRequired = DiagTransferPassenger(
                4002, DHPType.DomesticDeparture, PassengerType.Domestic,
                TransferScenario.DomesticToDomestic, DiagRules(checkIn: true), 600.0);
            lateRequired.IsLateForCheckIn = true;

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = new List<Passenger> { lateRequired.Clone() },
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(new List<Passenger>(), new List<Passenger>(), 0)
                    }
                }
            });

            DiagnosticsTransferServiceRow row = report.TransferServices
                .Single(r => r.Service == "Check-in");

            Check(row.Required == 1,
                "The late transfer still counts as rule-required");
            Check(row.Processed == 0 && row.MissingRequired == 0,
                $"The late required transfer is not counted as missing ({row.Processed}/{row.MissingRequired})");
            Check(row.Status == DiagnosticStatus.Pass,
                "A late required transfer does not fail the service row");
            Check(row.Notes != null && row.Notes.Any(n => n.Contains("late for check-in") ||
                    n.Contains("last-admission")),
                "The row note names the late-exclusion");
            Check(!report.Findings.Any(f => f.Code == "TRF-SVC-001"),
                "TRF-SVC-001 does not fire for a late required transfer");
            Check(report.PassengerIssues.Any(p => p.PassengerId == 4002 &&
                    p.Code == "PAX-TRF-002" && p.Severity == DiagnosticSeverity.Info),
                "PAX-TRF-002 names the late required transfer as an info issue");
            Check(report.Services.All(s => s.Kind != DiagnosticsServiceKind.CheckIn ||
                    s.Status != DiagnosticStatus.Fail),
                "The Check-In stage does not fail for a late passenger");
        }

        // -------------------------------------------------------------
        // Verification, gates and sizing
        // -------------------------------------------------------------

        private static void RunDiagnosticsVerification()
        {
            List<Passenger> passengers = DiagOriginPassengers(5);

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.Verification,
                        Label = "Verification",
                        EligiblePassengers = 5,
                        RequiredGates = 0,
                        GateCapacity = 60.0,
                        Utilization = 0.8
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.Status == DiagnosticStatus.Fail,
                "Verification with demand and no gates fails");
            Check(report.Findings.Any(f => f.Code == "SVC-021"),
                "SVC-021 explains verification demand without gates");
            Check(!report.Findings.Any(f => f.Code == "SVC-001"),
                "Verification is not audited as a per-passenger processed list");
        }

        private static void RunDiagnosticsGatesAndSizing()
        {
            GateSizingResult gates = new GateSizingResult
            {
                ActiveTotal = 5,
                ContactTotal = 3,
                BusTotal = 2,
                MaxStands = 6,
                NetRemote = 3,
                Buckets = new List<GateBucketStands>
                {
                    new GateBucketStands { Bucket = "Domestic", Active = 2, Contact = 1, Bus = 1 },
                    new GateBucketStands { Bucket = "Swing", Active = 2, Contact = 2, Bus = 0 }
                },
                Lounges = new List<GateLounge>
                {
                    new GateLounge { Code = "C", Count = 4, HoldArea = 100, PodiumArea = 20, QueueArea = 30 }
                },
                HoldArea = 100.0,
                PodiumArea = 20.0,
                QueueArea = 30.0,
                NetArea = 120.0
            };

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                GateResults = new List<GateSizingResult> { gates },
                SizingResults = new List<DiagnosticsSizingInput>
                {
                    new DiagnosticsSizingInput
                    {
                        Label = "Check-In",
                        PeakDemand = 400,
                        ProcessorCount = 0,
                        NetArea = 200.0,
                        GrossArea = 180.0
                    }
                }
            });

            Check(report.GateRows.Any(r =>
                    r.Label.StartsWith("Active stands", StringComparison.Ordinal) &&
                    r.Status == DiagnosticStatus.Fail),
                "Gate bucket totals that do not reconcile are flagged");
            Check(report.Findings.Any(f => f.Code == "GATE-004"),
                "GATE-004 reports the stand bucket mismatch");
            Check(report.Findings.Any(f => f.Code == "GATE-003"),
                "GATE-003 reports the lounge area mismatch");
            Check(report.GateRows.Any(r =>
                    r.Label.StartsWith("Bus gates", StringComparison.Ordinal) &&
                    r.Status == DiagnosticStatus.Fail),
                "PHP mode: a bus-gate group mismatch is still flagged");

            DiagnosticsSizingRow sizing = report.SizingRows.Single();

            Check(sizing.Status == DiagnosticStatus.Fail,
                "Sizing with demand and no processors fails");
            Check(report.Findings.Any(f => f.Code == "SIZE-001"),
                "SIZE-001 explains the missing processors");
            Check(report.Findings.Any(f => f.Code == "SIZE-002"),
                "SIZE-002 reports gross area below net area");

            // Schedule mode: the non-contact stands are remote stands; bus
            // gates are derived facility-wide from the ratio.
            GateSizingResult scheduleGates = new GateSizingResult
            {
                ScheduleMode = true,
                ActiveTotal = 4,
                ContactTotal = 0,
                BusTotal = 2,
                RemoteActive = 4,
                MaxStands = 4,
                NetRemote = 4,
                Buckets = new List<GateBucketStands>
                {
                    new GateBucketStands { Bucket = "Domestic", Active = 2, Contact = 0, Bus = 2, Remote = 2 },
                    new GateBucketStands { Bucket = "Swing", Active = 2, Contact = 0, Bus = 2, Remote = 2 }
                },
                Lounges = new List<GateLounge>(),
                NetArea = 0
            };

            DiagnosticsReport scheduleReport = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                GateResults = new List<GateSizingResult> { scheduleGates }
            });

            Check(scheduleReport.GateRows.Any(r =>
                    r.Label.StartsWith("Remote stands", StringComparison.Ordinal) &&
                    r.Status == DiagnosticStatus.Pass),
                "Schedule mode: remote stands reconcile across the groups");
            Check(!scheduleReport.GateRows.Any(r =>
                    r.Label.StartsWith("Bus gates", StringComparison.Ordinal)),
                "Schedule mode: bus gates are a facility total and are not checked per group");
            Check(!scheduleReport.GateRows.Any(r =>
                    r.Label.StartsWith("Bus stands", StringComparison.Ordinal)),
                "The old bus-stand row is replaced by the bus-gates row");
            Check(!scheduleReport.Findings.Any(f => f.Code == "GATE-005"),
                "Schedule mode: no false gate-count failure");

            // PHP mode: every non-contact stand is itself a bus gate.
            GateSizingResult phpGates = new GateSizingResult
            {
                ActiveTotal = 4,
                ContactTotal = 2,
                BusTotal = 2,
                RemoteActive = 4,
                MaxStands = 5,
                NetRemote = 3,
                Buckets = new List<GateBucketStands>
                {
                    new GateBucketStands { Bucket = "Domestic", Active = 2, Contact = 1, Bus = 1, Remote = 1 },
                    new GateBucketStands { Bucket = "International", Active = 2, Contact = 1, Bus = 1, Remote = 1 }
                },
                Lounges = new List<GateLounge>(),
                NetArea = 0
            };

            DiagnosticsReport phpReport = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                GateResults = new List<GateSizingResult> { phpGates }
            });

            Check(phpReport.GateRows.Any(r =>
                    r.Label.StartsWith("Bus gates", StringComparison.Ordinal) &&
                    r.Status == DiagnosticStatus.Pass),
                "PHP mode: bus gates reconcile across the groups");
            Check(!phpReport.GateRows.Any(r =>
                    r.Label.StartsWith("Remote stands", StringComparison.Ordinal)),
                "PHP mode: remote stands are a facility-derived total without a group check");
            Check(!phpReport.Findings.Any(f => f.Code == "GATE-005"),
                "PHP mode: no false gate-count failure");
        }

        // -------------------------------------------------------------
        // Output and empty wiring
        // -------------------------------------------------------------

        private static void RunDiagnosticsCsvAndEmpty()
        {
            List<Passenger> passengers = DiagOriginPassengers(2);
            List<Passenger> processed = new List<Passenger>
            {
                DiagWithHistory(passengers[0], FacilityType.CheckIn)
            };

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            string csv = report.ToCsv();

            Check(csv.StartsWith("Section,Item,Code,", StringComparison.Ordinal),
                "CSV starts with the long-format header");
            Check(csv.Contains("PassengerIssue") && csv.Contains("PAX-SVC-001"),
                "CSV carries the passenger-level issue rows");
            Check(report.SummaryLines().Any(l => l.Contains("CHECK-IN", StringComparison.OrdinalIgnoreCase)),
                "Summary lists the wired check-in service");
            Check(report.ServiceTableLines().Any(l => l.Contains("Check-In")),
                "Service table names the wired service");

            DiagnosticsReport empty = DiagnosticsCalculator.Calculate(new DiagnosticsInput());

            Check(empty.OverallStatus == DiagnosticStatus.NotWired,
                "An unwired model reports NOT WIRED rather than PASS");
            Check(empty.SummaryLines().Any(l => l.Contains("NOT WIRED")),
                "The empty summary says NOT WIRED");
        }

        // -------------------------------------------------------------
        // Channel coverage and virtual channels
        // -------------------------------------------------------------

        private static void RunDiagnosticsChannelCoverage()
        {
            List<Passenger> passengers = DiagOriginPassengers(3);

            // All three are Counter passengers. The result's only pool serves
            // Counter, but two expected records were not processed.
            List<Passenger> processed = new List<Passenger>
            {
                DiagWithHistory(passengers[0], FacilityType.CheckIn)
            };

            QueueResults queue = new QueueResults
            {
                Passengers = passengers,
                ProcessedPassengers = processed,
                PhysicalProcessorCount = 1,
                ResourcesUsed = 1,
                PeakQueueLength = 1,
                AverageWait = 0.5,
                MaxWait = 2.0,
                Wait95 = 1.0,
                ProcessorPools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo
                    {
                        Name = "Kiosk",
                        ResourceCount = 1,
                        PhysicalResourceCount = 1,
                        FirstResourceIndex = 0,
                        IsShared = false
                    }
                },
                PoolResults = new List<QueuePoolResult>
                {
                    new QueuePoolResult
                    {
                        Name = "Kiosk",
                        Channel = "Kiosk",
                        IsShared = false,
                        PhysicalResources = 1,
                        ActiveResources = 1,
                        PassengerCount = 1
                    }
                }
            };

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = queue
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.MissingExpected == 2,
                $"Two bag-drop records are outside the planned pool ({row.MissingExpected})");
            Check(report.Findings.Any(f => f.Code == "SVC-011" && f.ServiceLabel == "Check-In"),
                "SVC-011 reports expected records outside every planned pool");
            Check(report.Findings
                    .Where(f => f.Code == "SVC-011")
                    .SelectMany(f => f.Details)
                    .Any(d => d.Contains("Counter 2")),
                "SVC-011 details count the uncovered channel");
            Check(row.Notes.Any(n => n.Contains("Counter 2")),
                "The service row note names the uncovered channel");
            Check(report.PassengerIssues.All(p => !string.IsNullOrWhiteSpace(p.Channel)),
                "Passenger issues carry the channel");
        }

        private static void RunDiagnosticsVirtualWebChannel()
        {
            List<Passenger> passengers = DiagOriginPassengers(2);

            passengers.Add(new Passenger
            {
                Id = 3000,
                Channel = "Web",
                DHPType = DHPType.DomesticDeparture,
                PassengerType = PassengerType.Domestic,
                Journey = PassengerJourney.Origin,
                ProfileName = "DIAG",
                UsesOnlineCheckIn = true,
                HasCheckedBag = false,
                ArrivalTime = 12.0,
                QueueArrivalTime = 12.0,
                ScheduledDepartureTime = 720.0,
                FlightLegId = "W0",
                Airline = "AP",
                FlightNumber = "300"
            });

            List<Passenger> processed = passengers
                .Select(p => DiagWithHistory(p, FacilityType.CheckIn))
                .ToList();

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, processed, 2)
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.Expected == 2 && row.ExtraProcessed == 1,
                $"Only the Web record is extra at Check-In ({row.Expected}/{row.ExtraProcessed})");
            Check(report.Findings.Any(f => f.Code == "SVC-012"),
                "SVC-012 warns when the virtual Web channel is served at Check-In");
            Check(row.Notes.Any(n => n.Contains("Web")),
                "The service row note names the virtual Web channel");
            Check(report.PassengerIssues.Any(p =>
                    p.PassengerId == 3000 &&
                    p.Channel == "Web" &&
                    p.UsesOnlineCheckIn &&
                    !p.HasCheckedBag),
                "The Web issue exposes the channel and the online/bag flags");
        }

        /// <summary>
        /// Bag Drop is its own stage. Counter passengers have their bag
        /// accepted at check-in, while Web and Kiosk bag carriers use Bag Drop.
        /// Each stage owns its own service row and history.
        /// </summary>
        private static void RunDiagnosticsIntegratedBagDrop()
        {
            List<Passenger> passengers = DiagOriginPassengers(5);

            for (int i = 0; i < 3; i++)
            {
                passengers[i].Channel = "Web";
                passengers[i].UsesOnlineCheckIn = true;
                passengers[i].HasCheckedBag = true;
            }

            List<Passenger> checkInProcessed = new List<Passenger>
            {
                DiagWithHistory(passengers[3], FacilityType.CheckIn),
                DiagWithHistory(passengers[4], FacilityType.CheckIn)
            };

            List<Passenger> bagDropProcessed = new List<Passenger>();

            for (int i = 0; i < 3; i++)
                bagDropProcessed.Add(DiagWithHistory(passengers[i], FacilityType.BagDrop));

            DiagnosticsReport report = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, checkInProcessed, 2)
                    },
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.BagDrop,
                        Label = "Bag Drop",
                        Queue = DiagQueue(passengers, bagDropProcessed, 3)
                    }
                }
            });

            DiagnosticsServiceRow checkIn = report.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.CheckIn);
            DiagnosticsServiceRow bagDrop = report.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.BagDrop);

            Check(report.Services.Count == 2,
                "Check-In and Bag Drop are reported as two separate service rows");
            Check(checkIn.Expected == 2 && checkIn.Processed == 2,
                $"Check-In serves Counter passengers only ({checkIn.Expected}/{checkIn.Processed})");
            Check(checkIn.BagDrop == null,
                "The Check-In row no longer carries a Bag Drop subset");
            Check(checkIn.Status == DiagnosticStatus.Pass,
                "Check-In passes when its Counter records are served");
            Check(bagDrop.Expected == 3 && bagDrop.Processed == 3,
                $"Bag Drop serves the Web bag carriers ({bagDrop.Expected}/{bagDrop.Processed})");
            Check(bagDrop.BagDrop != null &&
                  bagDrop.BagDrop.Expected == 3 &&
                  bagDrop.BagDrop.Recorded == 3 &&
                  bagDrop.BagDrop.Missing == 0,
                "The Bag Drop row reports 3 expected, 3 recorded and no missing visit");
            Check(bagDrop.BagDrop.Channels.Any(c => c.Label == "Web" && c.Expected == 3),
                "The Bag Drop breakdown names the Web primary channel");
            Check(!report.Findings.Any(f => f.Code == "SVC-013" || f.Code == "SVC-014"),
                "Routing findings are no longer raised because Bag Drop is not routed");
            Check(bagDrop.PassengerMix.Any(m => m.Label == "Online + bag" && m.Expected == 3),
                "The Bag Drop mix reports online passengers with a checked bag");
            Check(checkIn.PassengerMix.Any(m => m.Label == "Offline + bag" && m.Expected == 2),
                "The Check-In mix reports offline passengers with a checked bag");
            Check(checkIn.Exceptions.Count == 0,
                "No late or transfer-rule exception is invented for the fixture");

            // Bag Drop can be audited from history alone, without a queue.
            DiagnosticsReport historySource = DiagnosticsCalculator.Calculate(
                new DiagnosticsInput
                {
                    Passengers = passengers,
                    Services = new List<DiagnosticsServiceInput>
                    {
                        new DiagnosticsServiceInput
                        {
                            Kind = DiagnosticsServiceKind.CheckIn,
                            Label = "Check-In",
                            Queue = DiagQueue(passengers, checkInProcessed, 2)
                        },
                        new DiagnosticsServiceInput
                        {
                            Kind = DiagnosticsServiceKind.BagDrop,
                            Label = "Bag Drop",
                            Passengers = bagDropProcessed
                        }
                    }
                });

            DiagnosticsServiceRow historyBagDrop = historySource.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.BagDrop);

            Check(historyBagDrop.Processed == 3 &&
                  historyBagDrop.BagDrop.Recorded == 3,
                "Bag Drop history alone proves the standalone visits");
            Check(historyBagDrop.Notes.Any(n =>
                    n.Contains("history", StringComparison.OrdinalIgnoreCase)),
                "The history-derived row explains its source");

            // A processed Bag Drop record without Bag Drop history is missing.
            List<Passenger> brokenBagDrop = bagDropProcessed
                .Where(p => p.Id != passengers[1].Id)
                .ToList();

            Passenger servedClone = bagDropProcessed
                .Single(p => p.Id == passengers[1].Id)
                .Clone();
            servedClone.History.Remove(FacilityType.BagDrop);
            brokenBagDrop.Add(servedClone);

            DiagnosticsReport broken = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.BagDrop,
                        Label = "Bag Drop",
                        Queue = DiagQueue(passengers, brokenBagDrop, 3)
                    }
                }
            });

            DiagnosticsServiceRow brokenBagDropRow = broken.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.BagDrop);

            Check(brokenBagDropRow.Expected == 3 && brokenBagDropRow.Processed == 3,
                "The broken fixture still serves every Bag Drop record");
            Check(brokenBagDropRow.BagDrop.Expected == 3 &&
                  brokenBagDropRow.BagDrop.Missing == 1,
                $"A record without Bag Drop history is missing ({brokenBagDropRow.BagDrop.Expected}/{brokenBagDropRow.BagDrop.Missing})");
            Check(brokenBagDropRow.BagDrop.MissingMix.Any(m =>
                    m.Label == "Online + bag" && m.Count == 1),
                "The Bag Drop mix explains the missing visit");
            Check(brokenBagDropRow.Status == DiagnosticStatus.Fail,
                "A missing Bag Drop history fails the Bag Drop row");
            Check(broken.Findings.Any(f => f.Code == "SVC-014" &&
                    f.ServiceLabel == "Bag Drop"),
                "SVC-014 names the processed Bag Drop record without history");
            Check(broken.PassengerIssues.Any(p =>
                    p.PassengerId == passengers[1].Id &&
                    p.Code == "PAX-SVC-014"),
                "PAX-SVC-014 keeps the missing Bag Drop passenger ID");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------

        private static List<Passenger> DiagOriginPassengers(int count)
        {
            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < count; i++)
            {
                passengers.Add(new Passenger
                {
                    Id = 1000 + i,
                    Channel = "Counter",
                    DHPType = DHPType.DomesticDeparture,
                    PassengerType = PassengerType.Domestic,
                    Journey = PassengerJourney.Origin,
                    ProfileName = "DIAG",
                    UsesOnlineCheckIn = false,
                    HasCheckedBag = true,
                    ArrivalTime = 10.0 + i,
                    QueueArrivalTime = 10.0 + i,
                    ScheduledDepartureTime = 700.0 + i,
                    FlightLegId = "L" + i,
                    Airline = "AP",
                    FlightNumber = (100 + i).ToString()
                });
            }

            return passengers;
        }

        private static Passenger DiagTransferPassenger(
            int id,
            DHPType dhp,
            PassengerType type,
            TransferScenario scenario,
            TransferScenarioRules rules,
            double departureMinute)
        {
            return new Passenger
            {
                Id = id,
                Channel = "Counter",
                DHPType = dhp,
                PassengerType = type,
                Journey = PassengerJourney.Transfer,
                TransferScenario = scenario,
                TransferProcessing = rules?.Clone(),
                ProfileName = "DIAG",
                UsesOnlineCheckIn = false,
                HasCheckedBag = true,
                ArrivalTime = departureMinute - 90.0,
                QueueArrivalTime = departureMinute - 90.0,
                ScheduledDepartureTime = departureMinute,
                ScheduledArrivalTime = departureMinute - 120.0,
                FlightLegId = "T" + id,
                Airline = "AP",
                FlightNumber = id.ToString()
            };
        }

        private static TransferScenarioRules DiagRules(
            bool checkIn = false,
            bool bagDrop = false,
            bool security = false)
        {
            return new TransferScenarioRules
            {
                Scenario = TransferScenario.DomesticToDomestic,
                HasProcessingRules = true,
                MinimumConnectionMinutes = 0.0,
                MaximumConnectionMinutes = 1440.0,
                RequiresCheckIn = checkIn,
                RequiresBagDrop = bagDrop,
                RequiresSecurityScreening = security,
                RequiresImmigration = false,
                RequiresEmigration = false,
                RequiresTransferDesk = false,
                RequiresBaggageRecheck = false,
                SecurityRouting = TransferFacilityRouting.Integrated,
                ImmigrationRouting = TransferFacilityRouting.Integrated,
                EmigrationRouting = TransferFacilityRouting.Integrated
            };
        }

        private static Passenger DiagWithHistory(Passenger source, FacilityType facility)
        {
            Passenger passenger = source.Clone();

            passenger.History[facility] = new FacilityRecord
            {
                ArrivalTime = passenger.ArrivalTime,
                StartServiceTime = passenger.ArrivalTime,
                EndServiceTime = passenger.ArrivalTime + 1.0,
                WaitTime = 0.0,
                AssignedResource = 0
            };

            return passenger;
        }

        private static QueueResults DiagQueue(
            List<Passenger> passengers,
            List<Passenger> processed,
            int active)
        {
            return new QueueResults
            {
                Passengers = passengers,
                ProcessedPassengers = processed,
                PhysicalProcessorCount = active,
                ResourcesUsed = active,
                PeakQueueLength = 1,
                AverageWait = 0.5,
                MaxWait = 2.0,
                Wait95 = 1.5,
                PoolResults = new List<QueuePoolResult>
                {
                    new QueuePoolResult
                    {
                        Name = "Shared",
                        IsShared = true,
                        ActiveResources = active,
                        PassengerCount = processed.Count(p => !p.IsWarmUp)
                    }
                }
            };
        }
    }
}
