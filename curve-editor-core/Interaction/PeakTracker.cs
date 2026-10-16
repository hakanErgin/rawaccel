using System.Collections.Generic;

namespace CurveEditor.Core.Interaction
{
    /// <summary>
    /// Rolling maximum over a time window (e.g. the fastest mouse speed in the last 5 seconds).
    /// When nothing new arrives the last peak is kept, so it can still be read after a swipe.
    /// </summary>
    public sealed class PeakTracker
    {
        private readonly double windowSeconds;
        // monotonic deque: values decrease from front to back, so the front is the window maximum
        private readonly LinkedList<(double Time, double Value)> samples = new LinkedList<(double, double)>();

        public PeakTracker(double windowSeconds = 5)
        {
            this.windowSeconds = windowSeconds;
        }

        /// <summary> Peak of the latest window that had samples, or null before the first sample. </summary>
        public double? Peak { get; private set; }

        /// <summary> Time (seconds) of the sample that set <see cref="Peak"/>. </summary>
        public double PeakTime { get; private set; }

        public void Add(double time, double value)
        {
            while (samples.Count > 0 && samples.Last.Value.Value <= value) samples.RemoveLast();
            samples.AddLast((time, value));
            Update(time);
        }

        /// <summary> Drops samples older than the window; keeps the last peak if the window is empty. </summary>
        public void Update(double now)
        {
            while (samples.Count > 0 && now - samples.First.Value.Time > windowSeconds) samples.RemoveFirst();
            if (samples.Count > 0)
            {
                Peak = samples.First.Value.Value;
                PeakTime = samples.First.Value.Time;
            }
        }

        public void Clear()
        {
            samples.Clear();
            Peak = null;
        }
    }
}
