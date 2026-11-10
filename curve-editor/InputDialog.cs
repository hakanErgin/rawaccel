using System.Windows;
using System.Windows.Controls;

namespace CurveEditor
{
    /// <summary> Minimal text prompt (profile names). </summary>
    public sealed class InputDialog : Window
    {
        private readonly TextBox box;

        private InputDialog(string title, string prompt, string initial)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            box = new TextBox { Text = initial ?? "", Width = 320, Margin = new Thickness(0, 6, 0, 12), MaxLength = 255 };

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
            ok.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            Content = panel;

            Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
        }

        public static string Show(Window owner, string title, string prompt, string initial)
        {
            var d = new InputDialog(title, prompt, initial) { Owner = owner };
            return d.ShowDialog() == true ? d.box.Text : null;
        }
    }
}
