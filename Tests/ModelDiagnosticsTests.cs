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

            DiagnosticsSizingRow sizing = report.SizingRows.Single();

            Check(sizing.Status == DiagnosticStatus.Fail,
                "Sizing with demand and no processors fails");
            Check(report.Findings.Any(f => f.Code == "SIZE-001"),
                "SIZE-001 explains the missing processors");
            Check(report.Findings.Any(f => f.Code == "SIZE-002"),
                "SIZE-002 reports gross area below net area");
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

            // All three need bag drop (online + checked bag), but the result's
            // only pool serves the BagDrop channel.
            for (int i = 0; i < passengers.Count; i++)
            {
                passengers[i].UsesOnlineCheckIn = true;
                passengers[i].HasCheckedBag = true;
                passengers[i].Channel = i == 0 ? "BagDrop" : "Counter";
            }

            List<Passenger> processed = new List<Passenger>
            {
                DiagWithHistory(passengers[0], FacilityType.BagDrop)
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
                        Name = "BagDrop",
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
                        Name = "BagDrop",
                        Channel = "BagDrop",
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
                        Kind = DiagnosticsServiceKind.BagDrop,
                        Label = "Bag Drop",
                        Queue = queue
                    }
                }
            });

            DiagnosticsServiceRow row = report.Services.Single();

            Check(row.MissingExpected == 2,
                $"Two bag-drop records are outside the planned pool ({row.MissingExpected})");
            Check(report.Findings.Any(f => f.Code == "SVC-011" && f.ServiceLabel == "Bag Drop"),
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
        /// Bag Drop is a channel inside Check-In. Its history is written on
        /// Check-In's processed clones, so the Bag Drop row must find those
        /// visits even when the wired population is the original generated list
        /// and no separate Bag Drop result exists.
        /// </summary>
        private static void RunDiagnosticsIntegratedBagDrop()
        {
            List<Passenger> passengers = DiagOriginPassengers(4);

            for (int i = 0; i < 2; i++)
            {
                passengers[i].Channel = "BagDrop";
                passengers[i].UsesOnlineCheckIn = true;
                passengers[i].HasCheckedBag = true;
            }

            List<Passenger> processed = new List<Passenger>();

            foreach (Passenger passenger in passengers)
            {
                Passenger clone = DiagWithHistory(passenger, FacilityType.CheckIn);

                if (passenger.Channel == "BagDrop" && passenger.HasCheckedBag)
                    clone.History[FacilityType.BagDrop] = new FacilityRecord
                    {
                        ArrivalTime = clone.ArrivalTime,
                        StartServiceTime = clone.ArrivalTime,
                        EndServiceTime = clone.ArrivalTime + 1.0,
                        WaitTime = 0.0,
                        AssignedResource = 0
                    };

                processed.Add(clone);
            }

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
                    },
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.BagDrop,
                        Label = "Bag Drop"
                    }
                }
            });

            DiagnosticsServiceRow checkIn = report.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.CheckIn);
            DiagnosticsServiceRow bagDrop = report.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.BagDrop);

            Check(checkIn.Status == DiagnosticStatus.Pass,
                "Check-In stays PASS with the integrated BagDrop channel");
            Check(bagDrop.Expected == 2 && bagDrop.Processed == 2,
                $"Bag Drop reads history from Check-In's clones ({bagDrop.Expected}/{bagDrop.Processed})");
            Check(bagDrop.Status == DiagnosticStatus.Pass,
                "Bag Drop is PASS when every expected visit carries history");
            Check(bagDrop.Notes.Any(n => n.Contains("channel inside Check-In")),
                "The Bag Drop row explains that it is served inside Check-In");

            List<Passenger> incomplete = processed
                .Where(p => p.Id != passengers[1].Id)
                .ToList();

            DiagnosticsReport broken = DiagnosticsCalculator.Calculate(new DiagnosticsInput
            {
                Passengers = passengers,
                Services = new List<DiagnosticsServiceInput>
                {
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.CheckIn,
                        Label = "Check-In",
                        Queue = DiagQueue(passengers, incomplete, 2)
                    },
                    new DiagnosticsServiceInput
                    {
                        Kind = DiagnosticsServiceKind.BagDrop,
                        Label = "Bag Drop"
                    }
                }
            });

            DiagnosticsServiceRow brokenBagDrop = broken.Services
                .Single(s => s.Kind == DiagnosticsServiceKind.BagDrop);

            Check(brokenBagDrop.Expected == 2 && brokenBagDrop.MissingExpected == 1,
                $"A missing BagDrop history is still reported ({brokenBagDrop.Expected}/{brokenBagDrop.MissingExpected})");
            Check(brokenBagDrop.Status == DiagnosticStatus.Fail,
                "A missing BagDrop history still fails the Bag Drop row");
            Check(broken.Findings.Any(f => f.Code == "SVC-001" &&
                    f.ServiceLabel == "Bag Drop"),
                "SVC-001 names the missing Bag Drop record");
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
