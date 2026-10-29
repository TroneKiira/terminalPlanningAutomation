using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities.TerminalEntry;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunTransferDemandTests()
        {
            Console.WriteLine("== Transfer demand and schedule caps ==");

            TransferDemandProfile profile = NewTransferTestProfile();
            List<string> validation = profile.GetValidationErrors();
            Check(validation.Count == 0,
                "Transfer profile accepts valid market shares and outbound allocation sums");

            profile.InternationalToDomesticPercent = 45.0;
            Check(profile.GetValidationErrors().Any(e => e.Contains("Domestic-outbound")),
                "Domestic outbound scenario allocation cannot exceed its parent transfer total");
            profile.InternationalToDomesticPercent = 40.0;

            TransferDemandResult php = TransferDemandCalculator.CalculatePhp(1000, 800, profile);
            Check(php.RequestedDomesticTransferPassengers == 100,
                $"PHP Domestic transfer demand is 10% of 1000 ({php.RequestedDomesticTransferPassengers})");
            Check(php.RequestedInternationalTransferPassengers == 200,
                $"PHP International transfer demand is 25% of 800 ({php.RequestedInternationalTransferPassengers})");
            Check(php.ByScenario[TransferScenario.DomesticToDomestic].RequestedPassengers == 60,
                "Domestic→Domestic allocates 60% of the Domestic transfer total");
            Check(php.ByScenario[TransferScenario.InternationalToDomestic].RequestedPassengers == 40,
                "International→Domestic allocates 40% of the Domestic transfer total");
            Check(php.ByScenario[TransferScenario.DomesticToInternational].RequestedPassengers == 50,
                "Domestic→International allocates 25% of the International transfer total");
            Check(php.ByScenario[TransferScenario.InternationalToInternational].RequestedPassengers == 150,
                "International→International allocates 75% of the International transfer total");

            TransferDemandProfile full = new TransferDemandProfile
            {
                DomesticTransferPercent = 100.0,
                InternationalTransferPercent = 100.0,
                DomesticToDomesticPercent = 50.0,
                InternationalToDomesticPercent = 50.0,
                DomesticToInternationalPercent = 50.0,
                InternationalToInternationalPercent = 50.0,
                ScheduleEligibility = TransferScheduleEligibility.ViaQualifiedLegs
            };
            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                full.ScenarioRules[scenario].MinimumConnectionMinutes = 30.0;
                full.ScenarioRules[scenario].MaximumConnectionMinutes = 240.0;
            }

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Transfer caps",
                Legs = new List<FlightLeg>
                {
                    MakeTransferLeg("ARR-D", LegDirection.Arrival, 600, 70, LegSector.Domestic, "LHR", "DEL"),
                    MakeTransferLeg("ARR-I", LegDirection.Arrival, 630, 40, LegSector.International, "LHR", "DXB"),
                    MakeTransferLeg("DEP-D", LegDirection.Departure, 720, 50, LegSector.Domestic, "SIN", "BOM"),
                    MakeTransferLeg("DEP-I", LegDirection.Departure, 740, 60, LegSector.International, "SIN", "DOH")
                }
            };

            TransferScheduleCapacity capacity =
                TransferDemandCalculator.CalculateScheduleCapacity(schedule, full);
            Check(capacity.ByScenario[TransferScenario.DomesticToDomestic] == 50,
                $"D→D feasible flow is limited by the 50-seat departure leg ({capacity.ByScenario[TransferScenario.DomesticToDomestic]})");
            Check(capacity.ByScenario[TransferScenario.InternationalToInternational] == 40,
                $"I→I feasible flow is limited by 40 eligible international arrivals ({capacity.ByScenario[TransferScenario.InternationalToInternational]})");
            Check(capacity.DomesticOutbound == 50 && capacity.InternationalOutbound == 60,
                "Schedule maximums respect each outbound market's passenger capacity");

            TransferDemandResult scheduleDemand =
                TransferDemandCalculator.CalculateSchedule(schedule, full);
            Check(scheduleDemand.DomesticDeparturePeak == 50 && scheduleDemand.InternationalDeparturePeak == 60,
                "Design Day transfer shares use schedule-derived rolling departure peaks");
            Check(scheduleDemand.EffectiveDomesticTransferPassengers <= capacity.DomesticOutbound,
                "Effective Domestic transfer demand does not exceed feasible scheduled connections");
            Check(scheduleDemand.EffectiveInternationalTransferPassengers <= capacity.InternationalOutbound,
                "Effective International transfer demand does not exceed feasible scheduled connections");
            Check(scheduleDemand.Warnings.Count > 0,
                "Schedule capacity shortfalls are reported instead of silently accepted");
            Check(capacity.DomesticDeparturePeakShareCap(scheduleDemand.DomesticDeparturePeak) <= 100.0 &&
                  capacity.InternationalDeparturePeakShareCap(scheduleDemand.InternationalDeparturePeak) <= 100.0,
                "Schedule-derived slider caps are percentages of their corresponding departure peaks");

            /* The acceptance case: transfer shares apply to the whole operating
               day, while the rolling peak remains a separate reporting value. */
            FlightScheduleResult dailySchedule = new FlightScheduleResult
            {
                ScheduleName = "Full-day transfer acceptance"
            };
            for (int i = 0; i < 25; i++)
            {
                dailySchedule.Legs.Add(MakeTransferLeg(
                    $"DAILY-DEP-D-{i}", LegDirection.Departure, 60 + i * 45, 1000,
                    LegSector.Domestic, "SIN", "BOM"));
            }
            for (int i = 0; i < 15; i++)
            {
                dailySchedule.Legs.Add(MakeTransferLeg(
                    $"DAILY-DEP-I-{i}", LegDirection.Departure, 60 + i * 60, 1000,
                    LegSector.International, "SIN", "DOH"));
            }
            dailySchedule.Legs.Add(MakeTransferLeg(
                "DAILY-ARR-D", LegDirection.Arrival, 0, 25000,
                LegSector.Domestic, "LHR", "DEL"));
            dailySchedule.Legs.Add(MakeTransferLeg(
                "DAILY-ARR-I", LegDirection.Arrival, 0, 15000,
                LegSector.International, "LHR", "DXB"));

            TransferDemandProfile dailyProfile = new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                InternationalTransferPercent = 10.0,
                DomesticToDomesticPercent = 100.0,
                InternationalToInternationalPercent = 100.0
            };

            TransferDemandResult dailyDemand =
                TransferDemandCalculator.CalculateSchedule(dailySchedule, dailyProfile);
            Check(dailyDemand.DomesticDepartureTotal == 25000 &&
                  dailyDemand.InternationalDepartureTotal == 15000,
                $"Design Day basis is the full-day departure market ({dailyDemand.DomesticDepartureTotal} + {dailyDemand.InternationalDepartureTotal})");
            Check(dailyDemand.RequestedDomesticTransferPassengers == 2500 &&
                  dailyDemand.RequestedInternationalTransferPassengers == 1500,
                $"10% of 25,000 Domestic and 15,000 International daily departures requests 2,500 + 1,500 transfers ({dailyDemand.RequestedDomesticTransferPassengers} + {dailyDemand.RequestedInternationalTransferPassengers})");
            Check(dailyDemand.EffectiveTransferPassengers == 4000,
                $"The acceptance case produces 4,000 full-day outbound transfers ({dailyDemand.EffectiveTransferPassengers})");
            Check(dailyDemand.DomesticDeparturePeak < dailyDemand.DomesticDepartureTotal &&
                  dailyDemand.DomesticDepartureTransferPeak > 0 &&
                  dailyDemand.DomesticDepartureTransferPeak < dailyDemand.EffectiveDomesticTransferPassengers,
                $"Peak-hour transfer reporting remains separate from the daily total (peak {dailyDemand.DomesticDepartureTransferPeak})");
            Check(dailyDemand.Capacity.DomesticDepartureShareCap(dailyDemand.DomesticDepartureTotal) >= 10.0 &&
                  dailyDemand.Capacity.DomesticDepartureShareCap(dailyDemand.DomesticDepartureTotal) <= 100.0,
                "Design Day slider caps are expressed against daily departure totals");

            FlightScheduleResult tooShort = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakeTransferLeg("ARR-SHORT", LegDirection.Arrival, 700, 50, LegSector.Domestic, "LHR", "DEL"),
                    MakeTransferLeg("DEP-SHORT", LegDirection.Departure, 720, 50, LegSector.Domestic, "SIN", "BOM")
                }
            };
            Check(TransferDemandCalculator.CalculateScheduleCapacity(tooShort, full).Total == 0,
                "Scenario-specific minimum connection time excludes connections that are too short");

            PassengerProfile scheduleProfile = MakeScheduleProfile(
                "Transfer test", 100.0, 60.0, 120.0, ShowUpProfile.Mix());
            TransferScenarioRules ddRules =
                full.ScenarioRules[TransferScenario.DomesticToDomestic];
            ddRules.HasProcessingRules = true;
            ddRules.RequiresCheckIn = false;
            ddRules.RequiresBagDrop = false;
            ddRules.RequiresSecurityScreening = true;
            ddRules.RequiresImmigration = false;
            ddRules.RequiresEmigration = false;
            ddRules.RequiresTransferDesk = false;
            ddRules.RequiresBaggageRecheck = true;

            List<PassengerGroup> scheduleGroups = new List<PassengerGroup>
            {
                MakeTypedScheduleGroup(DHPType.DomesticDeparture, scheduleProfile),
                MakeTypedScheduleGroup(DHPType.DomesticArrival, scheduleProfile),
                MakeTypedScheduleGroup(DHPType.InternationalDeparture, scheduleProfile),
                MakeTypedScheduleGroup(DHPType.InternationalArrival, scheduleProfile)
            };
            SchedulePassengerGenerationResult generated = new SchedulePassengerGenerator().Generate(
                schedule,
                scheduleGroups,
                new AirportPlanning.Core.Walking.WalkingProfile
                {
                    AverageSpeed = 1.35,
                    StandardDeviation = 0.0
                },
                seed: 4,
                arrivalDelay: new ArrivalProcessDelay(0.0, 0.0),
                transferDemand: full);

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                int departureSegments = generated.Passengers.Count(p =>
                    p.DHPType == (scenario.IsDomesticDeparture()
                        ? DHPType.DomesticDeparture
                        : DHPType.InternationalDeparture) &&
                    p.TransferScenario == scenario);
                Check(departureSegments == generated.TransferDemand.ByScenario[scenario].EffectivePassengers,
                    $"Schedule generation tags {scenario.ToFlowLabel()} departure cohorts without reusing passenger slots ({departureSegments})");
            }

            Check(generated.Passengers.Any(p => p.DHPType == DHPType.DomesticArrival && p.Journey == PassengerJourney.Transfer),
                "Schedule generation also marks via-qualified inbound transfer cohorts");

            int ddDepartureTransfers = generated.Passengers.Count(p =>
                p.DHPType == DHPType.DomesticDeparture &&
                p.TransferScenario == TransferScenario.DomesticToDomestic);
            int ddArrivalTransfers = generated.Passengers.Count(p =>
                p.DHPType == DHPType.DomesticArrival &&
                p.TransferScenario == TransferScenario.DomesticToDomestic);
            TransferServiceUsage dailyCheckIn =
                generated.TransferDemand.ServiceUsage[TransferService.CheckIn];
            TransferServiceUsage dailySecurity =
                generated.TransferDemand.ServiceUsage[TransferService.Security];
            TransferServiceUsage dailyDesk =
                generated.TransferDemand.ServiceUsage[TransferService.TransferDesk];
            TransferServiceUsage dailyRecheck =
                generated.TransferDemand.ServiceUsage[TransferService.BaggageRecheck];

            Check(generated.TransferDemand.TaggedTransferPassengers ==
                  generated.TransferDemand.EffectiveTransferPassengers,
                "Every schedule-effective outbound transfer is tagged when all groups are present");
            Check(dailyCheckIn.Bypassed == ddDepartureTransfers,
                $"A D→D check-in bypass is counted only for the tagged D→D departure cohort ({dailyCheckIn.Bypassed})");
            Check(dailyCheckIn.Unspecified ==
                  generated.TransferDemand.TaggedTransferPassengers - ddDepartureTransfers,
                "Transfers without explicit processing rules are reported as unspecified, not bypassed");
            Check(dailySecurity.Required == ddDepartureTransfers,
                "A required security rule is counted separately from a bypass");
            Check(dailyDesk.Bypassed == ddArrivalTransfers &&
                  dailyRecheck.Required == ddArrivalTransfers,
                "Arrival-side transfer desk and baggage recheck rules are counted on inbound records");
            Check(generated.TransferDemand.PeakServiceUsage[TransferService.CheckIn].Bypassed ==
                  ddDepartureTransfers &&
                  generated.TransferDemand.PeakServiceUsage[TransferService.TransferDesk].Bypassed ==
                  ddArrivalTransfers,
                "Peak-hour service usage is reported separately using the same scenario rules");

            /* Tagged schedule transfers must not re-enter the public terminal
               through the entry gates. Only originating departures queue there. */
            int originatingDepartures = generated.Passengers.Count(p =>
                p.DHPType == DHPType.DomesticDeparture ||
                p.DHPType == DHPType.InternationalDeparture) -
                generated.TransferDemand.TaggedTransferPassengers;
            TerminalEntryResults entry = new TerminalEntrySolver().Solve(new TerminalEntryInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                DigiYatraSharePercentage = 0.0,
                ManualPhysicalPositions = 1,
                DigiYatraPhysicalPositions = 1,
                OptimizeResources = false,
                Seed = 4
            });

            Check(entry.QueueResults.ProcessedPassengers.Count == originatingDepartures &&
                  entry.QueueResults.ProcessedPassengers.All(p => p.Journey != PassengerJourney.Transfer),
                $"Terminal entry serves originating departures only ({entry.QueueResults.ProcessedPassengers.Count} of {originatingDepartures})");
            Check(entry.TransfersBypassingEntry == generated.TransferDemand.TaggedTransferPassengers,
                $"Every tagged schedule transfer bypasses terminal entry ({entry.TransfersBypassingEntry})");
            Check(entry.QueueResults.Passengers.Count == generated.Passengers.Count,
                "Transfers remain available for downstream facilities after bypassing entry");

            FlightScheduleResult directOnly = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakeTransferLeg("ARR-DIRECT", LegDirection.Arrival, 600, 70, LegSector.Domestic, "COK", "COK"),
                    MakeTransferLeg("DEP-DIRECT", LegDirection.Departure, 720, 70, LegSector.Domestic, "HYD", "HYD")
                }
            };
            Check(TransferDemandCalculator.CalculateScheduleCapacity(directOnly, full).Total == 0,
                "Matching Via and Origin/Destination values exclude direct legs from transfer eligibility");

            /* All-passenger-legs eligibility (the default): a direct inbound
               flight still delivers connecting passengers, so matching runs on
               sectors, connection windows and capacities — Via is not required. */
            TransferDemandProfile allLegs = new TransferDemandProfile
            {
                DomesticTransferPercent = 100.0,
                InternationalTransferPercent = 100.0,
                DomesticToDomesticPercent = 50.0,
                InternationalToDomesticPercent = 50.0,
                DomesticToInternationalPercent = 50.0,
                InternationalToInternationalPercent = 50.0
            };
            Check(allLegs.ScheduleEligibility == TransferScheduleEligibility.AllPassengerLegs,
                "All-passenger-legs eligibility is the default");
            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                allLegs.ScenarioRules[scenario].MinimumConnectionMinutes = 30.0;
                allLegs.ScenarioRules[scenario].MaximumConnectionMinutes = 240.0;
            }

            allLegs.ScheduleEligibility = (TransferScheduleEligibility)7;
            Check(allLegs.GetValidationErrors().Any(e => e.Contains("Schedule eligibility")),
                "Profile validation rejects an out-of-range schedule eligibility value");
            allLegs.ScheduleEligibility = TransferScheduleEligibility.AllPassengerLegs;

            TransferScheduleCapacity allLegsCapacity =
                TransferDemandCalculator.CalculateScheduleCapacity(directOnly, allLegs);
            Check(allLegsCapacity.ByScenario[TransferScenario.DomesticToDomestic] == 70 &&
                  allLegsCapacity.Total == 70,
                $"All-legs eligibility matches direct legs inside the connection window ({allLegsCapacity.Total})");

            SchedulePassengerGenerationResult directGenerated = new SchedulePassengerGenerator().Generate(
                directOnly,
                scheduleGroups,
                new AirportPlanning.Core.Walking.WalkingProfile
                {
                    AverageSpeed = 1.35,
                    StandardDeviation = 0.0
                },
                seed: 7,
                arrivalDelay: new ArrivalProcessDelay(0.0, 0.0),
                transferDemand: allLegs);

            int directDepartureTransfers = directGenerated.Passengers.Count(p =>
                p.DHPType == DHPType.DomesticDeparture &&
                p.Journey == PassengerJourney.Transfer);
            Check(directDepartureTransfers == 35,
                $"All-legs tagging marks the D→D departure cohort on direct legs; I→D stays 0 without an international inbound leg ({directDepartureTransfers})");
            Check(directGenerated.Passengers.Count(p =>
                    p.Journey == PassengerJourney.Transfer) ==
                directGenerated.TransferDemand.ByScenario.Values.Sum(v => v.EffectivePassengers) * 2,
                "Arrival and departure cohorts are both tagged under all-legs eligibility");

            /* Missing Passenger Groups must not be mistaken for scenario-rule
               bypasses. The schedule still allocates the transfer demand, but
               the generator reports that it could not tag the missing cohort. */
            FlightScheduleResult missingGroupSchedule = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakeTransferLeg("MISS-ARR-D", LegDirection.Arrival, 600, 200,
                        LegSector.Domestic, "LHR", "DEL"),
                    MakeTransferLeg("MISS-DEP-D", LegDirection.Departure, 720, 100,
                        LegSector.Domestic, "SIN", "BOM"),
                    MakeTransferLeg("MISS-DEP-I", LegDirection.Departure, 740, 100,
                        LegSector.International, "SIN", "DOH")
                }
            };
            TransferDemandProfile missingProfile = new TransferDemandProfile
            {
                DomesticTransferPercent = 100.0,
                InternationalTransferPercent = 100.0,
                DomesticToDomesticPercent = 100.0,
                DomesticToInternationalPercent = 100.0
            };
            SchedulePassengerGenerationResult missingGenerated =
                new SchedulePassengerGenerator().Generate(
                    missingGroupSchedule,
                    new List<PassengerGroup>
                    {
                        MakeTypedScheduleGroup(DHPType.DomesticArrival, scheduleProfile),
                        MakeTypedScheduleGroup(DHPType.DomesticDeparture, scheduleProfile)
                    },
                    new AirportPlanning.Core.Walking.WalkingProfile
                    {
                        AverageSpeed = 1.35,
                        StandardDeviation = 0.0
                    },
                    seed: 8,
                    arrivalDelay: new ArrivalProcessDelay(0.0, 0.0),
                    transferDemand: missingProfile);

            Check(missingGenerated.TransferDemand.EffectiveTransferPassengers == 200 &&
                  missingGenerated.TransferDemand.TaggedTransferPassengers == 100,
                $"Missing groups reduce tagged transfers ({missingGenerated.TransferDemand.TaggedTransferPassengers} of {missingGenerated.TransferDemand.EffectiveTransferPassengers})");
            Check(missingGenerated.TransferDemand.Warnings.Any(w =>
                    w.Contains("could only tag 100 of 200")),
                "Missing generated cohorts produce a tagging warning");
            Check(missingGenerated.TransferDemand.ServiceUsage[TransferService.CheckIn].Bypassed == 0,
                "Missing cohorts are not counted as scenario-rule service bypasses");

            TransferDemandProfile aggregateProfile = new TransferDemandProfile
            {
                DomesticTransferPercent = 50.0,
                DomesticToDomesticPercent = 100.0
            };
            List<Passenger> population = Enumerable.Range(1, 4)
                .Select(id => new Passenger
                {
                    Id = id,
                    PassengerType = PassengerType.Domestic,
                    DHPType = DHPType.DomesticDeparture,
                    Journey = PassengerJourney.Origin
                })
                .ToList();

            TransferScenarioRules transferRules = aggregateProfile.ScenarioRules[TransferScenario.DomesticToDomestic];
            transferRules.HasProcessingRules = true;
            transferRules.RequiresCheckIn = false;
            transferRules.RequiresBagDrop = false;

            TransferDemandResult tagged = TransferPassengerTagger.ApplyAggregate(population, aggregateProfile);
            Check(tagged.EffectiveDomesticTransferPassengers == 2,
                "Aggregate/PHP tagging creates the configured transfer count");
            Check(population.Count(p => p.Journey == PassengerJourney.Transfer) == 2,
                "Aggregate tagging marks only the allocated cohort as transfers");
            Check(population.Count(p => p.TransferScenario == TransferScenario.DomesticToDomestic) == 2,
                "Aggregate tagging preserves the selected transfer scenario");
            Check(population.Where(p => p.Journey == PassengerJourney.Transfer).All(p => !p.RequiresCheckIn && !p.RequiresBagDrop),
                "Explicit transfer processing rules affect check-in and bag-drop requirements");
            Check(tagged.ServiceUsage[TransferService.CheckIn].Bypassed == 2 &&
                  tagged.ServiceUsage[TransferService.BagDrop].Bypassed == 2,
                "Aggregate tagging reports rule-based check-in and bag-drop bypasses");
            Check(tagged.ServiceUsage[TransferService.Security].Bypassed == 2 &&
                  tagged.ServiceUsage[TransferService.Security].Unspecified == 0,
                "An explicit processing rule that does not require security is reported as a bypass");
            Check(tagged.PeakServiceUsage[TransferService.CheckIn].Bypassed == 2,
                "PHP/aggregate populations are already a peak cohort, so peak usage equals daily usage");

            TransferDemandProfile noRuleProfile = new TransferDemandProfile
            {
                DomesticTransferPercent = 50.0,
                DomesticToDomesticPercent = 100.0
            };
            List<Passenger> noRulePopulation = Enumerable.Range(11, 4)
                .Select(id => new Passenger
                {
                    Id = id,
                    PassengerType = PassengerType.Domestic,
                    DHPType = DHPType.DomesticDeparture,
                    Journey = PassengerJourney.Origin
                })
                .ToList();
            TransferDemandResult noRuleTagged =
                TransferPassengerTagger.ApplyAggregate(noRulePopulation, noRuleProfile);
            Check(noRuleTagged.ServiceUsage[TransferService.CheckIn].Unspecified == 2 &&
                  noRuleTagged.ServiceUsage[TransferService.CheckIn].Bypassed == 0,
                "Tagged transfers without explicit processing rules are unspecified, not bypassed");
            Check(population.First(p => p.Journey == PassengerJourney.Transfer).Clone().TransferScenario == TransferScenario.DomesticToDomestic,
                "Passenger cloning preserves transfer scenario metadata");

            PassengerDemand phpDemand = new PassengerDemand
            {
                DesignHourDemand = 100,
                DeparturePercentage = 100.0,
                DomesticPercentage = 100.0
            };
            PassengerProfile phpProfile = new PassengerProfile
            {
                Name = "PHP profile",
                Percentage = 100.0,
                Journey = PassengerJourney.Origin,
                MaleProbability = 0.5
            };
            TransferDemandProfile phpTransfers = new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                DomesticToDomesticPercent = 100.0
            };
            PassengerGenerationResult phpGenerated = new PassengerGenerator().Generate(
                phpDemand,
                new List<PassengerProfile> { phpProfile },
                new List<PassengerProfile>(),
                new WalkingProfile { AverageSpeed = 1.35, StandardDeviation = 0.0 },
                720,
                120,
                9,
                transferDemand: phpTransfers);
            int phpTagged = phpGenerated.Passengers.Count(p => p.TransferScenario == TransferScenario.DomesticToDomestic);
            int expectedPhpTransfers = (int)Math.Floor(phpGenerated.Passengers.Count * 0.10 + 1e-9);
            Check(phpGenerated.TransferDemand.EffectiveDomesticTransferPassengers == expectedPhpTransfers &&
                  phpTagged == expectedPhpTransfers,
                $"The PHP Passenger Generator applies entered shares (effective {phpGenerated.TransferDemand.EffectiveDomesticTransferPassengers}, tagged {phpTagged}, total {phpGenerated.Passengers.Count})");
        }

        private static TransferDemandProfile NewTransferTestProfile()
        {
            return new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                InternationalTransferPercent = 25.0,
                DomesticToDomesticPercent = 60.0,
                InternationalToDomesticPercent = 40.0,
                DomesticToInternationalPercent = 25.0,
                InternationalToInternationalPercent = 75.0
            };
        }

        private static FlightLeg MakeTransferLeg(
            string id,
            LegDirection direction,
            int minute,
            int pax,
            LegSector sector,
            string station,
            string via)
        {
            return new FlightLeg
            {
                Id = id,
                Direction = direction,
                HasTime = true,
                MinutesFromMidnight = minute,
                PaxRaw = pax,
                SectorRaw = sector,
                ServiceType = LegServiceType.Passenger,
                Station = station,
                Via = via
            };
        }
    }
}
