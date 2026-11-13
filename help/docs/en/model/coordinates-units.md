# Coordinate system and units

## Beam coordinates

![Beam longitudinal axis and positive movement directions](../assets/koordinaten.png)

| Quantity | Positive direction |
| --- | --- |
| `x`, `u` | to the right |
| `w` | upward |
| `θ` | counterclockwise |

`x = 0` is at the left end and `x = L` at the right end.

Positions are actual model coordinates and remain independent of any visual distortion used to improve readability of the schematic.

The [load sign conventions](loads.md) and [reaction sign conventions](../results/diagrams-reactions.md#support-reactions-in-the-project-overview) are described with the respective quantities.

The beam coordinates `x/u/w/θ` should not be confused with the local cross-section axes `y/z` or `u/v`; see [Beam, material and cross-section](material-cross-section.md).

## Units

SpanDraft provides the profiles **Mechanical engineering** (default), **Structural engineering**, and **US customary**, as well as custom combinations.

<div class="spandraft-unit-table" data-collapsible-units>
  <table>
    <thead><tr><th scope="col">Quantity</th><th scope="col">Mechanical engineering</th><th scope="col">Structural engineering</th><th scope="col">US customary</th></tr></thead>
    <tbody>
      <tr><th scope="row">Beam length / position</th><td>mm</td><td>m</td><td>ft</td></tr>
      <tr><th scope="row">Force</th><td>N</td><td>kN</td><td>kip</td></tr>
      <tr><th scope="row">Moment</th><td>N·m</td><td>kN·m</td><td>kip·ft</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Section dimensions</th><td>mm</td><td>mm</td><td>in</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Deflection</th><td>mm</td><td>mm</td><td>in</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Section area <code>A</code></th><td>mm²</td><td>cm²</td><td>in²</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Second moment of area <code>I</code></th><td>mm⁴</td><td>cm⁴</td><td>in⁴</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Section modulus <code>W</code></th><td>mm³</td><td>cm³</td><td>in³</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Distributed load</th><td>N/m</td><td>kN/m</td><td>kip/ft</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Stress</th><td>MPa</td><td>MPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Young’s modulus</th><td>GPa</td><td>GPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Strength value</th><td>MPa</td><td>MPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Density</th><td>kg/m³</td><td>kg/m³</td><td>kg/m³</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Rotation</th><td>rad</td><td>rad</td><td>rad</td></tr>
    </tbody>
  </table>
  <button type="button" class="spandraft-unit-toggle" data-show-more="Show more units" data-show-less="Show less" aria-expanded="false" hidden>
    <span class="spandraft-unit-toggle-label">Show more units</span>
    <span class="spandraft-unit-toggle-icon" aria-hidden="true">⌄</span>
  </button>
</div>

Changing units affects presentation only. Internally, SpanDraft calculates with SI values; the mechanical model and physical results remain unchanged.

A value that is currently being edited may need to be confirmed or canceled before the units can be changed.

### Number formatting

All displayed numbers – given values, library values and results – follow the same rule. **Significant digits** sets their number `n`, from 1 to 5 (default: 3). Each value is rounded on its own, in the selected unit:

- Digits before the decimal separator are never lost: `1234.5 N` is displayed as `1 235 N`, and `12.375 mm` as `12.4 mm`.
- Halves are rounded away from zero, and trailing zeros are omitted (`12.0 mm` becomes `12 mm`, `999.6 N` becomes `1 000 N`).
- A safety factor is never rounded up: `0.9995` is displayed as `0.999`. Exactly zero is displayed as `0`, and an infinite safety factor as `∞`.

**Given values** – lengths, positions, loads, cross-section dimensions and material and section properties – are marked with `≈` when the display differs from the entered value: `800 mm` stays `800 mm`, while `800.000004 mm` is displayed as `≈ 800 mm`. In labels, `≈` replaces the equals sign, for example `F1 ≈ 800 mm`. Exact values and exact conversions carry no mark: `25.4 mm` is displayed in inches as `1 in`. While editing, the input field always contains the full value in decimal notation, for example `0.0000042`.

**Calculated values** – reactions, internal forces, stresses, deformations, safety factors, section properties calculated from geometry and the self-weight – are rounded the same way but never carry `≈`.

**Powers of ten for very small and large values** (default: on) displays rounded magnitudes below `0.001` or at least `1 000 000` using powers of ten in steps of three, for example `1.23 · 10⁶ N` or `420 · 10⁻⁶ mm`; the mantissa then has `n` significant digits.

When it is cleared, all values remain in decimal notation.

Digit groups are separated by narrow non-breaking spaces. The decimal separator follows the selected regional format.

The **Preview** next to the settings shows one input and two results in the selected units as they will be displayed after **OK**. Both settings apply in the application and the report; the report states the number of digits in its “Calculation model” section. The calculation and stored inputs remain unchanged. Diagram axis ticks use their own scaling.
