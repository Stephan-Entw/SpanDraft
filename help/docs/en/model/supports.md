# Supports and boundary conditions

Supports constrain the axial displacement `u`, transverse displacement `w`, or rotation `θ` at a beam position.

The position `x` is an exact model value and may also lie inside the beam. Only one support can be placed at a given position.

<div class="spandraft-support-table">
  <table>
    <thead>
      <tr><th scope="col">Support</th><th scope="col">Symbol</th><th scope="col">Constrained movements</th><th scope="col">Possible reactions</th></tr>
    </thead>
    <tbody>
      <tr><th scope="row">Fixed Support</th><td><img src="../assets/lager-einspannung.png" alt="Fixed Support" width="96" height="76"></td><td><code>u</code>, <code>w</code>, <code>θ</code></td><td><code>Rx</code>, <code>Ry</code>, <code>M</code></td></tr>
      <tr><th scope="row">Pinned Support</th><td><img src="../assets/lager-festlager.png" alt="Pinned Support" width="96" height="76"></td><td><code>u</code>, <code>w</code></td><td><code>Rx</code>, <code>Ry</code></td></tr>
      <tr><th scope="row">Roller Support</th><td><img src="../assets/lager-loslager.png" alt="Roller Support" width="96" height="76"></td><td><code>w</code></td><td><code>Ry</code></td></tr>
    </tbody>
  </table>
</div>

The movements `u`, `w`, and `θ` are explained under [Coordinate system and units](coordinates-units.md).

## Providing sufficient restraint

A Fixed Support can restrain the beam on its own.

Another typical arrangement is a Pinned Support combined with a Roller Support at a different position. A Pinned Support on its own still allows rotation about its support point.

Multiple supports are allowed and do not create internal hinges.

Two Roller Supports restrain vertical rigid-body motion but leave axial rigid-body motion free. In that case, SpanDraft cannot solve the model even if no external axial load is applied.

The supports are idealized. Constrained movements are set to zero, and a Roller Support always constrains the transverse displacement `w`.

Friction, clearance, support flexibility, and prescribed support displacements are not included.

Choose the support type according to the actual movement constraints. For details, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

Next: [Forces, moments and distributed loads](loads.md).
