# Release notes

## 1.2.0

SpanDraft 1.2.0 expands the beam model with multiple beam segments, elastic supports, internal hinges and varying distributed loads. It also brings more control over number formatting and several improvements to reports and editing.

### New

- **Model beams with changing properties.** Split a beam into segments with their own material, cross-section and bending axis. When self-weight is enabled, SpanDraft accounts for each segment individually and identifies the segment with the lowest safety factor in the overview and report.
- **Keep supports and loads tied to the beam geometry.** Position supports, loads and hinges relative to the start of a segment or attach them to its end. Their position follows the segment automatically when the beam geometry changes.
- **Add elastic supports.** Translational and rotational springs can be added independently or combined at the same position. A rotational spring can also be combined with rigid transverse restraint to model a partial restraint.
- **Release bending moments with internal hinges.** Model Gerber beams and other systems with moment releases within a continuous beam. The overview and calculation report show the rotations on both sides of an internal hinge and the shear force at the hinge.
- **Apply triangular and trapezoidal distributed loads.** Define different load intensities at the start and end of a distributed load, including loads that change direction along the beam. Both values are shown in the editor, overview and report.
- **Read release notes in SpanDraft.** The “What's new” menu item opens the English or German release notes in the app’s interface language.

### Improved

- **Choose how numbers are rounded.** Select from five display rounding levels and preview the effect directly in Settings. Rounded input values are marked with `≈`, while calculated results are rounded without an approximation marker. The calculation report records the selected rounding level, and very small or large values can optionally be displayed using powers of ten.
- **Read calculation results more easily.** Report tables for beam segments and internal hinges have been reorganized, and result diagram axes now use consistent number formatting. Calculation messages also give clearer guidance when support spacing or stiffness prevents a reliable result.
- **Edit with fewer extra steps.** Cross-section changes can be saved without pressing Enter to confirm the last input first. The editor toolbar adapts to limited space, and selected built-in materials now include clearer notes about where their reference values apply.

**Compatibility:** Earlier projects can be opened as usual. Once saved in SpanDraft 1.2.0, they use the new V5 project format and require version 1.2.0 or later.

## 1.1.0

- Add optional beam self-weight to the model, editor and calculation reports.
- Add a pinnable result probe to inspect deflection, rotation, shear force and bending moment at a position along the beam.
- Make load editing more compact and add buttons to reverse load signs.
- Refine the desktop interface, unify typography across the application and reports, and add hatching to result diagrams.
- Add an English offline user manual and switch between languages without leaving the current topic.
- Add German and English privacy notices accessible from the application.
- Make the project author optional; the calculation title remains required for new projects.

**Project compatibility:** SpanDraft 1.1.0 can open projects from earlier versions. Saving a project writes it in format V4, which SpanDraft 1.0.0 cannot open.

## 1.0.0

SpanDraft 1.0.0 is the first stable release.

- Add a complete calculation report with integrated preview, PDF export and Windows printing.
- Improve material handling with clearer material categories, explicit Re/Rp0.2 strength values and consistent material presentation throughout the application and reports.
- Refine cross-section setup, technical notation, numeric formatting and configurable unit and regional settings.
- Improve the project workflow with project information, more consistent editing controls and better handling of recent and recovered projects.
- Improve beam, load, diagram and report presentation for clearer technical drawings and more consistent layouts.
- Improve Windows integration with a dedicated `.spandraft` project icon, file association and more robust installer validation.

## 0.1.0

First public version of SpanDraft for Windows x64.

- Define a straight beam and place supports, point forces, point moments and
  constant distributed loads directly in the graphical editor.
- Choose materials and parametric or manual cross-sections, with reusable local
  material and section libraries.
- See automatic analysis results, support reactions and live diagrams for
  deflection, shear force and bending moment, plus elastic bending stress and
  yield-strength safety factor.
- Save and reopen projects, undo and redo edits, and recover after a crash.
- Use German or English interface text, configurable units and the included
  German offline manual.
