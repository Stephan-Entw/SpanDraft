# Supports and boundary conditions

Supports constrain the axial displacement `u`, transverse displacement `w`, or rotation `θ` at a beam position. A Spring Support holds them elastically.

The position `x` is an exact model value and may also lie inside the beam. Only one support can be placed at a given position; a Spring Support cannot share its position with any other support.

<div class="spandraft-support-table">
  <table>
    <thead>
      <tr><th scope="col">Support</th><th scope="col">Symbol</th><th scope="col">Constrained movements</th><th scope="col">Possible reactions</th></tr>
    </thead>
    <tbody>
      <tr><th scope="row">Fixed Support</th><td><img src="../assets/lager-einspannung.png" alt="Fixed Support" width="96" height="76"></td><td><code>u</code>, <code>w</code>, <code>θ</code></td><td><code>Rx</code>, <code>Ry</code>, <code>M</code></td></tr>
      <tr><th scope="row">Pinned Support</th><td><img src="../assets/lager-festlager.png" alt="Pinned Support" width="96" height="76"></td><td><code>u</code>, <code>w</code></td><td><code>Rx</code>, <code>Ry</code></td></tr>
      <tr><th scope="row">Roller Support</th><td><img src="../assets/lager-loslager.png" alt="Roller Support" width="96" height="76"></td><td><code>w</code></td><td><code>Ry</code></td></tr>
      <tr><th scope="row">Spring Support</th><td><img src="../assets/lager-federlager.png" alt="Spring Support" width="96" height="76"></td><td><code>u</code>; <code>w</code> and <code>θ</code> elastic</td><td><code>Rx</code>, <code>Ry</code> and/or <code>M</code></td></tr>
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

Friction, clearance, and prescribed support displacements are not included. Support flexibility is only modeled where you add a Spring Support.

## Spring supports

A Spring Support holds the beam axially rigidly (`u = 0`) and carries a **transverse spring**, a **rotational spring** or both. Each spring has a stiffness greater than zero:

- The transverse spring (stiffness `k`, for example in N/mm) pushes back with the force `R = −k · w`.
- The rotational spring (stiffness `kθ`, for example in kN·m/rad) pushes back with the moment `C = −kθ · θ`.
- With a rotational spring and no transverse spring, **Rigid in w** holds the transverse displacement rigidly. This is a partial restraint: the beam cannot move at the support, but its rotation is elastic.

The spring force or moment is reported as the reaction `Ry` or `M`; `Rx` is zero. A single spring is not enough to hold the beam: a transverse spring alone leaves the rotation about its position free, and a rotational spring alone leaves the translation free. A beam held only by springs is solvable if the springs hold two transverse positions, or one transverse position and a rotation.

SpanDraft has no stiffness limit and replaces no rigid support by a spring: a very stiff spring is a spring, not a rigid support. For extreme stiffnesses or springs that are extremely close together, SpanDraft may reject the model as numerically not reliably solvable instead of returning a doubtful result.

The stiffness units can be selected in the settings.

Choose the support type according to the actual movement constraints. For details, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

Next: [Forces, moments and distributed loads](loads.md).
