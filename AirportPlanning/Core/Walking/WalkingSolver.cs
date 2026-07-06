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
        public List<Passenger> Solve(
            WalkingInputs input)
        {
            

            foreach (Passenger passenger in input.Passengers)
            {
                passenger.WalkingTime =
                 input.Distance / passenger.WalkingSpeed;

                passenger.ArrivalTime =
                    passenger.EndServiceTime +
                    passenger.WalkingTime;
            }

            return input.Passengers;
        }
    }
}
