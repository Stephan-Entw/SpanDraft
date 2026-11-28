# Forces, moments and distributed loads

Loads describe the prescribed actions in the two-dimensional bending plane.

## Point force

A point force `F` acts at a position `x`, including at a beam end. Its sign determines its direction.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>F &gt; 0</code> · upward</span><span><code>F &lt; 0</code> · downward</span></div>
  <img src="../assets/punktkraft-vorzeichen.png" alt="Point-force arrows: upward on the left, downward on the right">
</figure>

## Point moment

A point moment `C` is an external moment applied at a position `x`. It causes no jump in the shear force there. In the toolbar, select **Moment** and enter the value of `C` in the **Moment M** field. The result diagram uses `M(x)` for the internal bending moment.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>C &gt; 0</code> · counterclockwise</span><span><code>C &lt; 0</code> · clockwise</span></div>
  <img src="../assets/punktmoment-vorzeichen.png" alt="Point moments: counterclockwise on the left, clockwise on the right">
</figure>

Multiple point forces and point moments may act at the same position.

## Distributed load

A distributed load acts from `xStart` to `xEnd` and is a **force per unit length**. The start and end positions define the loaded region, with `0 ≤ xStart < xEnd ≤ L`. It has two values: `q₁` at the start and `q₂` at the end. The intensity varies linearly in between.

- With `q₁ = q₂` the load is constant.
- If one value is zero, the load is triangular.
- If both values are nonzero and different, the load is trapezoidal.
- If `q₁` and `q₂` have opposite signs, the load changes its direction within the region.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>q &gt; 0</code> · upward</span><span><code>q &lt; 0</code> · downward</span></div>
  <img src="../assets/streckenlast-vorzeichen.png" alt="Distributed-load arrows: upward on the left, downward on the right">
</figure>

When you create a distributed load, `q₂` follows the value of `q₁` until you change `q₂` yourself. A constant load therefore needs one value; for a triangular or trapezoidal load, also enter `q₂`. In the drawing, the arrow height is proportional to the magnitude of the intensity relative to the larger of the two end values, and the arrow direction follows the sign.

Multiple distributed loads may overlap. Their signed intensities are added.

<details class="spandraft-details">
  <summary>Resultant of a distributed load</summary>
  <p>The resultant force is <code>Fq = (q₁ + q₂) / 2 · (xEnd − xStart)</code>. For a constant load it acts at the center of the loaded region. With a sign change it can be zero although the load produces a moment, for example an antisymmetric triangular load; SpanDraft therefore does not show a line of action of the resultant. This equivalent force describes the global equilibrium. For deflection and internal-force calculations, SpanDraft continues to treat the load as distributed.</p>
</details>

## Self-weight

Self-weight is an optional distributed load derived from the current material and cross-section; with several segments it is one constant load per segment from that segment's material and cross-section.

When enabled, SpanDraft calculates a downward constant load over the full beam length from the material density `ρ`, cross-sectional area `A`, and standard gravity:

`q_g = −ρ · A · g₀`, with `g₀ = 9.80665 m/s²`.

A density must be defined for the material before self-weight can be enabled.

The self-weight load is shown as `g`. This name is reserved for self-weight; existing supports or loads named `g` or `G` must be renamed first. Its value follows the current material density and cross-sectional area. Hiding the self-weight load affects only its display; disabling it removes the load from the mechanical model.

## Example: cantilever with distributed load and self-weight

A cantilever fixed at one end carries a constant distributed load over its full length; self-weight is enabled as well. The example shows how both distributed loads add up and how the fixed support takes both force and moment.

![Cantilever with fixed support on the left, distributed load q1 and self-weight g over the full length](../assets/beispiel-kragarm.png)

| Quantity | Value |
| --- | --- |
| Length | `L = 1.5 m` |
| Material | S235JR, `E = 210,000 N/mm²`, `ρ = 7850 kg/m³`, reference value `Re = 235 N/mm²` |
| Cross-section | Rectangle 40 × 80 mm, bending axis y-y, `A = 3200 mm²` |
| Support | Fixed support `A` at `x = 0` |
| Loads | Distributed load `q1 = −2 kN/m` from `x = 0` to `x = 1.5 m`; self-weight `g = ρ · A · g₀ = 246.3 N/m`, downward |

Expected results with `q = 2000 N/m + 246.3 N/m = 2246.3 N/m`:

- support force `A = q · L = 3.37 kN`, upward;
- fixed-end moment and largest bending moment `M = q · L² / 2 = 2.53 kN·m` at the fixed support;
- largest deflection `w = q · L⁴ / (8 · E · I) = 3.97 mm` at the free end;
- bending stress `σ = M / W = 59.2 N/mm²` at the fixed support.

[Download example file](../assets/examples/kragarm.spandraft)

## Limits of the load model

Axial or inclined forces and torsional moments are currently not supported load types.

For the wider model limitations, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

Next: [Result diagrams and support reactions](../results/diagrams-reactions.md).
