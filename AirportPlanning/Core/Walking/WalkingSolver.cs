using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Walking
{
    public class WalkingSolver
    {
        public List<Passenger> Solve(WalkingInputs input)
        {
            foreach (Passenger p in input.Passengers)
            {
                // Calculate time in seconds (Distance / Speed)
                double timeInSeconds = input.Distance / p.WalkingSpeed;

                // Convert to minutes
                double walkingTimeMinutes = timeInSeconds / 60.0;

                // Update passenger metrics
                p.WalkingTime = walkingTimeMinutes;

                // Advance their internal clock by the walking duration!
                p.ArrivalTime += walkingTimeMinutes;
                p.QueueArrivalTime = p.ArrivalTime;
            }

            return input.Passengers;
        }
    }
}
