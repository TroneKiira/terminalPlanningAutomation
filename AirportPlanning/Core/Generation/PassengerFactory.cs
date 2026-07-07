using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
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
                 Random rnd)
        {
            Passenger passenger =
                new Passenger();

            // -----------------------------
            // Identity
            // -----------------------------
            passenger.Id = id;

            // -----------------------------
            // Passenger Behaviour
            // -----------------------------
            passenger.Journey =
                profile.Journey;

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
