# Bending stress and safety factor

SpanDraft determines the maximum elastic bending stress from the bending moment and selected cross-section and compares it with the strength value stored for the material.

## Bending stress

The bending moment alone does not determine how highly the cross-section is stressed.

For the same bending moment, a larger section modulus `W` results in a lower elastic bending stress.

<math xmlns="http://www.w3.org/1998/Math/MathML" display="block" aria-label="Maximum bending stress equals the maximum absolute bending moment divided by the governing section modulus">
  <mrow>
    <msub><mi>σ</mi><mtext>b,max</mtext></msub>
    <mo>=</mo>
    <mfrac>
      <msub><mrow><mo>|</mo><mi>M</mi><mo>|</mo></mrow><mtext>max</mtext></msub>
      <msub><mi>W</mi><mtext>governing</mtext></msub>
    </mfrac>
  </mrow>
</math>

For asymmetric cross-sections, SpanDraft considers the less favorable of the two section sides `W+` and `W−` when determining the maximum stress magnitude.

<details class="spandraft-details">
  <summary>Asymmetric cross-sections</summary>
  <p><code>W+</code> and <code>W−</code> refer to the two geometric sides of the section. They do not automatically represent the tension and compression sides. The less favorable side is used when determining the maximum stress magnitude.</p>
</details>

## Safety factor

SpanDraft compares the maximum elastic bending stress with the strength value stored for the material: yield strength `Re` or 0.2% proof strength `Rp0.2`.

The formula shown in the report identifies the selected strength type.

<math xmlns="http://www.w3.org/1998/Math/MathML" display="block" aria-label="Safety factor equals the selected strength value divided by the maximum bending stress">
  <mrow>
    <mi>S</mi>
    <mo>=</mo>
    <mfrac>
      <mtext>Re or Rp0.2</mtext>
      <msub><mi>σ</mi><mtext>b,max</mtext></msub>
    </mfrac>
  </mrow>
</math>

**S > 1:** The bending stress is below the stored strength value.<br>
**S = 1:** The stored strength value has been reached.<br>
**S < 1:** The bending stress exceeds the stored strength value.

**S > 1 does not automatically mean that the real component is adequately designed.** The value assesses only the elastic bending stress calculated here against the stored material strength value.

If the bending stress is zero, the mathematical result may be `S = ∞`. This does not mean “infinitely safe.”

Displayed values may be rounded; the calculation uses the unrounded values. See [Number formatting](../model/coordinates-units.md#number-formatting).

## What the safety factor does not assess

- **Other stresses:** for example shear stress or combined axial force and bending.
- **Stability:** for example buckling, local buckling, or lateral-torsional buckling.
- **Local, dynamic, and time-dependent effects:** for example notches, connections, fatigue, or impact loading.
- **Code and design requirements:** for example allowable deflection or required design safety factors.

For more details, see [What SpanDraft calculates – and what it does not](../background/model-assumptions.md).

The safety factor is meaningful only together with a plausible model, the [result diagrams and support reactions](diagrams-reactions.md), and any additional checks required for the real component.
