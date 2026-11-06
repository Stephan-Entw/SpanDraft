# What SpanDraft calculates – and what it does not

SpanDraft analyzes a straight beam in a two-dimensional bending plane.

The material and cross-section remain constant along the beam length, supports are idealized, and deformations are assumed to be small.

The calculation is based on the linear-elastic Euler–Bernoulli beam model.

## What the model represents

| Area | Assumption in SpanDraft |
| --- | --- |
| Beam | one straight beam |
| Material | constant and linear elastic |
| Cross-section | constant, with one selected bending axis |
| Deformation | small displacements and rotations |
| Supports | ideal Fixed, Pinned, or Roller Supports |
| Loads | vertical point forces, point moments, constant distributed loads, and optional self-weight |
| Results | deformation, shear force, bending moment, support reactions, and a basic bending assessment |

## When the model is a good fit

SpanDraft is particularly suitable for slender beams where bending dominates and shear deformation is comparatively small.

For very short or deep beams, shear deformation may become significant; it is not included in the model.

If the deformations are large enough to significantly change the geometry or the effect of the loads, the linear model is no longer sufficient.

## What is not included

### Three-dimensional loading

Inclined forces or forces with arbitrary directions in space, simultaneous bending about two axes, and torsion are not included.

SpanDraft does not perform a general three-dimensional structural analysis.

### Extended loads and supports

Self-weight can be included as a downward constant distributed load derived from the material density and cross-sectional area.

Variable triangular or trapezoidal distributed loads and external axial loads are not currently supported load types in the user interface.

Elastic supports or springs, prescribed support displacements, and internal hinge releases are also not included.

### Nonlinear and dynamic effects

Large deformations and geometric nonlinearity, plastic behavior, vibration, impact and dynamic loads, and time- or temperature-dependent behavior are not considered.

### Additional strength checks

SpanDraft does not check buckling, local buckling, lateral-torsional buckling, fatigue, or local effects at notches, holes, or connections.

Shear and equivalent stresses, complete combined-stress assessments, and code-based safety or resistance checks are also not included.

The basic bending assessment is explained under [Bending stress and safety factor](../results/stress-safety.md).

## The bending axis remains two-dimensional

Selecting `y-y`, `z-z`, `u-u`, or `v-v` determines which cross-section properties are used for the single bending plane being analyzed.

It does not rotate the beam in three-dimensional space or create a three-dimensional decomposition of forces.

For more information, see [Beam, material and cross-section](../model/material-cross-section.md).

## The real component remains an idealization

SpanDraft uses idealized supports and loads and a constant cross-section.

A real component may include, for example, clearance, flexibility, holes, welds, or local reinforcements.

The important question is therefore not only whether the mathematical model can be solved, but whether its idealization represents the real situation adequately.
