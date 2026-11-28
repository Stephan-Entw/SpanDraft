# Beam, material and cross-section

A SpanDraft beam is defined by its length `L`, material, cross-section, and selected bending axis.

## Segments

A beam consists of one or more **segments**. Each segment has its own material, cross-section, and bending axis. They are constant within a segment and change abruptly at the segment boundaries. A new project starts with one segment. Under **Project properties**, select a segment in the **Segments** list and enter a position under **Split at**: both halves first receive all properties of the segment that was split. The material, cross-section, and axis cards apply to the selected segment; the **Start of the segment** can be moved if needed, and **Remove segment** lets the left neighbour take it over.

A segment boundary may be at any position, including on a support, a spring support, or a load. Self-weight uses the density and area of each segment and therefore needs the density of every material. Boundaries appear as thin cross lines on the beam and as dashed lines in the result diagrams. The beam length must stay greater than the last boundary.

## Beam length

The beam extends from `x = 0` on the left to `x = L` on the right.

Supports and loads may be located at the ends or anywhere along the beam, so overhangs are possible. When shortening a beam, all existing supports and loads must remain within the new length.

## Material

| Property | Meaning in SpanDraft |
| --- | --- |
| `E` – Young’s modulus | Determines stiffness and therefore deflection and rotation |
| Strength value: yield strength `Re` or 0.2% proof strength `Rp0.2` | Used to calculate the safety factor |

Density `ρ` is used when optional self-weight is enabled. SpanDraft then derives a downward distributed load from the material density and cross-sectional area.

Poisson’s ratio `ν` can be stored but does not directly enter the current beam calculation.

Material number, standard designations, and category are used for identification. Catalog values are technical reference values and do not replace the applicable standard, manufacturer data, or material certificate.

Each material has exactly one strength value. For a custom material, explicitly select its type and enter the corresponding numerical value. The material category does not determine the strength type.

Older data may not yet contain a strength type. Before editing such a material, saving the project normally, or creating a report, select either `Re` or `Rp0.2` in the material editor.

### Material library

The shared material table contains the current project material and the available material presets.

The project material is marked with a checkmark and the suffix **(current in project)**. If it matches the active search and filters, it appears first.

Under **Source**, you can show only the current project material, **Built-in materials**, or **My materials**. Categories apply across all three sources.

A lock icon identifies built-in, read-only materials. **Create copy…** creates an editable version in **My materials**. **New material** also creates a custom library entry once confirmed.

**Customize…** opens a small editor. **OK** confirms the edit:

- for a custom material, the library entry is saved immediately;
- for the project material, only the working state of the open selection is changed at first.

**Apply** transfers the selected material to the project overview, where the project changes are confirmed together.

**Cancel** in the material selection discards the working state of the project material but keeps any library changes that have already been confirmed.

The project stores all material values, including the category, material number, and other standards, as an independent snapshot. Changing or deleting a library entry later does not modify existing projects.

**Save to library…** is available for the project material as long as no fully identical material already exists. If a custom material with the same designation but different data already exists, you can replace the existing entry after an additional confirmation, change the designation, or cancel.

## Cross-section

For a **parametric cross-section**, you enter geometric dimensions and SpanDraft calculates `A`, `I`, `W+`, and `W−`.

The dimensions describe an idealized shape; they do not automatically represent a standardized commercial section.

<details class="spandraft-details">
  <summary>Supported parametric shapes</summary>
  <ul>
    <li>Rectangle: width and height</li>
    <li>Rectangular hollow section: outer width, outer height, wall thickness, and optional outer radius</li>
    <li>Circle: diameter</li>
    <li>Circular hollow section: outer diameter and wall thickness</li>
    <li>I/H section: height, width, web thickness, flange thickness, and inner radius</li>
    <li>U section: height, width, web thickness, flange thickness, and inner radius</li>
    <li>T section: height, width, web thickness, flange thickness, and inner radius</li>
    <li>Angle: width, height, leg thickness, and inner radius</li>
  </ul>
  <p>For rectangular hollow sections, the inner radius follows from the outer radius and wall thickness. For I/H, U, T, and angle sections, the outer edges remain sharp. A radius of zero means a sharp corner.</p>
</details>

With **manual properties**, you enter existing tabulated or manufacturer values for `A`, `I`, and `W` directly: either for a single axis or for an axis pair `y-y/z-z` or `u-u/v-v`.

For manual properties, `W+ = W− = W`. Different section moduli on the two sides of an asymmetric section therefore cannot be represented fully in this mode.

Manual properties contain no geometric outline and no derived centroid.

| Quantity | Meaning |
| --- | --- |
| `A` | Cross-sectional area, for example in mm² |
| `I` | Second moment of area, for example in mm⁴; together with `E`, it determines flexural rigidity |
| `W+`, `W−` | Section moduli used for elastic bending stress, for example in mm³ |

`W+` and `W−` refer to geometric sides of the section, not directly to tension and compression.

Custom cross-sections can be stored in the library and reused. The project retains an independent copy.

## Bending axis

![Local cross-section coordinates and bending axes](../assets/querschnitt-achsen.png)

Standard sections use the `y-y` or `z-z` axes. Angle sections use the principal axes `u-u` or `v-v`.

The selected axis determines which `I` value and which section moduli are used in the two-dimensional analysis. SpanDraft does not calculate bending about two axes simultaneously.

## Example: stepped shaft on two bearings

A shaft made of three segments rests on two bearings: thin bearing seats with Ø 30 mm at the ends and a thicker middle part with Ø 40 mm. The example shows segments with their own cross-sections and how SpanDraft determines the governing segment.

![Shaft of three segments Ø30, Ø40 and Ø30 on a pinned and a roller support with point force F1](../assets/beispiel-welle.png)

| Quantity | Value |
| --- | --- |
| Segments | Ø 30 mm from `x = 0` to `0.1 m`, Ø 40 mm to `0.3 m`, Ø 30 mm to `0.4 m`; bending axis y-y |
| Material | 42CrMo4 +QT, `E = 210,000 N/mm²`, reference value `Rp0.2 = 650 N/mm²` |
| Supports | Pinned support `A` at `x = 0`, roller support `B` at `x = 0.4 m` |
| Load | Point force `F1 = −5 kN` at `x = 0.15 m`; no self-weight |

Expected results:

- support reactions from the lever rule: `A = F · (L − a) / L = 3125 N`, `B = F · a / L = 1875 N`;
- largest bending moment `M = A · a = 468.75 N·m` under the force, in the Ø 40 segment: `σ = M / W = 74.6 N/mm²` with `W = π · d³ / 32`;
- at the shoulder at `x = 0.1 m` the moment of `312.5 N·m` is smaller, but the Ø 30 segment is weaker: `σ = 117.9 N/mm²`. This left segment governs, with safety factor `Rp0.2 / σ = 5.5`.

SpanDraft reports the **nominal bending stress** `σ = M / W` of the segment. At the shoulder, a real shaft also has a local **notch stress** that depends on the fillet radius and the diameter ratio. It is not part of the analysis and must be assessed separately for a strength verification; see [Bending stress and safety factor](../results/stress-safety.md).

[Download example file](../assets/examples/abgesetzte-welle.spandraft)

Next: [Supports and boundary conditions](supports.md).
