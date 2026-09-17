# ARCHWALK decision log

Started from `06_RESEARCH_AND_DECISIONS.md`. Confirmed on Rhino 8.25.25328.11001.

| ID | Choice | Evidence |
| --- | --- | --- |
| I01 | Compile against NuGet `RhinoCommon` **8.25.25328.11001** (`lib/net7.0`) with plugin TFM `net8.0-windows`, `ExcludeAssets=runtime` | Package cache matches installed Rhino; SDK 8.0.419 pinned in `global.json` |
| I02 | `WH_GETMESSAGE` on the UI thread + viewport HWND clip/recenter; no `RegisterRawInputDevices` | P0A: scan-code W and Cyrillic `WM_CHAR` eaten; D13 kept |
| I03 | Release capture on Idle, not from inside the hook | Avoids `UnhookWindowsHookEx` during `GetMsgProc` |
| I04 | Camera apply via `ViewportInfo` frustum from authoritative `φv` and **actual** `RhinoViewport.Size` | P0B FOV error 0.00° on square / wide / tall live views |
| I05 | `SetCameraTarget(target, false)` after projection | Location did not move; target distance 5 m in SI |
| I06 | Preview: floating native `RhinoView`, not off-screen viewport copy | `DrawToBitmap(new RhinoViewport(src))` is null; floating view bitmap is valid |
| I07 | Observer store: `ReadDocument` / `WriteDocument` + `AddCustomUndoEvent` inside `BeginUndoRecord` | Headless Undo restores records; import modes do not replace the current list |
| I08 | Ground: `GetMeshes` / `CreateFromBrep` / `InstanceObject.Explode`, local `MeshRay` | Nested/mirrored instance meshes; unit scale rebuild |
| I09 | Isolated host tests use `RhinoDoc.CreateHeadless` | Does not write into the user's open model |
| I10 | Output `.rhp` to `bin/plugin/` | `bin/Debug` stays locked while Rhino holds the first loaded copy |
| I11 | Two-point counts as perspective in 8.25 `ViewportInfo`; ApplyPose calls `ChangeToPerspectiveProjection` when `IsTwoPointPerspectiveProjection` | P1-restore: walk from a two-point view became standard perspective; Backspace restored two-point |
| I12 | SessionController owns the walk hook; `InputSession` remains P0 standalone capture | Two `WH_GETMESSAGE` owners are never active together; `AWResetInput` releases both |
| I13 | `AWEnter` defers capture until Idle after the command | Matches 01: hide cursor only after the launching command ends |
| I14 | Ctrl+S / Ctrl+Z: do not eat the chord; end keep-view; then `_Save` / `_Undo` (tests set `SuppressHostScripts`) | Spec exception vs WASD suppression; host tests do not open the Save dialog |
| I15 | Right-button profile: relative look only while RMB is held; cursor leaving the view pauses | P1-rmb; live context-menu check remaining after restart |
| I16 | P2 placement draft lives in `PlacementController`, not SessionState | Walk capture stays Idle until Enter; Esc clears draft with no Undo |
| I17 | Live preview stays on owned floating `ARCHWALK_PREVIEW` view; panel shows `DrawToBitmap` | Same P0C path; working view unchanged |
| I18 | WinForms `ObserverPanel` registered as Rhino panel «Наблюдатель» | Windows-only v1; Eto not required |
| I19 | Default foot source in P2 UI is **По отметке**; Surface option probes ground and commits `MovementMode.Surface` | P2 placement; full follow in P4 |
| I20 | Placement Enter/Готово commits one `ObserverRecord` before walk | Draft never writes Undo until confirm |
| I21 | Named Views via `NamedViews.Add(name, viewportId)`; overwrite only with explicit Replace | P3-named-view |
| I22 | Unit scale: multiply foot document coords by `UnitsChangedWithScaling.Scale`; keep `EyeHeightMeters` | P3-units-scale |
| I23 | Surface follow: Core `ISupportField` + `SurfaceNavigator`; Rhino `GroundCache` XY grid + local `MeshRay` | P4 offline 48 tests |
| I24 | Camera uses `MotionCore.RenderPose` (smoothed eye Z); support always from physical foot | P4 A34 |
| I25 | Geometry/layer/attribute mutation ends walk and invalidates ground generation | P4 A38 |
| I26 | Local Yak `archwalk` Windows package; no public push from CI | P5 `packaging/Build-Yak.ps1` → `archwalk-1.0.0-rh8_25-win.yak` |
| I27 | Session Idle handler unsubscribed on Exit/failed enter | P5 A55; no capture bridge or Idle work when Idle |
| I28 | Clipping planes not applied to automatic ground in v1; use Level or explicit `GroundSupportFilter` | A40 documented limitation |

`RhinoDoc.Redo()` after custom undo on headless 8.25 returned false in-process. The swap callback is still implemented for command-scoped redo (P3).
