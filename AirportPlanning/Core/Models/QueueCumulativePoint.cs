namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// One step of the cumulative arrival / departure diagram, the standard way
    /// an airport capacity diagram shows a queue.
    ///
    /// Sampled onto a regular time grid rather than at raw event times: a busy
    /// step produces one event per passenger, which would be tens of thousands
    /// of points that no one can read and that gain nothing over a minute grid.
    /// </summary>
    public class QueueCumulativePoint
    {
        /// <summary>Absolute simulation minute of this sample.</summary>
        public double Time { get; set; }

        /// <summary>Passengers who have arrived by <see cref="Time"/>.</summary>
        public int CumulativeArrivals { get; set; }

        /// <summary>Passengers whose service has finished by <see cref="Time"/>.</summary>
        public int CumulativeDepartures { get; set; }

        /// <summary>
        /// Passengers still waiting at <see cref="Time"/>. This is the vertical
        /// gap between the two cumulative curves, and is the quantity the
        /// diagram is actually about.
        /// </summary>
        public int QueueLength
        {
            get { return CumulativeArrivals - CumulativeDepartures; }
        }

        public QueueCumulativePoint()
        {
        }

        public QueueCumulativePoint(
            double time,
            int cumulativeArrivals,
            int cumulativeDepartures)
        {
            Time = time;
            CumulativeArrivals = cumulativeArrivals;
            CumulativeDepartures = cumulativeDepartures;
        }
    }
}
