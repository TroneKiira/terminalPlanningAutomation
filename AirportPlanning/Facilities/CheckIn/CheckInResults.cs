using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInResults
    {
        public int PhysicalCounters { get; set; }
        public int ActiveCounters { get; set; }
        public QueueResults QueueResults { get; set; }
    }
}
