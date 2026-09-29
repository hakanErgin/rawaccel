using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CurveEditor.Controls
{
    /// <summary>
    /// Text box for a double. Type a value and press Enter (or leave the box), or use the mouse
    /// wheel / Up and Down keys to step it: the step is one unit in the third significant digit,
    /// Shift for 10x finer, Ctrl for 10x coarser. Escape restores the current value.
    /// </summary>
    public sealed class NumericField : TextBox
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(NumericField),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((NumericField)d).ShowValue()));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(NumericField), new PropertyMetadata(double.MinValue));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(NumericField), new PropertyMetadata(double.MaxValue));

        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));

        public NumericField()
        {
            HorizontalContentAlignment = HorizontalAlignment.Right;
            ToolTip = "Type a value and press Enter.\nMouse wheel or Up/Down steps it (Shift finer, Ctrl coarser).";
            ShowValue();
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        private void ShowValue()
        {
            Text = Value.ToString("G7", CultureInfo.InvariantCulture);
            ClearValue(BorderBrushProperty);
        }

        private void Commit()
        {
            if (double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v))
            {
                SetClamped(v);
            }
            else
            {
                BorderBrush = ErrorBrush;
            }
        }

        private void SetClamped(double v)
        {
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            Value = v;
            GetBindingExpression(ValueProperty)?.UpdateSource();
            // the view model may have rejected or adjusted the value
            ShowValue();
        }

        /// <summary> One unit in the third significant digit of the value (0.001 around zero). </summary>
        public static double StepFor(double value)
        {
            double mag = Math.Abs(value);
            if (mag < 1e-12) return 1e-3;
            return Math.Pow(10, Math.Floor(Math.Log10(mag)) - 2);
        }

        private void Step(int direction)
        {
            Commit();
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            double step = StepFor(Value) * (shift ? 0.1 : 1) * (ctrl ? 10 : 1);
            // round to the step so repeated steps don't accumulate float noise
            double next = Math.Round((Value + direction * step) / step) * step;
            SetClamped(next);
            SelectAll();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            switch (e.Key)
            {
                case Key.Enter:
                    Commit();
                    SelectAll();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    ShowValue();
                    SelectAll();
                    e.Handled = true;
                    break;
                case Key.Up:
                    Step(1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Step(-1);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            if (!IsKeyboardFocusWithin) return;
            Step(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            Commit();
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            SelectAll();
        }
    }
}
