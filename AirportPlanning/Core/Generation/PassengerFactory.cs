using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using AirportPlanning.Core.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Generation
{
    public static class PassengerFactory
    {
        public static Passenger Create(
                 int id,
                 PassengerProfile profile,
                 WalkingProfile walkingProfile,
                 double arrivalTime,
                 Random rnd,
                 IChannelAssigner channelAssigner = null,
                 Random channelRnd = null,
                 Inputs.DHPType dhpType = Inputs.DHPType.DomesticDeparture)
        {
            Passenger passenger =
                new Passenger();

            // -----------------------------
            // Identity
            // -----------------------------
            passenger.Id = id;
            passenger.ProfileName = profile != null ? profile.Name : string.Empty;
            passenger.DHPType = dhpType;

            // -----------------------------
            // Passenger Behaviour
            // -----------------------------
            passenger.Journey =
                profile.TransferScenario.HasValue
                    ? PassengerJourney.Transfer
                    : profile.Journey;

            passenger.TransferScenario = profile.TransferScenario;

            passenger.ArrivalTime =
                arrivalTime;
            passenger.QueueArrivalTime =
                arrivalTime;

            // NEW: Assign Gender based on profile probability
            passenger.Gender = rnd.NextDouble() <= profile.MaleProbability ? PassengerGender.Male : PassengerGender.Female;

            passenger.UsesOnlineCheckIn =
                rnd.NextDouble() <=
                profile.OnlineCheckInProbability;

            passenger.HasCheckedBag =
                rnd.NextDouble() <=
                profile.CheckedBagProbability;

            // Walking Behaviour
            // -----------------------------
            passenger.WalkingSpeed =
                GenerateWalkingSpeed(
                    walkingProfile,
                    rnd);

            // -----------------------------
            // Channel (user-defined routing)
            // -----------------------------
            // With no map wired the deterministic default reproduces the built-in
            // routing and consumes no RNG, so existing numbers are untouched.
            IChannelAssigner assigner = channelAssigner ?? DefaultChannelAssigner.Instance;
            Random assignRnd = channelRnd ?? rnd;
            passenger.Channel = DefaultChannelAssigner.NormalizePrimaryChannel(
                assigner.AssignChannel(passenger, assignRnd));

            // When a channel-routing map is wired it is the source of truth for
            // the primary method. Web is the online-check-in route; Counter and
            // Kiosk are physical. Do not draw OCI a second time and allow the
            // profile share and the online flag to disagree.
            if (channelAssigner != null)
            {
                passenger.UsesOnlineCheckIn =
                    DefaultChannelAssigner.IsWebChannel(passenger.Channel);
            }

            return passenger;
        }
        private static double GenerateWalkingSpeed(
            WalkingProfile profile,
            Random rnd)
        {
            double speed =
                profile.AverageSpeed +
                profile.StandardDeviation *
                NextGaussian(rnd);

            return Math.Max(0.5, speed);
        }

        private static double NextGaussian(
        Random rnd)
        {
            double u1 =
                1.0 - rnd.NextDouble();

            double u2 =
                1.0 - rnd.NextDouble();

            return Math.Sqrt(-2.0 * Math.Log(u1))
                * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
