# Result diagrams and support reactions

SpanDraft displays the calculated deflection, shear force, and bending moment along the beam.

The horizontal axis represents the longitudinal beam coordinate `x`. Support reactions and selected key results are shown in the project overview.

## Deflection `w(x)`

![Deflection of the example beam with the extreme value marked at the center](../assets/diagramm-durchbiegung.png)

`w(x)` describes the transverse displacement of the beam axis along the beam.

Positive and negative values correspond to the two directions of the defined `w` axis. A typical downward deflection therefore appears as a negative value.

It is often useful to know where the largest deflection occurs and how large its magnitude is. Extreme values are marked in the diagram.

The **Maximum deflection** value in the project overview shows the largest magnitude of the transverse displacement.

## Shear force `V(x)`

![Shear force of the example beam with positive and negative values and a jump at the centered point force](../assets/diagramm-querkraft.png)

`V(x)` shows the internal shear force along the beam.

Point forces and vertical support reactions can produce visible jumps. Under a constant distributed load, the shear force changes continuously.

Positive and negative values indicate different directions according to the sign convention used. The sign does not indicate whether a result is “good” or “bad.”

## Bending moment `M(x)`

![Bending moment of the example beam with the maximum marked at the center](../assets/diagramm-biegemoment.png)

`M(x)` shows the internal bending-moment distribution.

Large magnitudes indicate regions with high bending demand. Positive and negative values represent different bending directions.

Both directions are considered when calculating the maximum bending stress; the relevant moment magnitude governs.

Extreme values are marked in the diagram. An applied point moment can create a jump in the bending-moment diagram.

<span id="support-reactions-in-the-project-overview"></span>

## Support reactions

Support reactions are forces or moments through which the idealized supports balance the external loads in the calculated model.

| Positive result | Direction |
| --- | --- |
| `Rx > 0` | → to the right |
| `Ry > 0` | ↑ upward |
| Reaction moment > 0 | ↺ counterclockwise |

A negative result acts in the opposite direction.

SpanDraft displays only reaction components that the relevant support type can transmit.

Components that cannot be transmitted remain blank in the overview. A calculated reaction component whose value is zero is still displayed.

## Key results in the project overview

| Result | Meaning |
| --- | --- |
| Maximum deflection | largest magnitude of transverse displacement |
| Governing moment | largest magnitude of the bending moment |
| [Maximum bending stress](stress-safety.md#bending-stress) | elastic bending stress derived from the governing moment and section properties |
| [Safety factor](stress-safety.md#safety-factor) | comparison with the stored material strength value |

If no results are displayed, see [Troubleshooting](../background/troubleshooting.md).
