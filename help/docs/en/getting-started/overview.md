# Getting to know SpanDraft

SpanDraft is a visual beam analysis application for individual straight beams. A model is built step by step from the beam definition, supports, and loads. As soon as the model is solvable, SpanDraft updates the results automatically.

<ol class="spandraft-workflow">
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Define the project</span>
      <span class="spandraft-workflow-description">Set the length, material, and cross-section.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Define the supports</span>
      <span class="spandraft-workflow-description">Specify how the beam is restrained.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Add loads</span>
      <span class="spandraft-workflow-description">Place point forces, point moments, distributed loads, and optional self-weight.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Calculate automatically</span>
      <span class="spandraft-workflow-description">Once the model is solvable, SpanDraft updates the results automatically after confirmed changes.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Review the results</span>
      <span class="spandraft-workflow-description">Inspect deflection, shear force, bending moment, support reactions, and derived results.</span>
    </div>
  </li>
</ol>

The model can be saved as a `.spandraft` project at any time and edited again later.

## Important behavior in SpanDraft

The beam schematic is a technical representation and may be visually distorted for better readability. Supports and loads are positioned graphically, but their physical positions and values remain exactly as entered. The numerical values therefore define the model.

Results refer to the confirmed model and update automatically after confirmed changes.

In the beam editor, **Undo** and **Redo** apply to confirmed project changes. In the cross-section editor, they apply to confirmed changes to the current cross-section draft. Text that has not yet been confirmed uses the normal undo behavior of the input field.

Next: [First calculation](first-calculation.md)
