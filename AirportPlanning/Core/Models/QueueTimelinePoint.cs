namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Queue length at one instant in the simulation.
    ///
    /// The sequence of these is the queue over time: arrivals add one, the
    /// moment service starts removes one, so the sweep is a staircase. Only
    /// passengers who actually queued contribute, which is why a point is
    /// emitted per event rather than per passenger.
    /// </summary>
    public class QueueTimelinePoint
    {
        /// <summary>Absolute simulation minute of the event.</summary>
        public double Time { get; set; }

        /// <summary>Passengers waiting immediately after this event.</summary>
        public int Queue { get; set; }

        public QueueTimelinePoint()
        {
        }

        public QueueTimelinePoint(double time, int queue)
        {
            Time = time;
            Queue = queue;
        }
    }
}
