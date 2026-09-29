using System;
using System.IO;
using System.Threading.Tasks;
using CurveEditor.Core;
using Newtonsoft.Json;

namespace CurveEditor.Services
{
    /// <summary>
    /// Loads and saves settings.json (the same file and format the grapher and writer use) and
    /// sends settings to the driver. Mirrors grapher/Models/Serialized/SettingsManager.cs.
    /// </summary>
    public sealed class SettingsService
    {
        public const string SettingsFileName = "settings.json";

        public SettingsService(string directory)
        {
            SettingsPath = Path.Combine(directory, SettingsFileName);
            SidecarPath = Path.Combine(directory, CustomCurveSidecar.DefaultFileName);
        }

        public string SettingsPath { get; }

        public string SidecarPath { get; }

        /// <summary> The configuration being edited (starts as settings.json). </summary>
        public DriverConfig Config { get; private set; }

        public CustomCurveSidecar Sidecar { get; private set; } = new CustomCurveSidecar();

        /// <summary> Set when settings.json existed but could not be used. </summary>
        public string LoadWarning { get; private set; }

        public void Load()
        {
            LoadWarning = null;
            Config = null;

            if (File.Exists(SettingsPath))
            {
                try
                {
                    var result = DriverConfig.Convert(File.ReadAllText(SettingsPath));
                    if (result.Item2 == null)
                    {
                        Config = result.Item1;
                    }
                    else
                    {
                        LoadWarning = $"{SettingsFileName} has invalid settings, showing the driver's active settings instead:\n{result.Item2}";
                    }
                }
                catch (Exception e) when (e is JsonException || e is IOException)
                {
                    LoadWarning = $"{SettingsFileName} could not be read, showing the driver's active settings instead:\n{e.Message}";
                }
            }

            if (Config == null)
            {
                Config = DriverConfig.GetActive();
            }

            if (Config.profiles.Count == 0)
            {
                var p = new Profile();
                Config.profiles.Add(p);
                Config.accels.Add(new ManagedAccel(p));
            }

            Sidecar = CustomCurveSidecar.Load(SidecarPath);
        }

        /// <summary> Default settings without touching the driver or disk (used by --screenshot). </summary>
        public void LoadDefaults()
        {
            LoadWarning = null;
            Config = DriverConfig.GetDefault();
            Sidecar = new CustomCurveSidecar();
        }

        /// <summary> Validates, writes settings.json and the sidecar, then sends the config to the driver. </summary>
        public async Task<string> ApplyAsync()
        {
            string errors = Config.Errors();
            if (errors != null) return errors;

            File.WriteAllText(SettingsPath, Config.ToJSON());

            try
            {
                Sidecar.Save(SidecarPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // losing the sidecar only loses custom-curve smoothing metadata
            }

            var cfg = Config;
            try
            {
                await Task.Run(() => cfg.Activate());
            }
            catch (InteropException e)
            {
                return "Settings were saved, but the driver rejected them: " + e.Message;
            }

            return null;
        }
    }
}
