using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.Transfer;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunTransferFacilityTests()
        {
            Console.WriteLine("== Transfer desk and baggage recheck facilities ==");

            RunTransferDeskSelectionTests();
            RunTransferDeskPoolRoutingTests();
            RunTransferDeskSharedPoolTests();
            RunTransferDeskShareHistoryTests();
            RunBaggageRecheckTests();
            RunTransferFlowPoolHelperTests();
        }

        // -------------------------------------------------------------
        // Selection: arrivals only, recheck substitution, bypass handling
        // -------------------------------------------------------------
        private static void RunTransferDeskSelectionTests()
        {
            /* Three arrival-leg transfers (two desk flows, one recheck flow),
               one departure-leg transfer whose rules require the desk and one
               originating departure. The desk serves the two desk flows; the
               recheck flow queues at the recheck facility (TS mapping). */
            TransferScenarioRules deskRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic);
            deskRules.RequiresTransferDesk = true;

            TransferScenarioRules recheckRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic);
            recheckRules.RequiresBaggageRecheck = true;

            List<Passenger> passengers = new List<Passenger>
            {
                TransferArrival(10, deskRules, TransferScenario.DomesticToDomestic),
                TransferArrival(11, deskRules, TransferScenario.DomesticToInternational),
                TransferArrival(12, recheckRules, TransferScenario.InternationalToDomestic),
                new Passenger
                {
                    Id = 20,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    Journey = PassengerJourney.Transfer,
                    TransferScenario = TransferScenario.InternationalToInternational,
                    TransferProcessing = deskRules.Clone(),
                    PassengerType = PassengerType.International,
                    ProfileName = "Transfer"
                },
                OriginDeparture(30, international: true)
            };

            TransferDeskSolver solver = new TransferDeskSolver();
            TransferDeskResults result = solver.Solve(new TransferDeskInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(result.QueueResults.ProcessedPassengers.Count == 2,
                $"The desk serves only the two desk-flow arrival transfers ({result.QueueResults.ProcessedPassengers.Count})");
            Check(result.ServedCount == 2 && result.BypassedCount == 1,
                "The desk reports two served transfers and the recheck flow's explicit no-desk bypass");

            List<Passenger> output = result.QueueResults.Passengers;
            Check(output.Count == passengers.Count,
                "The desk returns the full population");

            Passenger notServedOrigin = output[4];
            Check(notServedOrigin.History.Count == 0 &&
                  notServedOrigin.ArrivalTime == passengers[4].ArrivalTime,
                "The originating departure passes through the desk untouched");

            /* A departure-leg transfer whose rules require the desk is never
               queued: the desk applies to the arrival leg only. */
            Check(output[3].History.Count == 0 &&
                  output[3].ArrivalTime == passengers[3].ArrivalTime,
                "A departure-leg transfer requiring the desk is not queued");
            Check(solver.Warnings.Any(w => w.Contains("require both")) == false,
                "No recheck overlap warning when the rules follow the TS mapping");

            /* The recheck-required arrival owes its desk element to the recheck
               facility: the desk queue skips it, and the recheck queue serves it. */
            Check(!output[2].History.ContainsKey(FacilityType.TransferDesk) &&
                  output[2].History.ContainsKey(FacilityType.BaggageRecheck) == false,
                "The recheck arrival passes through the desk unserved");

            BaggageRecheckSolver recheckSolver = new BaggageRecheckSolver();
            BaggageRecheckResults recheck = recheckSolver.Solve(new BaggageRecheckInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(recheck.QueueResults.ProcessedPassengers.Count == 1,
                $"The recheck queue serves the recheck-flow arrival ({recheck.QueueResults.ProcessedPassengers.Count})");

            /* Both flags set on one rules object signals a misconfiguration:
               the desks warn, apply the TS substitution, and the recheck queue
               still serves the passenger exactly once. */
            TransferScenarioRules bothRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic);
            bothRules.RequiresTransferDesk = true;
            bothRules.RequiresBaggageRecheck = true;

            TransferDeskSolver overlapSolver = new TransferDeskSolver();
            TransferDeskResults overlap = overlapSolver.Solve(new TransferDeskInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferArrival(40, bothRules, TransferScenario.InternationalToDomestic)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false
            });
            Check(overlap.QueueResults.ProcessedPassengers.Count == 0 &&
                  overlapSolver.Warnings.Any(w => w.Contains("require both")),
                "The desk warns and applies recheck substitution for an overlapping flow");

            BaggageRecheckSolver overlapRecheck = new BaggageRecheckSolver();
            BaggageRecheckResults overlapRecheckResult = overlapRecheck.Solve(new BaggageRecheckInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferArrival(41, bothRules, TransferScenario.InternationalToDomestic)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false
            });
            Check(overlapRecheckResult.QueueResults.ProcessedPassengers.Count == 1,
                "The recheck queue serves a flow whose rules set both flags exactly once");

            /* A transfer whose rules explicitly say no desk (a scenario-rule
               bypass) is not queued and is counted as bypassed. */
            TransferScenarioRules bypassRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic);
            TransferDeskSolver bypassSolver = new TransferDeskSolver();
            TransferDeskResults bypass = bypassSolver.Solve(new TransferDeskInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferArrival(50, bypassRules, TransferScenario.DomesticToDomestic)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false
            });
            Check(bypass.QueueResults.ProcessedPassengers.Count == 0 &&
                  bypass.BypassedCount == 1,
                "An explicit no-desk scenario rule excludes the transfer and counts as bypassed");

            /* A transfer with no processing rules keeps normal passenger
               behaviour at the desk: not queued, never counted as a bypass. */
            Passenger unspecified = new Passenger
            {
                Id = 51,
                Channel = "Arrival",
                DHPType = DHPType.DomesticArrival,
                Journey = PassengerJourney.Transfer,
                TransferScenario = TransferScenario.DomesticToDomestic,
                TransferProcessing = null,
                PassengerType = PassengerType.Domestic,
                ProfileName = "Transfer",
                ArrivalTime = 51 * 0.2,
                QueueArrivalTime = 51 * 0.2
            };

            TransferDeskSolver unspecifiedSolver = new TransferDeskSolver();
            TransferDeskResults unspecifiedResult = unspecifiedSolver.Solve(new TransferDeskInputs
            {
                Passengers = new List<Passenger> { unspecified },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false
            });
            Check(unspecifiedResult.QueueResults.ProcessedPassengers.Count == 0 &&
                  unspecifiedResult.BypassedCount == 0 &&
                  unspecifiedResult.QueueResults.Passengers[0].History.Count == 0,
                "An unspecified transfer keeps normal arrival behaviour outside the desk");
        }

        // -------------------------------------------------------------
        // Per-flow pools: names, routing, restore, per-pool plans
        // -------------------------------------------------------------
        private static void RunTransferDeskPoolRoutingTests()
        {
            TransferScenarioRules deskRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic);
            deskRules.RequiresTransferDesk = true;

            TransferScenarioRules intlDeskRules = NewRoutingRules(
                TransferScenario.DomesticToInternational);
            intlDeskRules.RequiresTransferDesk = true;

            List<Passenger> passengers = new List<Passenger>();
            for (int i = 0; i < 3; i++)
                passengers.Add(TransferArrival(60 + i, deskRules, TransferScenario.DomesticToDomestic));
            for (int i = 0; i < 4; i++)
                passengers.Add(TransferArrival(70 + i, intlDeskRules, TransferScenario.DomesticToInternational));
            passengers.Add(OriginDeparture(80));

            string domPool = TransferFlowPools.FlowChannelName(
                TransferFlowPools.DeskPoolPrefix, TransferScenario.DomesticToDomestic);
            string intlPool = TransferFlowPools.FlowChannelName(
                TransferFlowPools.DeskPoolPrefix, TransferScenario.DomesticToInternational);

            TransferDeskSolver solver = new TransferDeskSolver();
            TransferDeskResults result = solver.Solve(new TransferDeskInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = true,
                PhysicalResources = new List<int> { 1, 1 },
                TargetWait95 = new List<double> { 5.0, 5.0 },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(result.PoolNames.Contains(domPool) && result.PoolNames.Contains(intlPool),
                "Per-flow pools are named 'Transfer Desk (flow)' in flow order");
            Check(result.PhysicalPerChannel[result.PoolNames.IndexOf(domPool)] == 1 &&
                  result.PhysicalPerChannel[result.PoolNames.IndexOf(intlPool)] == 1,
                "Each flow pool reports the broadcast position count");

            List<Passenger> output = result.QueueResults.Passengers;
            List<Passenger> domServed = output
                .Where(p => p.TransferScenario == TransferScenario.DomesticToDomestic &&
                            p.History.ContainsKey(FacilityType.TransferDesk))
                .ToList();
            List<Passenger> intlServed = output
                .Where(p => p.TransferScenario == TransferScenario.DomesticToInternational &&
                            p.History.ContainsKey(FacilityType.TransferDesk))
                .ToList();

            Check(domServed.Count == 3 && intlServed.Count == 4,
                "Each desk flow is served inside its own pool");
            Check(domServed.All(p => p.AssignedPool == domPool) &&
                  intlServed.All(p => p.AssignedPool == intlPool),
                "Each flow pool tags the passengers it served");

            Check(domServed.All(p => p.Channel == "Arrival") &&
                  intlServed.All(p => p.Channel == "Arrival") &&
                  output.All(p => p.Channel == "Arrival" || p.Id == 80),
                "Per-flow routing restores the persistent channel before output");
            Check(domServed.All(p =>
                    p.History[FacilityType.TransferDesk].EndServiceTime >
                    p.History[FacilityType.TransferDesk].ArrivalTime),
                "Served transfers advance to the end of their service");

            /* Pool plans keyed by the flow names override the broadcast. */
            string planDomPool = domPool;
            TransferDeskSolver plannedSolver = new TransferDeskSolver();
            TransferDeskResults planned = plannedSolver.Solve(new TransferDeskInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = true,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = planDomPool, PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = intlPool, PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) }
                },
                OptimizeResources = false,
                Seed = 9,
                StaffingPercentage = 100.0
            });

            Check(planned.PhysicalPerChannel[planned.PoolNames.IndexOf(planDomPool)] == 2 &&
                  planned.PhysicalPerChannel[planned.PoolNames.IndexOf(intlPool)] == 1,
                "Keyed pool plans override individual flow pools by name");
        }

        // -------------------------------------------------------------
        // Shared pool: one queue for every desk-required transfer
        // -------------------------------------------------------------
        private static void RunTransferDeskSharedPoolTests()
        {
            TransferScenarioRules domRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic);
            domRules.RequiresTransferDesk = true;

            TransferScenarioRules intlRules = NewRoutingRules(
                TransferScenario.DomesticToInternational);
            intlRules.RequiresTransferDesk = true;

            List<Passenger> passengers = new List<Passenger>
            {
                TransferArrival(90, domRules, TransferScenario.DomesticToDomestic),
                TransferArrival(91, intlRules, TransferScenario.DomesticToInternational)
            };

            TransferDeskSolver solver = new TransferDeskSolver();
            TransferDeskResults result = solver.Solve(new TransferDeskInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false,
                Seed = 3,
                StaffingPercentage = 100.0
            });

            Check(result.PoolNames.SequenceEqual(new[] { "Shared" }),
                "Shared pools run one 'Shared' desk queue");
            Check(result.QueueResults.ProcessedPassengers.Count == 2 &&
                  result.QueueResults.ProcessedPassengers.All(p => p.AssignedPool == "Shared"),
                "Both flows are served by the shared pool");
        }

        // -------------------------------------------------------------
        // History, diagnostics keys and repeat-service guard
        // -------------------------------------------------------------
        private static void RunTransferDeskShareHistoryTests()
        {
            TransferScenarioRules deskRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic);
            deskRules.RequiresTransferDesk = true;

            Passenger arrival = TransferArrival(100, deskRules, TransferScenario.DomesticToDomestic);
            arrival.ArrivalTime = 5.0;
            arrival.QueueArrivalTime = 5.0;

            List<Passenger> firstRun = new List<Passenger> { arrival, OriginDeparture(101) };

            TransferDeskSolver solver = new TransferDeskSolver();
            TransferDeskResults first = solver.Solve(new TransferDeskInputs
            {
                Passengers = firstRun,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false,
                Seed = 2,
                StaffingPercentage = 100.0
            });

            Check(first.QueueResults.ProcessedPassengers.All(p =>
                    p.History.ContainsKey(FacilityType.TransferDesk)),
                "Desk service writes a TransferDesk history record");

            FacilityRecord record = first.QueueResults.ProcessedPassengers[0]
                .History[FacilityType.TransferDesk];
            Check(record.EndServiceTime >= record.StartServiceTime &&
                  record.WaitTime >= 0.0,
                "The history record carries the timing of the visit");

            /* Running the same population through the desk twice must not
               produce two desk records: the already-served passenger passes
               through, mirroring the BagDrop repeated-facility rule. */
            List<Passenger> secondRun = new List<Passenger>
            {
                first.QueueResults.Passengers[0],
                OriginDeparture(102)
            };

            TransferDeskSolver secondSolver = new TransferDeskSolver();
            TransferDeskResults second = secondSolver.Solve(new TransferDeskInputs
            {
                Passengers = secondRun,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false,
                Seed = 2,
                StaffingPercentage = 100.0
            });

            Check(second.QueueResults.ProcessedPassengers.Count == 0,
                "A transfer that already carries a desk record is not served again");
        }

        // -------------------------------------------------------------
        // Recheck facility: selection, per-flow pool naming, history
        // -------------------------------------------------------------
        private static void RunBaggageRecheckTests()
        {
            TransferScenarioRules recheckRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic);
            recheckRules.RequiresBaggageRecheck = true;

            List<Passenger> passengers = new List<Passenger>
            {
                TransferArrival(110, recheckRules, TransferScenario.InternationalToDomestic),
                OriginDeparture(111, international: true)
            };

            BaggageRecheckSolver solver = new BaggageRecheckSolver();
            BaggageRecheckResults result = solver.Solve(new BaggageRecheckInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = true,
                OptimizeResources = false,
                Seed = 4,
                StaffingPercentage = 100.0
            });

            string recheckPool = TransferFlowPools.FlowChannelName(
                TransferFlowPools.RecheckPoolPrefix, TransferScenario.InternationalToDomestic);

            Check(result.PoolNames.SequenceEqual(new[] { recheckPool }),
                "The recheck pool follows the flow that needs it");
            Check(result.QueueResults.ProcessedPassengers.Count == 1 &&
                  result.QueueResults.ProcessedPassengers[0].History.ContainsKey(FacilityType.BaggageRecheck),
                "The recheck-required arrival is served and recorded");
            Check(result.ServedCount == 1 && result.BypassedCount == 0,
                "The recheck facility reports one served and no bypassed transfers");

            /* An unspecified arrival keeps normal behaviour: the recheck
               service requires an explicit rule. */
            BaggageRecheckSolver unspecifiedSolver = new BaggageRecheckSolver();
            BaggageRecheckResults unspecified = unspecifiedSolver.Solve(new BaggageRecheckInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferArrival(120, NewRoutingRules(TransferScenario.InternationalToDomestic),
                        TransferScenario.InternationalToDomestic)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                SplitPoolsPerFlow = false,
                OptimizeResources = false
            });
            Check(unspecified.QueueResults.ProcessedPassengers.Count == 0 &&
                  unspecified.QueueResults.Passengers[0].History.Count == 0,
                "An unspecified transfer is not queued at recheck");
        }

        // -------------------------------------------------------------
        // Flow-pool helper: naming, channel collection, overlap warning
        // -------------------------------------------------------------
        private static void RunTransferFlowPoolHelperTests()
        {
            Check(TransferFlowPools.FlowChannelName(
                      TransferFlowPools.DeskPoolPrefix, TransferScenario.DomesticToDomestic) ==
                  "Transfer Desk (Domestic to domestic)",
                "Flow channel names read 'prefix (flow label)'");

            TransferScenarioRules deskRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic);
            deskRules.RequiresBaggageRecheck = true;

            List<Passenger> passengers = new List<Passenger>
            {
                TransferArrival(130, deskRules, TransferScenario.InternationalToDomestic)
            };

            List<string> channels = TransferFlowPools.PresentFlowChannels(
                passengers,
                p => TransferServiceUsageCalculator.UsesService(p, TransferService.BaggageRecheck),
                TransferFlowPools.RecheckPoolPrefix,
                splitPoolsPerFlow: true);

            Check(channels.SequenceEqual(new[]
                {
                    TransferFlowPools.FlowChannelName(
                        TransferFlowPools.RecheckPoolPrefix, TransferScenario.InternationalToDomestic)
                }),
                "Only flows with served passengers produce pool channels");

            Check(TransferFlowPools.PresentFlowChannels(
                passengers,
                p => false,
                TransferFlowPools.RecheckPoolPrefix,
                splitPoolsPerFlow: true).Count == 0,
                "A population with no served passengers produces no flow channels");

            List<string> shared = TransferFlowPools.PresentFlowChannels(
                passengers,
                p => true,
                TransferFlowPools.RecheckPoolPrefix,
                splitPoolsPerFlow: false);
            Check(shared.Count == 0,
                "Shared pools bypass flow-channel creation");

            List<string> warnings = new List<string>();
            TransferFlowPools.WarnOnDeskAndRecheckOverlap(
                new List<Passenger>
                {
                    TransferArrival(131, NewRoutingRules(
                        TransferScenario.InternationalToDomestic),
                        TransferScenario.InternationalToDomestic)
                },
                warnings.Add);
            Check(warnings.Count == 0,
                "No overlap warning for rules that do not set both flags");

            TransferScenarioRules bothRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic);
            bothRules.RequiresTransferDesk = true;
            bothRules.RequiresBaggageRecheck = true;

            List<string> overlapWarnings = new List<string>();
            TransferFlowPools.WarnOnDeskAndRecheckOverlap(
                new List<Passenger>
                {
                    TransferArrival(132, bothRules, TransferScenario.InternationalToDomestic)
                },
                overlapWarnings.Add);
            Check(overlapWarnings.Any(w => w.Contains("require both")),
                "The overlap warning fires when both flags are set");
        }
    }
}
