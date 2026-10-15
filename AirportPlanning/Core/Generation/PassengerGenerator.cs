using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Generation
{
    public class PassengerGenerator
    {
        private readonly GaussianArrivalGenerator _arrivalGenerator;

        public PassengerGenerator()
        {
            _arrivalGenerator = new GaussianArrivalGenerator();
        }

        public PassengerGenerationResult Generate(
           PassengerDemand demand,
            List<PassengerProfile> domesticProfiles,
            List<PassengerProfile> internationalProfiles,
            WalkingProfile walkingProfile,
            int departureMinute,
            int horizon,
            int seed,
            IChannelAssigner channelAssigner = null)
        {
            ValidateInputs(
                demand,
                domesticProfiles,
                internationalProfiles);

            Random rnd = new Random(seed);

            // Channel draws use a dedicated stream so the main arrival/attribute
            // stream is untouched when a channel map is wired.
            Random channelRnd = channelAssigner != null
                ? new Random(unchecked(seed * 31 + 7))
                : null;

            PassengerGenerationResult result =
                new PassengerGenerationResult();

            List<Passenger> passengers =
                new List<Passenger>();

            int nextPassengerId = 1;

            // -----------------------------
            // Domestic Passengers
            // -----------------------------
            GeneratePassengerGroup(
                demand.DomesticPassengers,
                PassengerType.Domestic,
                domesticProfiles,
                walkingProfile,
                departureMinute,
                horizon,
                rnd,
                channelAssigner,
                channelRnd,
                passengers,
                result.ArrivalSimulations,
                ref nextPassengerId);

            // -----------------------------
            // International Passengers
            // -----------------------------
            GeneratePassengerGroup(
                demand.InternationalPassengers,
                PassengerType.International,
                internationalProfiles,
                walkingProfile,
                departureMinute,
                horizon,
                rnd,
                channelAssigner,
                channelRnd,
                passengers,
                result.ArrivalSimulations,
                ref nextPassengerId);

            result.Passengers =
                passengers
                .OrderBy(p => p.ArrivalTime)
                .ToList();

            foreach (Passenger passenger in result.Passengers)
            {
                passenger.QueueArrivalTime =
                    passenger.ArrivalTime;
            }

            return result;
        }

        private void GeneratePassengerGroup(
            int passengerCount,
            PassengerType passengerType,
            List<PassengerProfile> profiles,
            WalkingProfile walkingProfile,
            int departureMinute,
            int horizon,
            Random rnd,
            IChannelAssigner channelAssigner,
            Random channelRnd,
            List<Passenger> passengers,
            List<ArrivalSimulationResult> simulations,
            ref int nextPassengerId)
        {
            if (profiles.Count == 0 || passengerCount == 0)
                return;

            List<PassengerAllocation> allocations =
                AllocatePassengers(
                    passengerCount,
                    profiles);
            

            foreach (PassengerAllocation allocation in allocations)
            {
                ArrivalSimulationResult arrivals =
                    _arrivalGenerator.Generate(
                        allocation.PassengerCount,
                        departureMinute,
                        horizon,
                        allocation.Profile.ArrivalProfile,
                        rnd);

                simulations.Add(arrivals);

                // The legacy generator only produces departing passengers, so
                // tag them explicitly. This keeps the four-way DHPType counts
                // correct for legacy populations too.
                DHPType dhpType = passengerType == PassengerType.Domestic
                    ? DHPType.DomesticDeparture
                    : DHPType.InternationalDeparture;

                foreach (int arrival in arrivals.ArrivalMinutes)
                {
                    Passenger passenger =
                       PassengerFactory.Create(
                        nextPassengerId++,
                        allocation.Profile,
                        walkingProfile,
                        arrival,
                        rnd,
                        channelAssigner,
                        channelRnd,
                        dhpType);

                    passenger.PassengerType =
                        passengerType;

                    passenger.DHPType = dhpType;
                    passenger.ShowUpSignature = "none";

                    passengers.Add(passenger);
                }
            }
        }

        private List<PassengerAllocation> AllocatePassengers(
            int totalPassengers,
            List<PassengerProfile> profiles)
        {
            List<PassengerAllocation> allocations =
                new List<PassengerAllocation>();

            int allocated = 0;

            for (int i = 0; i < profiles.Count; i++)
            {
                PassengerProfile profile = profiles[i];

                int count;

                if (i == profiles.Count - 1)
                {
                    count = totalPassengers - allocated;
                }
                else
                {
                    count =
                        (int)Math.Round(
                            totalPassengers *
                            profile.Percentage /
                            100.0);

                    allocated += count;
                }

                allocations.Add(
                    new PassengerAllocation
                    {
                        Profile = profile,
                        PassengerCount = count
                    });
            }

            return allocations;
        }

        private void ValidateInputs(
            PassengerDemand demand,
            List<PassengerProfile> domesticProfiles,
            List<PassengerProfile> internationalProfiles)
        {
            if (demand == null)
                throw new ArgumentNullException(nameof(demand));

            if (domesticProfiles == null)
                throw new ArgumentNullException(nameof(domesticProfiles));

            if (internationalProfiles == null)
                throw new ArgumentNullException(nameof(internationalProfiles));

            ValidateProfileGroup(
                domesticProfiles,
                "Domestic");

            ValidateProfileGroup(
                internationalProfiles,
                "International");
        }

        private void ValidateProfileGroup(
            List<PassengerProfile> profiles,
            string groupName)
        {
            if (profiles.Count == 0)
                return;

            double percentage =
                profiles.Sum(x => x.Percentage);

            if (Math.Abs(percentage - 100.0) > 0.001)
            {
                throw new ArgumentException(
                    $"{groupName} passenger profiles must sum to 100%.");
            }
        }
    }
}
