using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Binary min-heap for queue resources.
    /// The resource with the smallest NextAvailableTime
    /// is always at the top of the heap.
    /// </summary>
    public class QueueResourceHeap
    {
        private readonly List<QueueResource> _heap;

        public int Count
        {
            get { return _heap.Count; }
        }

        public QueueResourceHeap(
            List<QueueResource> resources)
        {
            if (resources == null)
                throw new ArgumentNullException(nameof(resources));

            _heap = new List<QueueResource>(resources);

            Heapify();
        }

        /// <summary>
        /// Removes and returns the earliest available resource.
        /// </summary>
        public QueueResource Pop()
        {
            if (_heap.Count == 0)
                throw new InvalidOperationException(
                    "Heap is empty.");

            QueueResource root = _heap[0];

            QueueResource last =
                _heap[_heap.Count - 1];

            _heap.RemoveAt(_heap.Count - 1);

            if (_heap.Count > 0)
            {
                _heap[0] = last;
                SiftDown(0);
            }

            return root;
        }

        /// <summary>
        /// Inserts a resource back into the heap.
        /// </summary>
        public void Push(
            QueueResource resource)
        {
            _heap.Add(resource);

            SiftUp(_heap.Count - 1);
        }

        //----------------------------------------------------
        // Heap Construction
        //----------------------------------------------------

        private void Heapify()
        {
            for (int i = Parent(_heap.Count - 1);
                 i >= 0;
                 i--)
            {
                SiftDown(i);
            }
        }

        //----------------------------------------------------
        // Heap Operations
        //----------------------------------------------------

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = Parent(index);

                if (Compare(
                    _heap[parent],
                    _heap[index]) <= 0)
                    break;

                Swap(parent, index);

                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = Left(index);
                int right = Right(index);

                int smallest = index;

                if (left < _heap.Count &&
                    Compare(
                        _heap[left],
                        _heap[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < _heap.Count &&
                    Compare(
                        _heap[right],
                        _heap[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == index)
                    break;

                Swap(index, smallest);

                index = smallest;
            }
        }

        //----------------------------------------------------
        // Comparison
        //----------------------------------------------------

        private int Compare(
            QueueResource a,
            QueueResource b)
        {
            int result =
                a.NextAvailableTime.CompareTo(
                b.NextAvailableTime);

            // Stable ordering when equal
            if (result == 0)
            {
                result = a.Id.CompareTo(b.Id);
            }

            return result;
        }

        //----------------------------------------------------
        // Helpers
        //----------------------------------------------------

        private static int Parent(int i)
        {
            return (i - 1) / 2;
        }

        private static int Left(int i)
        {
            return 2 * i + 1;
        }

        private static int Right(int i)
        {
            return 2 * i + 2;
        }

        private void Swap(
            int i,
            int j)
        {
            QueueResource temp = _heap[i];
            _heap[i] = _heap[j];
            _heap[j] = temp;
        }
    }
}