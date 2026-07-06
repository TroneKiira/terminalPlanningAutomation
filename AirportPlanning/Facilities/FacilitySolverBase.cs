using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Distributions;

using Rhino;
using AirportPlanning.Core.Walking;


namespace AirportPlanning.Facilities
{
    public abstract class FacilitySolverBase
    {
        protected readonly LogNormalServiceTimeGenerator _serviceGenerator;
        protected readonly QueueSimulator _queueSimulator;
        protected readonly QueueMetricsCalculator _metricsCalculator;

        protected FacilitySolverBase()
        {
            _serviceGenerator = new LogNormalServiceTimeGenerator();
            _queueSimulator = new QueueSimulator();
            _metricsCalculator = new QueueMetricsCalculator();
        }

        protected List<Passenger> FilterPassengers(IEnumerable<Passenger> passengers, Func<Passenger, bool> predicate)
        {
            return passengers.Where(predicate).ToList();
        }

        protected List<QueueResource> PrepareQueue(List<Passenger> passengers, int numberOfResources, ServiceProfile serviceProfile, Random random)
        {
            AssignServiceTimes(passengers, serviceProfile, random);
            return QueueResource.Create(numberOfResources);
        }

        protected QueueResults SimulateQueue(List<Passenger> passengers, List<QueueResource> resources)
        {
            _queueSimulator.ResetPassengers(passengers);
            _queueSimulator.ResetResources(resources);
            _queueSimulator.RunQueue(passengers, resources);

            QueueResults results = _metricsCalculator.Calculate(passengers, resources);

            results.ProcessedPassengers = passengers;
            results.ResourcesUsed = resources.Count;

            return results;
        }

        protected void AssignServiceTimes(List<Passenger> passengers, ServiceProfile serviceProfile, Random random)
        {
            List<double> serviceTimes = _serviceGenerator.Generate(
                    passengers.Count,
                    serviceProfile.MeanServiceTime,
                    serviceProfile.StandardDeviation,
                    random);

            for (int i = 0; i < passengers.Count; i++)
            {
                passengers[i].ServiceTime = serviceTimes[i];
            }
        }

        protected void AdvancePassengers(IEnumerable<Passenger> passengers)
        {
            foreach (Passenger passenger in passengers)
            {
                passenger.ArrivalTime = passenger.EndServiceTime + passenger.WalkingTime;
                passenger.QueueArrivalTime = passenger.ArrivalTime;
            }
        }

        protected QueueResults CreateEmptyResult(List<Passenger> passengers)
        {
            return new QueueResults
            {
                Passengers = passengers,
                ProcessedPassengers = new List<Passenger>(),
                Resources = new List<QueueResource>(),
                ResourcesUsed = 0,
                PeakQueueLength = 0,
                AverageWait = 0,
                MaxWait = 0,
                Wait95 = 0,
                SimulationDuration = 0,
                CounterUtilization = new List<double>()
            };
        }

        protected QueueResults ExecuteFacility(
            List<Passenger> allPassengers,
            Func<Passenger, bool> filter,
            int numberOfResources,
            ServiceProfile serviceProfile,
            Random random)
        {
            List<Passenger> facilityPassengers = FilterPassengers(allPassengers, filter);

            if (facilityPassengers.Count == 0)
                return CreateEmptyResult(allPassengers);

            List<QueueResource> resources = PrepareQueue(facilityPassengers, numberOfResources, serviceProfile, random);

            QueueResults results = SimulateQueue(facilityPassengers, resources);

            // FIX: Removed AdvancePassengers from here so the optimizer loop doesn't mutate state!

            results.Passengers = allPassengers;

            return results;
        }

        protected QueueResults ExecuteFacility(
            List<Passenger> allPassengers,
            List<Passenger> facilityPassengers,
            List<QueueResource> resources)
        {
            QueueResults results = SimulateQueue(facilityPassengers, resources);

            // FIX: Removed AdvancePassengers from here too!

            results.Passengers = allPassengers;

            return results;
        }
    }
}
