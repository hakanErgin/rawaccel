using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CurveEditor.ViewModels;

namespace CurveEditor
{
    public sealed class DeviceRow : ObservableObject
    {
        private bool overrideDefaults;
        private bool disable;
        private int dpi;
        private int pollingRate;
        private string profile;

        public string Name { get; set; }

        /// <summary> Name as reported by the device (stored in settings.json). </summary>
        public string DeviceName { get; set; }

        public string Id { get; set; }

        public DeviceSettings Existing { get; set; }

        public bool Override { get => overrideDefaults; set => Set(ref overrideDefaults, value); }

        public bool Disable { get => disable; set => Set(ref disable, value); }

        public int Dpi { get => dpi; set => Set(ref dpi, value < 0 ? 0 : value); }

        public int PollingRate { get => pollingRate; set => Set(ref pollingRate, value < 0 ? 0 : value); }

        public string Profile { get => profile; set => Set(ref profile, value); }
    }

    /// <summary> Per-device settings; same rules as grapher/DeviceMenuForm.cs and SettingsManager.Submit. </summary>
    public partial class DevicesWindow : Window
    {
        public const string FirstProfileLabel = "(first profile)";

        private readonly DriverConfig config;

        public DevicesWindow(DriverConfig config, IEnumerable<ProfileEditor> profiles)
        {
            InitializeComponent();
            this.config = config;

            ProfileNames = new[] { FirstProfileLabel }.Concat(profiles.Select(p => p.Name)).ToList();

            DefaultDisable = config.defaultDeviceConfig.disable;
            DefaultDpi = config.defaultDeviceConfig.dpi;
            DefaultPollingRate = config.defaultDeviceConfig.pollingRate;

            IList<MultiHandleDevice> connected;
            try
            {
                connected = MultiHandleDevice.GetList();
            }
            catch (System.Exception)
            {
                connected = new List<MultiHandleDevice>();
            }

            foreach (var dev in connected)
            {
                var existing = config.devices.FirstOrDefault(d => d.id == dev.id);
                var row = MakeRow(string.IsNullOrEmpty(dev.name) ? "(unnamed device)" : dev.name, dev.id, existing);
                row.DeviceName = dev.name ?? "";
                Rows.Add(row);
            }

            // keep configured devices that aren't plugged in right now
            foreach (var existing in config.devices.Where(d => connected.All(c => c.id != d.id)))
            {
                Rows.Add(MakeRow((string.IsNullOrEmpty(existing.name) ? existing.id : existing.name) + " (not connected)", existing.id, existing));
            }

            DataContext = this;
        }

        private DeviceRow MakeRow(string name, string id, DeviceSettings existing)
        {
            var cfg = existing?.config ?? config.defaultDeviceConfig;
            string profile = existing?.profile;
            return new DeviceRow
            {
                Name = name,
                Id = id,
                Existing = existing,
                Override = existing != null,
                Disable = cfg.disable,
                Dpi = cfg.dpi,
                PollingRate = cfg.pollingRate,
                Profile = string.IsNullOrEmpty(profile) || !ProfileNames.Contains(profile) ? FirstProfileLabel : profile,
            };
        }

        public List<string> ProfileNames { get; }

        public ObservableCollection<DeviceRow> Rows { get; } = new ObservableCollection<DeviceRow>();

        public bool DefaultDisable { get; set; }

        public int DefaultDpi { get; set; }

        public int DefaultPollingRate { get; set; }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var defaults = config.defaultDeviceConfig;
            defaults.disable = DefaultDisable;
            defaults.dpi = System.Math.Max(0, DefaultDpi);
            defaults.pollingRate = System.Math.Max(0, DefaultPollingRate);
            config.defaultDeviceConfig = defaults;

            foreach (var row in Rows)
            {
                string profile = row.Profile == FirstProfileLabel ? "" : row.Profile;

                if (row.Override)
                {
                    // start from existing (or default) config so hidden fields such as time clamps are kept
                    var cfg = row.Existing?.config ?? defaults;
                    cfg.disable = row.Disable;
                    cfg.dpi = row.Dpi;
                    cfg.pollingRate = row.PollingRate;

                    if (row.Existing == null)
                    {
                        config.devices.Add(new DeviceSettings
                        {
                            name = row.DeviceName ?? "",
                            id = row.Id,
                            profile = profile,
                            config = cfg,
                        });
                    }
                    else
                    {
                        row.Existing.config = cfg;
                        row.Existing.profile = profile;
                    }
                }
                else if (row.Existing != null)
                {
                    config.devices.Remove(row.Existing);
                }
            }

            DialogResult = true;
        }
    }
}
