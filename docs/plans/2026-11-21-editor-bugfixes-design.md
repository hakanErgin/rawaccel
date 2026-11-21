# Curve editor bug fixes (profiles, settings files, chart)

Status: implemented and reviewed on branch `worktree-glistening-marinating-parasol` (base `c892fdc`); Windows CI build and manual test pending.

## Scope

Fix the bugs found in a read-through of the curve editor, plus the Cap checkbox
round trip the user reported. Everything is in `curve-editor/` and
`curve-editor-core/`; the driver, wrapper and grapher are unchanged.

The user asked for the stricter review because some fixes change how
`settings.json` and `curve-editor.json` are saved and loaded.

### Settings files (user data)

1. **Untouched profiles are written back unchanged.** `ProfileEditor.Saved` holds
   the state the wrapper `Profile` currently contains. Apply calls
   `ProfileState.WriteTo` only when `Current` differs from `Saved`, so a profile
   loaded with an `in_out` or `input` cap mode keeps it. Before, every Apply
   rewrote every profile in the editor's normalized form (cap mode `output`).
   The name is still assigned and `SetProfileAt` still runs for every profile,
   because the driver gets the name from the native copy.
2. **The driver rejects a write after `settings.json` is saved.**
   `SettingsService.ApplyAsync` now returns `(Error, Saved)`. When the file was
   written, the view model keeps the profile order it wrote (the picked profile
   first) instead of moving it back. The switch still shows as pending, because
   pending is now judged against what the driver runs (item 6).
3. **Sidecar keys for the current names are removed before rewriting.** Apply
   already removes keys for each profile's applied name and for deleted
   profiles. It now also removes each current name and the name each profile
   had when `settings.json` was last written (`SidecarName`), so entries saved
   by a rejected write don't stay, whether the profile is no longer a custom
   curve or was renamed again since.
4. **Malformed files.**
   - `settings.json`: any exception while reading or converting it (for example
     a `TargetInvocationException` from `AccelArgs.OnDeserialized` when `"data"`
     is missing) now falls back to the driver's settings with a warning. Before,
     only `JsonException`/`IOException` were caught; anything else reached
     `MainWindow.OnLoaded`, which said "Could not read settings from the driver"
     and closed. `OutOfMemoryException` is not caught.
   - `curve-editor.json`: `CustomCurveSidecar.Parse` turns explicit nulls
     (`"profiles": null`, null profile entries, `"maxY": null`) into empty
     values, and `Restore` ignores an entry whose `"points"` is null.
5. **Screenshot mode** (`--screenshot`, CI) no longer reports `LoadedFromFile`,
   so it never reads the real driver to match profiles.

### Which profile is active

6. **What the driver runs is tracked.** `DeviceLayout` copies the first profile
   name, the profile names, the default device config and every device's id,
   profile and config.
   - It is taken from the driver on open (from `settings.json`'s own config
     when there is no usable file, since that config *is* the driver's).
   - After a successful startup activation or Apply, it becomes the config
     that was sent.
   - It is null when the driver can't be read.

   Using it:
   - `PendingSwitch` is also true when the selected profile at index 0 isn't
     the driver's first profile. This fixes "All changes applied" being shown
     when the driver uses another profile.
   - Device changes and the set of profiles (a deleted profile, say) are judged
     by comparing with the driver's (`DevicesDiffer`), instead of a flag set on
     every Devices… OK or Delete. When the driver's state is unknown, the old
     flag is used.
   - Live mouse input (`UsesSelectedProfile`) routes devices by the driver's
     layout and the profile's applied name, because the marker is drawn on the
     driver's curve.
7. **A new profile has no applied state** (`Applied = null`): no ghost curve, no
   Revert, no live movement until it is applied.
8. **Revert is blocked while Apply runs** (command disabled, plus a
   `BlockedByApply` check).
9. **Profile names are limited to 255 characters** (driver `MAX_NAME_LEN` 256
   including the terminator).

### Curve and chart

10. **Cap checkbox.** On an uncapped Classic/Power curve, ticking Cap now adds an
    input cap at the anchor speed, converted to the equivalent output cap
    (Normalize). The curve below the cap and every other parameter stay the
    same, so unticking gives back the exact previous settings. Before, a gain
    curve's acceleration was recomputed from the cap point, so tick+untick left
    a different curve. If that fails validation, the old drag-based path is
    used. `CapX` is kept (output cap mode ignores it).
11. `ProfileState.SameAs` uses `CurveArgs.SameAsSaved`: custom-curve points and
    smoothing only count in lookup mode. They are never saved in other modes, so
    trying Custom curve and switching back isn't an unsaved change.
12. **View limits.** `ViewState.SetWindow` ignores NaN windows and clamps x to
    `MaxViewX = 1e6` and y to ±`MaxViewY = 1e9`, and keeps a minimum span that
    stays representable at those limits (relative to the values, moved back
    inside the limit if needed). Zooming out on a log axis no longer overflows
    to infinity/NaN. Home's automatic value top shows the clamped value. Pan keeps the span at both ends of a log
    axis and at the right end of a linear one. The Home fields, and Home values
    already saved, are clamped to the same limits.
13. **Keys during a handle drag** are swallowed (except modifier keys, which
    change drag precision through the mouse path). Delete, arrows, +/-, Tab, F,
    Home and Z no longer act mid-drag.
14. **Natural curve with Limit 1.** The decay handle is hidden (it would sit on
    the offset handle and does nothing). A Limit drag from a flat curve starts
    the knee halfway to the anchor instead of dividing by zero.
15. `LookupHandles.SampleCurve` uses a default range (10) for a missing or too
    small maxX. `Validation` rejects lookup values that are NaN or outside float
    range, or that become 0 or out of order as floats. The control points are
    checked with these rules before the table is built, because smoothing
    such points would overflow the spline and throw.
16. **Separate toggle.** Ticking Separate copies the horizontal curve to Y.
    Unticking now restores Y from the driver's state, or from the saved state
    when the driver's is unknown (a new profile, or the driver couldn't be
    read), so the profile isn't left changed. Before, only the driver's state
    was used, and a new profile kept the copy. If the baseline was itself
    separate, Y is kept.
