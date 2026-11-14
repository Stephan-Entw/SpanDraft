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

Multiple supports are allowed and do not create internal hinges; you place hinges explicitly (see below).

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

## Hinges

A **hinge** is a moment release in the beam: the bending moment is zero on both sides and the beam may kink there. Deflection, shear force, and axial force are transmitted. A Gerber beam is the typical example. The hinge tool sits behind the supports in the toolbar; the ring on the beam axis is placed, dragged, and edited like a support.

- A hinge lies **inside** the beam, not at an end, and at most one hinge is allowed at a position.
- It may lie on a Pinned Support, a Roller Support, or a Spring Support with a transverse spring only, at a segment boundary, under a distributed load, and at a point force. A hinge is not possible on a Fixed Support, on a Spring Support with a rotational spring (including a partial restraint), or at a point moment.
- Each hinge removes one restraint from the beam, so a beam with hinges needs correspondingly more supports: Pinned–Roller–Roller with one hinge is determinate, with two hinges it is a mechanism. SpanDraft detects mechanisms exactly and then reports that the support is insufficient or that there are too many hinges.

In the overview and in the report, the **Hinge results** table shows the rotation to the left `θ−` and to the right `θ+` of the hinge and the transmitted shear force `V`; at a hinge over a support or with a load the shear force jumps, and both values are shown. The deflection diagram shows the kink.

Choose the support type according to the actual movement constraints. For details, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

Next: [Forces, moments and distributed loads](loads.md).
