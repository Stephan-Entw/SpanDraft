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

SpanDraft distinguishes two kinds of numbers.

**Given values** – lengths, positions, loads, cross-section dimensions and stored material and section properties – are displayed as they were given: `800 mm` stays `800 mm`, and `800.000004 mm` stays `800.000004 mm`. Result precision and engineering notation do not apply to them, and zeros are not added.

Only if a value would need more than 12 digits, for example after changing units, SpanDraft shows an approximation marked with `≈`, rounded to 3 digits but never more coarsely than to one whole unit, such as `≈ 3.94 in` or `≈ 3 937 in`. The full value remains available while editing.

**Calculated values** – reactions, internal forces, stresses, deformations, safety factors, section properties calculated from geometry and the self-weight – are rounded. **Result precision** sets between 1 and 15 significant digits for them (default: 3): `12.356 mm` is displayed as `12.4 mm`, and `12 mm` as `12.0 mm`. In decimal notation, a value is never rounded more coarsely than to one whole unit: `1234.56 N` is displayed as `1 235 N`.

**Engineering notation: Automatic** displays rounded magnitudes below `0.001` or at least `1 000 000` using powers of ten in steps of three, for example `1.23 · 10⁶ N`.

With **Off**, these values remain in decimal notation.

Digit groups are separated by narrow non-breaking spaces. The decimal separator follows the selected regional format.

Both settings apply to calculated values in the application and report. The calculation and stored inputs remain unchanged. Diagram axis ticks use their own scaling.