17. **Log speed axis after Synchronous.** Switching to Synchronous turns the
    log axis on, as before, and remembers that the editor did it. When the
    curve shown stops being Synchronous (mode change, undo, another profile or
    the vertical curve), the axis goes back to linear. A manual tick or untick
    of "Log speed axis" clears the flag, so the user's choice stays.

## Not changed (decisions)

- Startup activation and Apply aren't serialized in the editor: the driver's
  control queue is sequential (`driver.cpp:586`), so writes land in order.
- The live marker ignores device DPI on purpose: the DPI factor cancels between
  input speed and output (`rawaccel.hpp` `modify`).

## Acceptance criteria

- Cap tick then untick (and untick then tick) on Classic/Power, gain and legacy,
  restores the exact `CurveArgs` (core test
  `CapThenUncapGivesBackTheSameSettings`).
- Apply on an unedited profile leaves its `settings.json` fields as loaded.
- With apply-on-open off and the driver on another first profile, the editor
  shows the switch as pending and doesn't call the file's first profile active.
- A driver rejection after the file is written keeps the list in file order and
  the switch pending.
- Broken `settings.json` (missing `"data"`) or `curve-editor.json` (nulls) opens
  the editor with a warning or defaults instead of closing it.
- Zooming out thousands of notches on either axis type keeps a finite, drawable
  window (`ZoomingOutStopsAtTheAxisLimits`).
- Ticking then unticking Separate on a new profile leaves it unchanged
  (no unsaved mark).
- Classic → Synchronous → Classic leaves the speed axis linear; with "Log speed
  axis" ticked by hand first, it stays log.
- Core tests pass (181); the Windows CI build compiles the WPF editor.

## Verification

- Core tests: `dotnet test curve-editor-core-tests` in `mcr.microsoft.com/dotnet/sdk:8.0`
  (Linux docker), 181/181 passed.
- The WPF editor, wrapper and wrapper tests build only on Windows (GitHub
  Actions `curve-editor.yml`, windows-2022). Not run yet: the branch isn't
  pushed.
- No manual Windows test yet. Items 16 and 17 are WPF view-model code with no
  core test; they rely on review and the Windows build.

## Review disposition

- Decision review (Codex, correctness + security lenses). It runs after
  implementation, because the review level was decided after the code was
  written.
  - Round 1: ACTIONABLE, the same two P2 findings from both lenses, both fixed:
    D1, deleting a profile no device used left the editor "All changes
    applied" (now the profile set is compared too); D2, panning at the ±1e9
    value limit collapsed the window to zero height (now a representable
    minimum span). Defect classes checked: other edit kinds missed by the
    driver comparison (add, rename, reorder, default device config: covered);
    other zero-span divisions (`XToScreen`, `ScreenToX`, `YToScreen`: covered
    by `KeepSpan` on both axes).
  - Round 2: security CLEAN; correctness ACTIONABLE with one P3, fixed: D3,
    Home values saved before the limits existed bypassed them (now clamped on
    read in `HomeMaxSpeed` and `SavedMaxY`). Defect class: saved preferences
    read without the limits enforced on write; the other saved values
    (`applyOnStartup`, custom-curve points, which are checked against the
    driver table) are not affected.
  - Round 3: started as the confirmation round, cancelled before a verdict
    when the user asked to fix items 16 and 17 too (no round consumed).
    Re-run on the new state: CLEAN on both lenses. Decision gate closed.
- Final review: dual (`review-codex` correctness + security lenses, and
  `review-claude`).
  - Round 1: Codex security CLEAN. Codex correctness ACTIONABLE, fixed:
    F1 (P2) smoothed lookup points beyond float range threw in validation
    instead of being rejected (now checked before building the table);
    F2 (P3) a sidecar entry saved under an intermediate name by a rejected
    Apply stayed after a later rename (now `SidecarName` is removed too);
    F3 (P3) Home's automatic value top showed the unclamped value. Claude
    ACTIONABLE, fixed: F4 (P3) this doc pointed to item 4 instead of item 6.
    Defect classes checked: F1, other places building a lookup table from
    unvalidated points (only `CurveMath.Create`; drags stay inside the view
    limits; values loaded from `settings.json` are out of scope, unchanged
    by this work); F2, other name-keyed sidecar cleanups (Delete had the same
    gap and now queues `SidecarName` as well); F3, other Home values stored
    before clamping (none: `SavedOrFitY` and `FitX` read the view).
  - Round 2: Codex correctness and Claude CLEAN. Codex security ACTIONABLE:
    F5 (P2) the F1 check missed float underflow (speeds like 1e-310 become 0
    as floats, then smoothing divides by zero and throws). Same defect as F1,
    so it counts as a repeat after an attempted fix. Fixed by checking the
    raw points with all the table rules at float precision (finite, positive,
    strictly increasing) before building, with a smoothing math error turned
    into a validation message as a safety net. Defect class: comparing in
    double where the driver stores float; the other checks are on values the
    driver keeps as double.
  - Round 3: CLEAN from all three (Codex correctness, Codex security,
    Claude). The user chose to run it after the F5 repeat was escalated.
  - The Windows CI build (the project's full check) runs after the push.

## Next steps

1. Done: decision review and final dual review, all findings fixed.
2. Ask the user before pushing (CI compiles the WPF app) and opening a PR.
3. The user tests on Windows (checklist in the PR).
