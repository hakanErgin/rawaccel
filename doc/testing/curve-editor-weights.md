# Manual check: range and domain weights in the curve editor

Checks for the chart changes that make the editor apply a profile's domain and range weights the way the driver does. Tests and a compile cover the maths, but the interactions need a run on Windows.

## Get the build

Every push to the pull request builds the editor automatically on Windows.

1. Open the pull request's **Checks** tab, pick the latest **curve-editor** run, and download the **preview-release** artifact under *Artifacts* (signed in to GitHub).
2. Unzip it, then unzip `rawaccel-editor-x64.zip` inside and follow its `INSTALL.txt`: back up `wrapper.dll` in your Raw Accel folder, then copy the files there.

## Setup

Use a profile with weights other than 1, for example *Domain weight X* = 2 and *Range weight X* = 0.5.

## Checks

1. **Curve shape.** The curve looks stretched and flattened compared with weights of 1. After **Apply**, the dashed applied line changes the same way.
2. **Handles.** Dragging a handle keeps it on the drawn curve. With *Range weight X* = 0 the curve is flat, and handles only move sideways. Clicking to add a point on a custom curve shows a message instead.
3. **Vertical line.** With one curve for both axes (by-component off), set different Y weights, or a *Y/X ratio* other than 1. A dotted line labelled *vertical* appears. **Home** keeps it in view, even with a large Y/X ratio.
4. **Live dot.** Apply, then move the mouse:
   - straight sideways: the green dot sits on the solid line;
   - straight up and down: it sits on the dotted line;
   - diagonally: it lands between the two.
5. **Peak marker.** Do a fast swipe, then untick *Include base sensitivity*. The peak marker moves with the curve instead of staying at the old height.
6. **By component.** Turn by-component on and edit the Y curve. The chart uses the Y weights, and the live dot follows vertical movement.
7. **No regression with weights of 1.** Set every weight back to 1. The chart, handles and live dot behave as before this change, and no dotted line shows unless *Y/X ratio* isn't 1.

## Reporting

Note what you did, what you expected and what you saw, with a screenshot if you can. Add it as a comment on the pull request.
