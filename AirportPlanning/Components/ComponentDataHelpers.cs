using System;
using Grasshopper.Kernel.Types;

namespace AirportPlanning.Components
{
    /// <summary>
    /// Helpers for recovering typed objects (including interfaces) from
    /// Grasshopper generic parameters. Grasshopper wraps arbitrary objects in a
    /// GH_ObjectWrapper, so a plain cast is not enough.
    /// </summary>
    internal static class ComponentDataHelpers
    {
        public static T Unwrap<T>(object raw) where T : class
        {
            if (raw == null)
                return null;

            if (raw is T typed)
                return typed;

            if (raw is GH_ObjectWrapper wrapper && wrapper.Value is T wrapped)
                return wrapped;

            return null;
        }
    }
}
