using System;
using CurveEditor.Core;

namespace CurveEditor.ViewModels
{
    /// <summary> One numeric curve parameter shown in the side panel, kept in sync with the handles. </summary>
    public sealed class ParameterField : ObservableObject
    {
        private readonly Func<CurveArgs, double> get;
        private readonly Action<CurveArgs, double> set;
        private readonly Func<ParameterField, double, bool> commit;
        private double value;

        public ParameterField(
            string label,
            string tooltip,
            Func<CurveArgs, double> get,
            Action<CurveArgs, double> set,
            double minimum,
            double maximum,
            Func<ParameterField, double, bool> commit)
        {
            Label = label;
            Tooltip = tooltip;
            this.get = get;
            this.set = set;
            Minimum = minimum;
            Maximum = maximum;
            this.commit = commit;
        }

        public string Label { get; }

        public string Tooltip { get; }

        public double Minimum { get; }

        public double Maximum { get; }

        public double Value
        {
            get => value;
            set
            {
                if (value == this.value) return;
                if (commit(this, value))
                {
                    this.value = value;
                }
                // notify either way so a rejected value snaps back in the text box
                OnPropertyChanged();
            }
        }

        /// <summary> Delta since the last applied settings, for display. </summary>
        public string Delta { get; private set; } = "";

        public void Apply(CurveArgs args, double v) => set(args, v);

        public void Refresh(CurveArgs current, CurveArgs applied)
        {
            value = get(current);
            OnPropertyChanged(nameof(Value));

            string delta = "";
            if (applied != null && applied.Mode == current.Mode)
            {
                double d = value - get(applied);
                if (Math.Abs(d) > 1e-12 * Math.Max(1, Math.Abs(value)))
                {
                    delta = (d > 0 ? "+" : "") + d.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (delta != Delta)
            {
                Delta = delta;
                OnPropertyChanged(nameof(Delta));
            }
        }
    }
}
