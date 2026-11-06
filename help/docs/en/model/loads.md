# Forces, moments and distributed loads

Loads describe the prescribed actions in the two-dimensional bending plane.

## Point force

A point force `F` acts at a position `x`, including at a beam end. Its sign determines its direction.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>F &gt; 0</code> · upward</span><span><code>F &lt; 0</code> · downward</span></div>
  <img src="../assets/punktkraft-vorzeichen.png" alt="Point-force arrows: upward on the left, downward on the right">
</figure>

## Point moment

A point moment `M` acts at a position `x` without introducing an additional resultant shear force at that position.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>M &gt; 0</code> · counterclockwise</span><span><code>M &lt; 0</code> · clockwise</span></div>
  <img src="../assets/punktmoment-vorzeichen.png" alt="Point moments: counterclockwise on the left, clockwise on the right">
</figure>

Multiple point forces and point moments may act at the same position.

## Constant distributed load

A constant distributed load `q` acts uniformly from `xStart` to `xEnd`.

It is a **force per unit length**. The start and end positions define the loaded region, with `0 ≤ xStart < xEnd ≤ L`.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>q &gt; 0</code> · upward</span><span><code>q &lt; 0</code> · downward</span></div>
  <img src="../assets/streckenlast-vorzeichen.png" alt="Distributed-load arrows: upward on the left, downward on the right">
</figure>

Multiple distributed loads may overlap. Their signed intensities are added.

Triangular or otherwise varying distributed loads are currently not available as separate load types.

<details class="spandraft-details">
  <summary>Resultant of a constant distributed load</summary>
  <p>The resultant force is <code>Fq = q · (xEnd − xStart)</code> and acts at the center of the loaded region at <code>xq = (xStart + xEnd) / 2</code>. This equivalent force describes the global equilibrium. For deflection and internal-force calculations, SpanDraft continues to treat the load as distributed.</p>
</details>

## Self-weight

Self-weight is an optional distributed load derived from the current material and cross-section.

When enabled, SpanDraft calculates a downward constant load over the full beam length from the material density `ρ`, cross-sectional area `A`, and standard gravity:

`q_g = −ρ · A · g₀`, with `g₀ = 9.80665 m/s²`.

A density must be defined for the material before self-weight can be enabled.

The self-weight load is shown as `g`. This name is reserved for self-weight; existing supports or loads named `g` or `G` must be renamed first. Its value follows the current material density and cross-sectional area. Hiding the self-weight load affects only its display; disabling it removes the load from the mechanical model.

## Limits of the load model

Axial or inclined forces and torsional moments are currently not supported load types.

For the wider model limitations, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

Next: [Result diagrams and support reactions](../results/diagrams-reactions.md).
