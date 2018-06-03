using Common.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Makaretu.Dns.Peds
{
    /// <summary>
    /// 
    /// </summary>
    public class Performance
    {
        static ILog log = LogManager.GetLogger(typeof(Performance));

        /// <summary>
        ///   The category name for this collection of performance counters.
        /// </summary>
        /// <remarks>
        ///   This is always <c>Privacy enabled DNS</c>.
        /// </remarks>
        public const string CategoryName = "Privacy enabled DNS";

        /// <summary>
        ///   Gets the singleton instance of the <see cref="Performance"/> class.
        /// </summary>
        public static readonly Performance Instance = new Performance();

        long frequency = 0;
        bool enabled = true;
        bool available = true;
        bool availabilityChecked = false;
        string unavailableReason;
        PerformanceCounter[] counters;

        [DllImport("kernel32.dll", SetLastError = true)]
        private extern static short QueryPerformanceFrequency(out long x);

        [DllImport("Kernel32.dll")]
        public static extern void QueryPerformanceCounter(ref long ticks);

        private Performance()
        {
        }

        /// <summary>
        ///   Gets the number of ticks per second based on the current
        ///   high-resolution performance counter frequency.
        /// </summary>
        /// <value>
        ///   A <see cref="long"/> representing the ticks per second.
        /// </value>
        /// <remarks>
        ///   <b>Frequency</b> is the ticks per second of the current
        ///   high-resolution performance counter frequency.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///   If the installed hardware does not support a high-resolution performance counter or
        ///   security does not allow access to it.
        /// </exception>
        public long Frequency
        {
            get
            {
                if (!Available)
                    throw new InvalidOperationException(unavailableReason);

                if (frequency == 0)
                {
                    try
                    {
                        if (0 == QueryPerformanceFrequency(out frequency))
                            throw new Win32Exception();
                    }
                    catch (Exception e)
                    {
                        log.Error("QueryPerformanceFrequency failed", e);
                        available = false;
                        unavailableReason = e.Message;
                        throw e;
                    }
                }

                return frequency;
            }
        }

        bool Available
        {
            get
            {
                if (!availabilityChecked)
                {
                    lock (this)
                    {
                        CheckAvailability();
                    }
                }

                return available;
            }
        }

        void CheckAvailability()
        {
            if (availabilityChecked)
                return;

            availabilityChecked = true;
            try
            {

                var ccdc = new CounterCreationDataCollection();
                ccdc.Add(new CounterCreationData(
                   "# of DNS requests",
                   "Displays the number of times that a DNS request is made.",
                   PerformanceCounterType.NumberOfItems32));
                ccdc.Add(new CounterCreationData(
                   "# of DNS requests/sec",
                   "Displays the number of times that a DNS request is made in a second.",
                   PerformanceCounterType.RateOfCountsPerSecond32));

                ccdc.Add(new CounterCreationData(
                   "Avg DNS request time",
                   "Displays the average time to resolve a DNS request.",
                   PerformanceCounterType.AverageTimer32));
                ccdc.Add(new CounterCreationData(
                   "Avg DNS request time base",
                   "Helper.",
                   PerformanceCounterType.AverageBase));

                // If our category does not exist, then create it.
#if false
                PerformanceCounterCategory.Delete(CategoryName);
#endif
                var createdCategory = false;
                if (PerformanceCounterCategory.Exists(CategoryName))
                {
                    if (log.IsDebugEnabled)
                        log.Debug("Counter categorty " + CategoryName + " exists");
                }
                else
                {
                    if (log.IsDebugEnabled)
                        log.Debug("Creating counter category " + CategoryName);

                    PerformanceCounterCategory.Create(
                       CategoryName,
                       "Performance counters for PEDS.",
                      PerformanceCounterCategoryType.SingleInstance,
                      ccdc);
                    createdCategory = true;
                }

                // Get our counters.
                counters = new PerformanceCounter[ccdc.Count];
                for (int i = 0; i < ccdc.Count; ++i)
                {
                    counters[i] = new PerformanceCounter(CategoryName, ccdc[i].CounterName, String.Empty, false);
                }

                // If we just created the category, zero out the values.
                if (createdCategory)
                {
                    if (log.IsDebugEnabled)
                        log.Debug("Zero out the values");

                    foreach (PerformanceCounter counter in counters)
                    {
                        counter.RawValue = 0;
                    }
                }

                // Verify that the counters are working.
                if (log.IsDebugEnabled)
                    log.Debug("Verify counters");

                counters[0].NextSample();
            }
            catch (Exception e)
            {
                log.Error("Performance counters not available", e);

                available = false;
                unavailableReason = e.Message;
            }
        }

        /// <summary>
        ///   Gets or sets wether the performance counters are being maintained.
        /// </summary>
        /// <value>
        ///   <b>true</b> if the performance counters are being maintained; otherwise, <b>false</b>.
        /// </value>
        /// <remarks>
        ///   <b>Enabled</b> will always return <b>false</b> if the installed hardware does not support a 
        ///   high-resolution performance counter or security does not allow access to it.
        /// </remarks>
        public bool Enabled
        {
            get
            {
                return enabled && Available;
            }
            set
            {
                if (log.IsDebugEnabled)
                    log.Debug("Enabled set to " + value.ToString());

                enabled = value;
            }
        }

        /// <summary>
        ///   Gets a <see cref="PerformanceCounter"/> that represents
        ///   the number of times that a DNS request is made.
        /// </summary>
        /// <value>
        ///   A <see cref="PerformanceCounter"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        ///   When the installed hardware does not support a high-resolution performance counter or
        ///   security does not allow access to it.
        /// </exception>
        public PerformanceCounter RequestCount
        {
            get { return GetCounter(0); }
        }

        /// <summary>
        ///   Gets a <see cref="PerformanceCounter"/> that represents
        ///   the number of times that a DNS request is made per second.
        /// </summary>
        /// <value>
        ///   A <see cref="PerformanceCounter"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        ///   When the installed hardware does not support a high-resolution performance counter or
        ///   security does not allow access to it.
        /// </exception>
        public PerformanceCounter RequestCountPerSecond
        {
            get { return GetCounter(1); }
        }

        /// <summary>
        ///   Gets a <see cref="PerformanceCounter"/> that represents
        ///   the average time to resolve a DNS request.
        /// </summary>
        /// <value>
        ///   A <see cref="PerformanceCounter"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        ///   When the installed hardware does not support a high-resolution performance counter or
        ///   security does not allow access to it.
        /// </exception>
        public PerformanceCounter AvgResolveTime
        {
            get { return GetCounter(2); }
        }

        internal PerformanceCounter AvgResolveTimeBase
        {
            get { return GetCounter(3); }
        }

        PerformanceCounter GetCounter(int index)
        {
            if (!Available)
                throw new InvalidOperationException("Can not access counters because: " + unavailableReason);

            return counters[index];
        }

    }
}
