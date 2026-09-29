using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CurveEditor.Controls;
using CurveEditor.Core;
using CurveEditor.Core.Curves;
using CurveEditor.Core.Handles;
using CurveEditor.Core.Interaction;
using CurveEditor.Models;
using CurveEditor.Services;

namespace CurveEditor.ViewModels
{
    public sealed class ModeOption
    {
        public ModeOption(CurveMode mode, string name, string description)
        {
            Mode = mode;
            Name = name;
            Description = description;
        }

        public CurveMode Mode { get; }

        public string Name { get; }

        public string Description { get; }

        public override string ToString() => Name;
    }

    /// <summary> Edit state for one profile of the driver config. </summary>
    public sealed class ProfileEditor : ObservableObject
    {
        private string name;

        public ProfileEditor(Profile profile, ProfileState state)
        {
            Profile = profile;
            name = profile.name;
            AppliedName = profile.name;
            Current = state;
            Applied = state;
        }

        public Profile Profile { get; }

        public string Name
        {
            get => name;
            set => Set(ref name, value);
        }

        /// <summary> Name in the last applied settings (used to keep the sidecar tidy). </summary>
        public string AppliedName { get; set; }

        public ProfileState Current { get; set; }

        /// <summary> Last applied (or loaded) state: drawn as the ghost curve. </summary>
        public ProfileState Applied { get; set; }

        public UndoStack<ProfileState> History { get; } = new UndoStack<ProfileState>();

        public bool IsDirty => !Current.SameAs(Applied) || Name != AppliedName;

        public override string ToString() => Name;
    }

    public sealed class MainViewModel : ObservableObject
    {
        private readonly SettingsService settings;
        private readonly ViewState view;
        private ProfileEditor selected;
        private bool editingY;
        private string status = "";
        private string selection = "";
        private string selectedHandleId;
        private HandleContext context;
        private bool dragging;
        private bool dragPushed;
        private string lastNudgeId;
        private DateTime lastNudgeTime;
        private ChartKind chartKind = ChartKind.Sensitivity;
        private bool devicesDirty;
        private bool applying;
        private string lutText = "";
        private readonly List<string> deletedNames = new List<string>();

