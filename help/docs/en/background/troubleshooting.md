# Troubleshooting

If SpanDraft does not calculate a result or a result looks unexpected, most causes can be narrowed down by checking the supports, loads, geometry, units, and model limitations.

## No results

| Situation | Check |
| --- | --- |
| **Calculation unavailable · Supports missing** | Add the required supports. |
| **Calculation unavailable · Insufficient support** | Are all rigid-body movements restrained? |
| **Calculation unavailable · Incomplete model** | Check the reported inputs. |
| **Calculation not possible** | Check the geometry and unusual ratios between dimensions. Numerically problematic models may cause the calculation to stop. |
| A change has not yet been applied | Confirm the input. |

A common case is a beam with two Roller Supports at different positions. They prevent vertical rigid-body motion but leave the entire beam free to move axially.

A typical simple support arrangement therefore uses a Pinned Support together with a Roller Support. See [Supports and boundary conditions](../model/supports.md).

## Input is not applied

SpanDraft applies only inputs from which a valid model can be created. The specific error message normally indicates which value must be corrected.

<details class="spandraft-details">
  <summary>Typical invalid inputs</summary>
  <ul>
    <li>Position outside <code>0 ≤ x ≤ L</code>.</li>
    <li>Distributed load without a valid range <code>xStart &lt; xEnd</code>.</li>
    <li>Two supports at the same position.</li>
    <li>Shortening the beam would leave existing loads or supports outside the beam.</li>
    <li>Geometrically invalid cross-section.</li>
    <li>Invalid material values: properties must remain within the physical ranges accepted by SpanDraft.</li>
    <li>Duplicate name.</li>
  </ul>
</details>

### Self-weight cannot be enabled

Self-weight requires a density for the selected material. If no density is available, SpanDraft displays:

**Self-weight cannot be included. No density is specified for the material.**

The name `g` is reserved for self-weight. If an existing support or load already uses `g` or `G`, rename that entity before enabling self-weight.

## A result looks implausible

Check the model in this order:

1. **Loads:** Are the magnitude, direction, position, and loaded region correct?
2. **Supports:** Does the idealized support arrangement represent the real situation?
3. **Beam and cross-section:** Are the beam length, cross-section dimensions, and bending axis correct?
4. **Material and units:** Are the material properties and selected display units plausible?
5. **Model limitations:** Does the problem fit the [two-dimensional SpanDraft beam model](model-assumptions.md)?

If necessary, a simple reference load case or an independent equilibrium check can provide an additional plausibility check.

An example is shown under [First calculation](../getting-started/first-calculation.md).

The results are explained under [Result diagrams and support reactions](../results/diagrams-reactions.md) and [Bending stress and safety factor](../results/stress-safety.md).

Very small nonzero values remain visible. With automatic engineering notation, they may be displayed using a power of ten. The calculation continues to use the unrounded value.

<details class="spandraft-details">
  <summary>Problems with project or library files</summary>
  <ul>
    <li>Projects store their material and cross-section data independently of the libraries.</li>
    <li>Damaged files are not repaired automatically.</li>
  </ul>
  <p>If a project file is damaged, use an intact backup whenever possible.</p>
</details>

## Recovery after a crash

After an unexpected shutdown, SpanDraft may offer to restore a local unsaved editing state.

The original project file is not overwritten automatically. Review the restored state and then save it deliberately.
