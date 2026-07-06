using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.Immigration
{
    public class ImmigrationResults
    {
        public int NumberOfResources { get; set; }

        public QueueResults QueueResults { get; set; }
    }
}
