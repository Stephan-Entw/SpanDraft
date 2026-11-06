# SpanDraft – User Manual

SpanDraft lets you model and analyze a straight beam: define its cross-section and material, place supports and loads in a schematic, and review deflection, shear force, bending moment, support reactions, and selected strength results.

This manual explains how to use SpanDraft and how to interpret its results. It is intended for users who want to perform a quick check or a more detailed analysis of a beam without creating a general-purpose FEA model. Basic concepts such as forces and moments are explained briefly; prior knowledge of the finite element method is not required.

## Where would you like to start?

| Your goal | Go to |
| --- | --- |
| Analyze your first beam | [First calculation](getting-started/first-calculation.md) |
| Get familiar with the interface | [Getting to know SpanDraft](getting-started/overview.md) |
| Select a cross-section, material, or bending axis | [Beam, material and cross-section](model/material-cross-section.md) |
| Set up the supports correctly | [Supports and boundary conditions](model/supports.md) |
| Add forces, moments, distributed loads, or self-weight | [Forces, moments and distributed loads](model/loads.md) |
| Understand sign conventions and units | [Coordinate system and units](model/coordinates-units.md) |
| Read diagrams and support reactions | [Result diagrams and support reactions](results/diagrams-reactions.md) |
| Assess bending stress and the safety factor | [Bending stress and safety factor](results/stress-safety.md) |
| Understand what the model includes | [What SpanDraft calculates – and what it does not](background/model-assumptions.md) |
| Understand an error or missing result | [Troubleshooting](background/troubleshooting.md) |

## Key points

SpanDraft analyzes a single straight beam with a constant cross-section and material along its length. The calculation is linear elastic and considers bending in one selected plane. The inputs are idealized mechanical quantities; SpanDraft does not automatically determine the actual boundary conditions of a real component.

The application displays deflection, shear force, bending moment, and support reactions. Self-weight can optionally be derived from the material density and cross-sectional area. The additional bending stress and safety factor are not a complete verification of the real component. Buckling, local buckling, welds, fatigue, and other effects, for example, are not checked.

The main limitations are described under [What SpanDraft calculates – and what it does not](background/model-assumptions.md). For the safety factor, see [Bending stress and safety factor](results/stress-safety.md).

## Typical workflow

1. Create a new project and define the beam length, material, cross-section, and bending axis.
2. Model the intended support conditions.
3. Add point forces, point moments, constant distributed loads and, if required, self-weight.
4. Review the results and any messages; correct the inputs if necessary.
5. Save the project as a `.spandraft` file.

Results are recalculated automatically after confirmed changes. An input that is still being edited, or a temporary preview, does not change the calculated project.

SpanDraft is available in German and English.