        public MainViewModel(SettingsService settings, ViewState view)
        {
            this.settings = settings;
            this.view = view;

            Modes = new[]
            {
                new ModeOption(CurveMode.Classic, "Classic", "Linear or power-law growth above an offset, optionally capped"),
                new ModeOption(CurveMode.Natural, "Natural", "Rises quickly then eases into a limit"),
                new ModeOption(CurveMode.Power, "Power", "Sensitivity grows as a power of speed"),
                new ModeOption(CurveMode.Synchronous, "Synchronous", "S-curve around a sync speed, between 1/motivity and motivity"),
                new ModeOption(CurveMode.Jump, "Jump", "Steps up at a given speed, optionally smoothed"),
                new ModeOption(CurveMode.Lookup, "Custom curve", "Free-form: add, drag and delete points"),
                new ModeOption(CurveMode.NoAccel, "No acceleration", "Constant sensitivity"),
            };

            ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => IsDirty && !applying);
            RevertCommand = new RelayCommand(Revert, () => selected != null && selected.IsDirty);
            UndoCommand = new RelayCommand(Undo, () => selected?.History.CanUndo == true);
            RedoCommand = new RelayCommand(Redo, () => selected?.History.CanRedo == true);
            FitCommand = new RelayCommand(Fit);
            ResetViewCommand = new RelayCommand(ResetView);
            ConvertToCustomCommand = new RelayCommand(ConvertToCustom, () => CurrentArgs != null && CurrentArgs.Mode != CurveMode.Lookup);
            DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1);
        }

        public event EventHandler CurveChanged;

        /// <summary> Asks the view for a name (title, prompt, initial) and returns null on cancel. </summary>
        public Func<string, string, string, string> PromptForText { get; set; }

        /// <summary> Supplied by the view: data-space size of one arrow-key nudge at the selected handle. </summary>
        public Func<Point2?> NudgeStepProvider { get; set; }

        public IReadOnlyList<ModeOption> Modes { get; }

        public ObservableCollection<ProfileEditor> Profiles { get; } = new ObservableCollection<ProfileEditor>();

        public DriverConfig Config => settings.Config;

        public ICommand ApplyCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand FitCommand { get; }
        public ICommand ResetViewCommand { get; }
        public ICommand ConvertToCustomCommand { get; }
        public ICommand DeleteProfileCommand { get; }

        public ObservableCollection<ParameterField> Parameters { get; } = new ObservableCollection<ParameterField>();

        #region Loading

        public void Load()
        {
            settings.Load();
            Profiles.Clear();

            foreach (var p in settings.Config.profiles)
            {
                settings.Sidecar.Profiles.TryGetValue(p.name, out var saved);
                Profiles.Add(new ProfileEditor(p, ProfileState.FromProfile(p, saved)));
            }

            devicesDirty = false;
            deletedNames.Clear();
            SelectedProfile = Profiles.FirstOrDefault();
            Status = settings.LoadWarning ?? $"Loaded {settings.SettingsPath}";
        }

        #endregion

        #region Selection and derived state

        public ProfileEditor SelectedProfile
        {
            get => selected;
            set
            {
                if (value == null || !Set(ref selected, value)) return;
                editingY = false;
                selectedHandleId = null;
                OnPropertyChanged(nameof(EditingY));
                RebuildParameters();
                RefreshAll();
                ResetView();
            }
        }

        private ProfileState State => selected?.Current;

        public CurveArgs CurrentArgs => State == null ? null : (editingY && State.Separate ? State.Y : State.X);

        public CurveArgs AppliedArgs => selected?.Applied == null ? null : (editingY && State.Separate ? selected.Applied.Y : selected.Applied.X);

        public ICurve Curve => CurrentArgs == null ? null : CurveMath.Create(CurrentArgs);

        public ICurve Ghost => AppliedArgs == null ? null : CurveMath.Create(AppliedArgs);

        public IReadOnlyList<Handle> Handles =>
            CurrentArgs == null ? Array.Empty<Handle>() : HandleSets.For(CurrentArgs.Mode).GetHandles(CurrentArgs, Context);

        public string SelectedHandleId
        {
            get => selectedHandleId;
            set
            {
                if (Set(ref selectedHandleId, value)) UpdateSelectionText();
            }
        }

        private HandleContext Context
        {
            get
            {
                if (context == null || (!dragging && (context.AnchorX < view.MinX || context.AnchorX > view.MaxX)))
                {
                    RecomputeContext();
                }
                return context;
            }
        }

        private void RecomputeContext()
        {
            context = new HandleContext(
                view.ScreenToX(view.Left + view.Width * 0.6),
                view.ScreenToX(view.Left + view.Width * 0.9));
        }

        public bool IsDirty => devicesDirty || Profiles.Any(p => p.IsDirty);

        public string DirtyText => IsDirty ? "● Unsaved changes — press Apply to write them to the driver" : "All changes applied";

        public string Status
        {
            get => status;
            set => Set(ref status, value);
        }

        public string SelectionText
        {
            get => selection;
            private set => Set(ref selection, value);
        }

        public ChartKind ChartKind
        {
            get => chartKind;
            set
            {
                if (!Set(ref chartKind, value)) return;
                Raise();
                Fit();
            }
        }

        public bool LogX
        {
            get => view.LogX;
            set
            {
                if (view.LogX == value) return;
                view.SetLogX(value);
                OnPropertyChanged();
                RecomputeContext();
                Raise();
            }
        }

        #endregion

        #region Curve options

        public bool EditingY
        {
            get => editingY;
            set
            {
                if (!Set(ref editingY, value)) return;
                selectedHandleId = null;
                RebuildParameters();
                RefreshAll();
                ResetView();
            }
        }

        public ModeOption SelectedMode
        {
            get => CurrentArgs == null ? null : Modes.First(m => m.Mode == CurrentArgs.Mode);
            set
            {
                if (value == null || CurrentArgs == null || value.Mode == CurrentArgs.Mode) return;
                ChangeMode(value.Mode);
            }
        }

        public bool Gain
        {
            get => CurrentArgs?.Gain ?? true;
            set
            {
                if (CurrentArgs == null || value == CurrentArgs.Gain) return;
                EditCurve(a => a.Gain = value, "gain");
            }
        }

        public bool Legacy
        {
            get => !Gain;
            set => Gain = !value;
        }

        public bool IsLookup => CurrentArgs?.Mode == CurveMode.Lookup;

        public bool IsParametric => CurrentArgs != null && CurrentArgs.Mode != CurveMode.Lookup && CurrentArgs.Mode != CurveMode.NoAccel;

        public bool HasGainOption => CurrentArgs != null && CurrentArgs.Mode != CurveMode.NoAccel;

        public string GainLabel => IsLookup ? "Points are output speed (gain)" : "Gain";

        public string LegacyLabel => IsLookup ? "Points are sensitivity (legacy)" : "Legacy";

        public bool CapVisible => CurrentArgs != null && (CurrentArgs.Mode == CurveMode.Classic || CurrentArgs.Mode == CurveMode.Power);

        public bool Capped
        {
            get => CurrentArgs != null && CapVisible && CurrentArgs.CapY > 0;
            set
            {
                if (!CapVisible || value == Capped) return;
                var next = HandleSets.SetCapped(CurrentArgs, value, Context);
                ReplaceCurve(next, pushUndo: true);
                RebuildParameters();
                RefreshAll();
            }
        }

        public bool SmoothLut
        {
            get => CurrentArgs?.SmoothLut ?? false;
            set
            {
                if (value == SmoothLut) return;
                if (value && CurrentArgs.ControlPoints.Count > LutBuilder.MaxSmoothControlPoints)
                {
                    Status = $"Smoothing supports up to {LutBuilder.MaxSmoothControlPoints} points; remove some points first.";
                    OnPropertyChanged();
                    return;
                }
                EditCurve(a => a.SmoothLut = value, "smoothing");
            }
        }

        public string PointCountText => IsLookup
            ? $"{CurrentArgs.ControlPoints.Count} points → {CurrentArgs.BuildLut().Length} table entries (max {CurveArgs.MaxLutPoints})"
            : "";

        /// <summary> Custom curve points as "x,y;x,y;…" (same format as the grapher's LUT box). </summary>
        public string LutText
        {
            get => lutText;
            set
            {
                if (!IsLookup) return;
                if (!TryParseLut(value, out var points, out string error))
                {
                    Status = error;
                    OnPropertyChanged();
                    return;
                }
                EditCurve(a =>
                {
                    a.ControlPoints = points;
                    if (points.Count > LutBuilder.MaxSmoothControlPoints) a.SmoothLut = false;
                }, "points");
            }
        }

        private static bool TryParseLut(string text, out List<Point2> points, out string error)
        {
            points = new List<Point2>();
            error = null;
            var entries = (text ?? "").Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var parts = entry.Split(',');
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    error = $"Could not read point \"{entry.Trim()}\"; use x,y;x,y;…";
                    return false;
                }
                if (x <= 0 || y <= 0 || (points.Count > 0 && x <= points[points.Count - 1].X))
                {
                    error = "Points need positive values and strictly increasing speeds.";
                    return false;
                }
                points.Add(new Point2(x, y));
            }
            if (points.Count < 2 || points.Count > CurveArgs.MaxLutPoints)
            {
                error = $"A custom curve needs between 2 and {CurveArgs.MaxLutPoints} points.";
                return false;
            }
            return true;
        }

        private static string FormatLut(IEnumerable<Point2> points) =>
            string.Join(";", points.Select(p => string.Format(CultureInfo.InvariantCulture, "{0:G7},{1:G7}", p.X, p.Y)));

        #endregion

        #region Profile-level settings

        private double ProfileValue(Func<ProfileState, double> get) => State == null ? 0 : get(State);

        public double Sensitivity { get => ProfileValue(s => s.Sensitivity); set => EditProfile(s => s.Sensitivity = value, value != 0); }
        public double YXRatio { get => ProfileValue(s => s.YXRatio); set => EditProfile(s => s.YXRatio = value, value != 0); }
        public double Rotation { get => ProfileValue(s => s.Rotation); set => EditProfile(s => s.Rotation = value); }
        public double Snap { get => ProfileValue(s => s.Snap); set => EditProfile(s => s.Snap = value, value >= 0 && value <= 45); }
        public double SpeedCap { get => ProfileValue(s => s.SpeedCap); set => EditProfile(s => s.SpeedCap = value, value >= 0); }
        public double LRRatio { get => ProfileValue(s => s.LRRatio); set => EditProfile(s => s.LRRatio = value, value > 0); }
        public double UDRatio { get => ProfileValue(s => s.UDRatio); set => EditProfile(s => s.UDRatio = value, value > 0); }
        public double DomainX { get => ProfileValue(s => s.DomainX); set => EditProfile(s => s.DomainX = value, value > 0); }
        public double DomainY { get => ProfileValue(s => s.DomainY); set => EditProfile(s => s.DomainY = value, value > 0); }
        public double RangeX { get => ProfileValue(s => s.RangeX); set => EditProfile(s => s.RangeX = value, value >= 0); }
        public double RangeY { get => ProfileValue(s => s.RangeY); set => EditProfile(s => s.RangeY = value, value >= 0); }
        public double LpNorm { get => ProfileValue(s => s.LpNorm); set => EditProfile(s => s.LpNorm = value, value > 0); }
        public double InputSmoothHalflife { get => ProfileValue(s => s.InputSmoothHalflife); set => EditProfile(s => s.InputSmoothHalflife = value, value >= 0); }
        public double ScaleSmoothHalflife { get => ProfileValue(s => s.ScaleSmoothHalflife); set => EditProfile(s => s.ScaleSmoothHalflife = value, value >= 0); }
        public double OutputSmoothHalflife { get => ProfileValue(s => s.OutputSmoothHalflife); set => EditProfile(s => s.OutputSmoothHalflife = value, value >= 0); }

        public bool Separate
        {
            get => State?.Separate ?? false;
            set
            {
                if (State == null || value == State.Separate) return;
                Push();
                var next = State.Clone();
                next.Separate = value;
                if (value && next.Y.Mode == CurveMode.NoAccel && next.X.Mode != CurveMode.NoAccel)
                {
                    // start the vertical curve as a copy of the horizontal one
                    next.Y = next.X.Clone();
                }
                selected.Current = next;
                editingY = false;
                OnPropertyChanged(nameof(EditingY));
                RebuildParameters();
                RefreshAll();
            }
        }

        private static readonly string[] ProfileProperties =
        {
            nameof(Sensitivity), nameof(YXRatio), nameof(Rotation), nameof(Snap), nameof(SpeedCap),
            nameof(LRRatio), nameof(UDRatio), nameof(DomainX), nameof(DomainY), nameof(RangeX), nameof(RangeY),
            nameof(LpNorm), nameof(InputSmoothHalflife), nameof(ScaleSmoothHalflife), nameof(OutputSmoothHalflife),
            nameof(Separate),
        };

        private void EditProfile(Action<ProfileState> edit, bool valid = true)
        {
            if (State == null) return;
            if (!valid)
            {
                Status = "That value isn't allowed by the driver.";
                RefreshAll();
                return;
            }

            var next = State.Clone();
            edit(next);
            if (next.SameAs(State)) return;

            Push();
            selected.Current = next;
            RefreshAll();
        }

        #endregion

        #region Editing

        private void Push()
        {
            selected.History.Push(State);
            lastNudgeId = null;
        }

        private void SetCurrentArgs(ProfileState state, CurveArgs args)
        {
            if (editingY && state.Separate) state.Y = args;
            else state.X = args;
        }

        /// <summary> Replaces the curve being edited. Returns false if the driver would reject it. </summary>
        private bool ReplaceCurve(CurveArgs args, bool pushUndo, string what = null)
        {
            if (!HandleSets.IsUsable(args))
            {
                var errors = Validation.Validate(args);
                Status = $"Can't set {what ?? "that value"}: " + (errors.Count > 0 ? string.Join("; ", errors) : "the curve would be undefined");
                return false;
            }

            if (args.Equals(CurrentArgs)) return true;

            if (pushUndo) Push();
            var next = State.Clone();
            SetCurrentArgs(next, args);
            selected.Current = next;
            return true;
        }

        private void EditCurve(Action<CurveArgs> edit, string what)
        {
            if (CurrentArgs == null) return;
            var next = CurrentArgs.Clone();
            edit(next);
            ReplaceCurve(next, pushUndo: true, what: what);
            RebuildParameters();
            RefreshAll();
        }

        private void ChangeMode(CurveMode mode)
        {
            var prev = CurrentArgs;
            var next = prev.Clone();
            next.Mode = mode;

            if (mode == CurveMode.Lookup && next.ControlPoints.Count < 2)
            {
                next.ControlPoints = LookupHandles.SampleCurve(prev, Math.Max(view.MaxX, 10));
                next.SmoothLut = true;
            }

            next = HandleSets.For(mode).Normalize(next);

            if (!HandleSets.IsUsable(next))
            {
                // stored parameters for this mode are unusable: start from the driver defaults
                next = HandleSets.For(mode).Normalize(new CurveArgs { Mode = mode, Gain = prev.Gain, ControlPoints = next.ControlPoints, SmoothLut = next.SmoothLut });
            }

            ReplaceCurve(next, pushUndo: true, what: "mode");
            selectedHandleId = null;
            if (mode == CurveMode.Synchronous && !view.LogX) LogX = true;
            RebuildParameters();
            RefreshAll();
            ResetView();
        }

        private void ConvertToCustom()
        {
            var prev = CurrentArgs;
            var next = prev.Clone();
            next.Mode = CurveMode.Lookup;
            next.ControlPoints = LookupHandles.SampleCurve(prev, Math.Max(view.MaxX, 10), 14);
            next.SmoothLut = true;
            ReplaceCurve(next, pushUndo: true, what: "custom curve");
            selectedHandleId = null;
            RebuildParameters();
            RefreshAll();
            Status = "Converted to a custom curve. Drag points, double-click to add, right-click to delete.";
        }

        public void BeginDrag(string id)
        {
            dragging = true;
            dragPushed = false;
            SelectedHandleId = id;
        }

        public void MoveHandle(string id, Point2 target, bool isNudge)
        {
            if (CurrentArgs == null) return;

            var next = HandleSets.For(CurrentArgs.Mode).Drag(CurrentArgs, id, target, Context);
            if (next.Equals(CurrentArgs)) return;

            if (isNudge)
            {
                // a burst of arrow presses on one handle is a single undo step
                bool coalesce = lastNudgeId == id && (DateTime.UtcNow - lastNudgeTime).TotalMilliseconds < 1000;
                if (!coalesce) Push();
                lastNudgeId = id;
                lastNudgeTime = DateTime.UtcNow;
            }
            else if (!dragPushed)
            {
                Push();
                dragPushed = true;
            }

            var state = State.Clone();
            SetCurrentArgs(state, next);
            selected.Current = state;
            SelectedHandleId = id;
            RefreshAll(parametersOnly: true);
        }

        public void EndDrag(string id)
        {
            dragging = false;
            RefreshAll();
        }

        public void AddPoint(Point2 p)
        {
            if (!IsLookup) return;
            var next = CurrentArgs.Clone();
            int index = LookupHandles.AddPoint(next, p);
            if (index < 0)
            {
                Status = next.ControlPoints.Count >= LookupHandles.MaxControlPoints(next)
                    ? $"This curve already has the maximum number of points ({LookupHandles.MaxControlPoints(next)})."
                    : "A point already exists at that speed.";
                return;
            }
            if (ReplaceCurve(next, pushUndo: true, what: "point"))
            {
                SelectedHandleId = HandleIds.LutPoint(index);
                RefreshAll();
            }
        }

        public void RemoveHandle(string id)
        {
            if (!IsLookup || !HandleIds.TryParseLutPoint(id, out int index)) return;
            var next = CurrentArgs.Clone();
            if (!LookupHandles.RemovePoint(next, index))
            {
                Status = "A custom curve needs at least two points.";
                return;
            }
            if (ReplaceCurve(next, pushUndo: true, what: "point"))
            {
                SelectedHandleId = null;
                RefreshAll();
            }
        }

        private void Undo()
        {
            if (selected.History.TryUndo(State, out var prev))
            {
                selected.Current = prev;
                lastNudgeId = null;
                AfterHistoryChange();
            }
        }

        private void Redo()
        {
            if (selected.History.TryRedo(State, out var next))
            {
                selected.Current = next;
                lastNudgeId = null;
                AfterHistoryChange();
            }
        }

        private void Revert()
        {
            Push();
            selected.Current = selected.Applied;
            selected.Name = selected.AppliedName;
            AfterHistoryChange();
            Status = "Reverted to the last applied settings.";
        }

        private void AfterHistoryChange()
        {
            if (!State.Separate) editingY = false;
            OnPropertyChanged(nameof(EditingY));
            RebuildParameters();
            RefreshAll();
        }

        #endregion

        #region Parameters panel

        private void RebuildParameters()
        {
            Parameters.Clear();
            var a = CurrentArgs;
            if (a == null) return;

            ParameterField F(string label, string tip, Func<CurveArgs, double> get, Action<CurveArgs, double> set, double min = double.MinValue, double max = double.MaxValue) =>
                new ParameterField(label, tip, get, set, min, max, CommitParameter);

            switch (a.Mode)
            {
                case CurveMode.Classic:
                    Parameters.Add(F("Acceleration", "How quickly sensitivity grows", c => c.Acceleration, (c, v) => c.Acceleration = v, 1e-300));
                    Parameters.Add(F("Exponent", "Curvature (2 = linear sensitivity growth)", c => c.ExponentClassic, (c, v) => c.ExponentClassic = v, 1.0001, 100));
                    Parameters.Add(F("Input offset", "Speed where acceleration starts", c => c.InputOffset, (c, v) => c.InputOffset = v, 0));
                    if (a.CapY > 0) Parameters.Add(F("Cap", a.Gain ? "Gain cap (sensitivity levels off toward this)" : "Maximum sensitivity", c => c.CapY, (c, v) => c.CapY = v, 1e-6));
                    break;
                case CurveMode.Power:
                    Parameters.Add(F("Scale", "Speed scale", c => c.Scale, (c, v) => c.Scale = v, 1e-300));
                    Parameters.Add(F("Exponent", "Power the speed is raised to", c => c.ExponentPower, (c, v) => c.ExponentPower = v, 1e-6, 100));
                    Parameters.Add(F("Output offset", "Sensitivity at low speed", c => c.OutputOffset, (c, v) => c.OutputOffset = v, 0));
                    if (a.CapY > 0) Parameters.Add(F("Cap", a.Gain ? "Gain cap (sensitivity levels off toward this)" : "Maximum sensitivity", c => c.CapY, (c, v) => c.CapY = v, 1e-6));
                    break;
                case CurveMode.Natural:
                    Parameters.Add(F("Decay rate", "How quickly the limit is approached", c => c.DecayRate, (c, v) => c.DecayRate = v, 1e-9));
                    Parameters.Add(F("Limit", "Sensitivity approached at high speed", c => c.Limit, (c, v) => c.Limit = v, 1e-9));
                    Parameters.Add(F("Input offset", "Speed where acceleration starts", c => c.InputOffset, (c, v) => c.InputOffset = v, 0));
                    break;
                case CurveMode.Jump:
                    Parameters.Add(F("Jump speed", "Speed of the step", c => c.CapX, (c, v) => c.CapX = v, 1e-9));
                    Parameters.Add(F("Jump sensitivity", "Sensitivity after the step", c => c.CapY, (c, v) => c.CapY = v, 1e-9));
                    Parameters.Add(F("Smoothness", "0 = hard step, 1 = smoothest", c => c.Smooth, (c, v) => c.Smooth = v, 0, 1));
                    break;
                case CurveMode.Synchronous:
                    Parameters.Add(F("Sync speed", "Speed where sensitivity is 1", c => c.SyncSpeed, (c, v) => c.SyncSpeed = v, 1e-9));
                    Parameters.Add(F("Motivity", "Maximum sensitivity (minimum is 1/motivity)", c => c.Motivity, (c, v) => c.Motivity = v, 1.000001));
                    Parameters.Add(F("Gamma", "Transition speed (higher = faster)", c => c.Gamma, (c, v) => c.Gamma = v, 1e-9));
                    Parameters.Add(F("Smoothness", "Softness of the approach to motivity", c => c.Smooth, (c, v) => c.Smooth = v, 0, 1));
                    break;
            }

            var applied = AppliedArgs;
            foreach (var p in Parameters) p.Refresh(a, applied);
        }

        private bool CommitParameter(ParameterField field, double value)
        {
            var next = CurrentArgs.Clone();
            field.Apply(next, value);
            if (!ReplaceCurve(next, pushUndo: true, what: field.Label.ToLowerInvariant())) return false;
            RefreshAll();
            return true;
        }

        #endregion

        #region Refresh

        private void Raise() => CurveChanged?.Invoke(this, EventArgs.Empty);

        private void RefreshAll(bool parametersOnly = false)
        {
            var a = CurrentArgs;
            var applied = AppliedArgs;
            if (a != null)
            {
                foreach (var p in Parameters) p.Refresh(a, applied);
            }

            if (!parametersOnly)
            {
                OnPropertyChanged(nameof(SelectedMode));
                OnPropertyChanged(nameof(Gain));
                OnPropertyChanged(nameof(Legacy));
                OnPropertyChanged(nameof(GainLabel));
                OnPropertyChanged(nameof(LegacyLabel));
                OnPropertyChanged(nameof(HasGainOption));
                OnPropertyChanged(nameof(IsLookup));
                OnPropertyChanged(nameof(IsParametric));
                OnPropertyChanged(nameof(CapVisible));
                OnPropertyChanged(nameof(Capped));
                OnPropertyChanged(nameof(SmoothLut));
                foreach (var name in ProfileProperties) OnPropertyChanged(name);
            }

            if (IsLookup)
            {
                lutText = FormatLut(a.ControlPoints);
                OnPropertyChanged(nameof(LutText));
            }
            OnPropertyChanged(nameof(PointCountText));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtyText));
            UpdateSelectionText();
            CommandManager.InvalidateRequerySuggested();
            Raise();
        }

        public void UpdateSelectionText()
        {
            var a = CurrentArgs;
            var h = a == null || selectedHandleId == null ? null : Handles.FirstOrDefault(x => x.Id == selectedHandleId);
            if (h == null)
            {
                SelectionText = "Click a handle to select it · Shift = fine drag, Shift+Ctrl = finer, Alt = lock axis · wheel = zoom";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}: speed {1:G6}, sensitivity {2:G6}", h.Label, h.Position.X, h.Position.Y);

            var applied = AppliedArgs;
            foreach (var name in h.Affects)
            {
                var prop = typeof(CurveArgs).GetProperty(name);
                if (prop?.PropertyType != typeof(double)) continue;
                double v = (double)prop.GetValue(a);
                sb.AppendFormat(CultureInfo.InvariantCulture, " · {0} {1:G6}", name, v);
                if (applied != null && applied.Mode == a.Mode)
                {
                    double d = v - (double)prop.GetValue(applied);
                    if (Math.Abs(d) > 1e-12 * Math.Max(1, Math.Abs(v)))
                        sb.AppendFormat(CultureInfo.InvariantCulture, " (Δ {0}{1:G4})", d > 0 ? "+" : "", d);
                }
            }

            var step = NudgeStepProvider?.Invoke();
            if (step is Point2 s)
            {
                string sx = h.Constraint == HandleConstraint.YOnly ? "" : string.Format(CultureInfo.InvariantCulture, " ←→ {0:G3}", s.X);
                string sy = h.Constraint == HandleConstraint.XOnly ? "" : string.Format(CultureInfo.InvariantCulture, " ↑↓ {0:G3}", s.Y);
                sb.Append(" · arrow step").Append(sx).Append(sy);
            }

            SelectionText = sb.ToString();
        }

        #endregion

        #region View

        /// <summary> Default speed range: enough to show every handle and where the curve settles. </summary>
        private double DefaultMaxX()
        {
            var a = CurrentArgs;
            if (a == null) return 60;

            var positions = HandleSets.For(a.Mode).GetHandles(a, HandleContext.Default)
                .Where(h => h.Id != HandleIds.Point && h.Id != HandleIds.Bend && h.Kind != HandleKind.Level)
                .Select(h => h.Position.X)
                .Where(HandleSets.IsFinitePositive)
                .ToList();

            double max = positions.Count > 0 ? positions.Max() * (a.Mode == CurveMode.Lookup ? 1.15 : 2) : 60;
            return Math.Max(10, Math.Min(max, 5000));
        }

        public void ResetView()
        {
            double maxX = DefaultMaxX();
            view.SetWindow(view.LogX ? maxX / 1000 : 0, maxX, 0.5, 2);
            RecomputeContext();
            Fit();
        }

        public void Fit()
        {
            var curve = Curve;
            var ghost = Ghost;
            if (curve == null) return;

            Func<ICurve, double, double> value = (c, x) =>
                chartKind == ChartKind.Velocity ? c.Velocity(x) :
                chartKind == ChartKind.Gain ? c.Gain(x) : c.Sensitivity(x);

            var extra = chartKind == ChartKind.Sensitivity ? Handles.Select(h => h.Position).ToList() : new List<Point2>();
            if (ghost != null)
            {
                for (int i = 0; i <= 50; i++)
                {
                    double x = view.ScreenToX(view.Left + view.Width * i / 50);
                    extra.Add(new Point2(x, value(ghost, x)));
                }
            }

            view.FitY(x => value(curve, x), extra);
            if (chartKind != ChartKind.Sensitivity && view.MinY < 0 && chartKind == ChartKind.Velocity)
            {
                view.SetWindow(view.MinX, view.MaxX, 0, view.MaxY);
            }
            Raise();
        }

        #endregion

        #region Live mouse

        /// <summary>
        /// Converts a raw (already accelerated) mouse report into input speed on the applied curve,
        /// by inverting output speed = input speed × sensitivity. Returns null if it can't be placed.
        /// </summary>
        public double? LiveInputSpeed(double dx, double dy, double timeMs)
        {
            var applied = selected?.Applied;
            if (applied == null) return null;

            double counts = applied.Separate ? Math.Abs(editingY ? dy : dx) : Math.Sqrt(dx * dx + dy * dy);
            double factor = applied.Sensitivity * (applied.Separate && editingY ? applied.YXRatio : 1);
            if (factor <= 0 || counts == 0) return null;

            double outSpeed = counts / timeMs / factor;
            var ghost = Ghost;
            if (ghost == null) return null;

            double v = Solver.Bisect(x => ghost.Velocity(x), outSpeed, 1e-4, 1e5, logScale: true, clamp: false);
            return double.IsNaN(v) ? (double?)null : v;
        }

        #endregion

        #region Profiles, devices, apply

        public void AddProfile()
        {
            string name = PromptForText?.Invoke("New profile", "Name for the new profile (starts as a copy of the current one):", UniqueName(selected?.Name ?? "profile"));
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (Profiles.Any(p => p.Name == name))
            {
                Status = $"A profile named \"{name}\" already exists.";
                return;
            }

            var profile = new Profile { name = name };
            var state = State.Clone();
            state.WriteTo(profile);
            Config.profiles.Add(profile);
            Config.accels.Add(new ManagedAccel(profile));

            var editor = new ProfileEditor(profile, state) { AppliedName = null };
            Profiles.Add(editor);
            SelectedProfile = editor;
            RefreshAll();
            Status = $"Added profile \"{name}\". Assign it to a device under Devices…";
        }

        public void RenameProfile()
        {
            if (selected == null) return;
            string name = PromptForText?.Invoke("Rename profile", "New name:", selected.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name == selected.Name) return;
            if (Profiles.Any(p => p.Name == name))
            {
                Status = $"A profile named \"{name}\" already exists.";
                return;
            }

            foreach (var d in Config.devices.Where(d => d.profile == selected.Name))
            {
                d.profile = name;
                devicesDirty = true;
            }
            selected.Name = name;
            RefreshAll();
        }

        private void DeleteProfile()
        {
            if (Profiles.Count <= 1) return;
            var victim = selected;
            int index = Profiles.IndexOf(victim);

            foreach (var d in Config.devices.Where(d => d.profile == victim.Name))
            {
                // unassigned devices use the first profile
                d.profile = "";
                devicesDirty = true;
            }

            if (victim.AppliedName != null) deletedNames.Add(victim.AppliedName);

            int configIndex = Config.profiles.IndexOf(victim.Profile);
            Config.profiles.RemoveAt(configIndex);
            Config.accels.RemoveAt(configIndex);
            devicesDirty = true;

            Profiles.RemoveAt(index);
            SelectedProfile = Profiles[Math.Min(index, Profiles.Count - 1)];
            RefreshAll();
            Status = $"Deleted profile \"{victim.Name}\" (not written until you Apply).";
        }

        private string UniqueName(string baseName)
        {
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (Profiles.All(p => p.Name != candidate)) return candidate;
            }
        }

        public void DevicesChanged()
        {
            devicesDirty = true;
            RefreshAll();
            Status = "Device settings changed (not written until you Apply).";
        }

        private async Task ApplyAsync()
        {
            applying = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                foreach (var editor in Profiles)
                {
                    int i = Config.profiles.IndexOf(editor.Profile);
                    editor.Profile.name = editor.Name;
                    editor.Current.WriteTo(editor.Profile);
                    Config.SetProfileAt(i, editor.Profile);
                }

                var sidecar = settings.Sidecar;
                foreach (var name in deletedNames) sidecar.Profiles.Remove(name);
                foreach (var editor in Profiles)
                {
                    if (editor.AppliedName != null) sidecar.Profiles.Remove(editor.AppliedName);
                }
                foreach (var editor in Profiles)
                {
                    var entry = editor.Current.ToSidecarEntry();
                    if (entry != null) sidecar.Profiles[editor.Name] = entry;
                }

                string errors = await settings.ApplyAsync();
                if (errors != null)
                {
                    Status = errors;
                    System.Windows.MessageBox.Show(errors, "Raw Accel", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                foreach (var editor in Profiles)
                {
                    editor.Applied = editor.Current;
                    editor.AppliedName = editor.Name;
                }
                devicesDirty = false;
                deletedNames.Clear();
                Status = $"Applied at {DateTime.Now:T} and saved to {settings.SettingsPath}";
            }
            catch (Exception e)
            {
                Status = "Apply failed: " + e.Message;
                System.Windows.MessageBox.Show(e.Message, "Raw Accel", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                applying = false;
                RebuildParameters();
                RefreshAll();
            }
        }

        #endregion
    }
}
