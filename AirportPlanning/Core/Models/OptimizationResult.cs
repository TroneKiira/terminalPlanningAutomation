using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class OptimizationResult
    {
        public bool Success { get; set; }

        // NEW: Differentiate between Physical and Staffed
        public int OptimalPhysicalResources { get; set; }
        public int OptimalActiveResources { get; set; }
        public double Fitness { get; set; }

        public int Iterations { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public QueueResults QueueResults { get; set; }
    }
}
