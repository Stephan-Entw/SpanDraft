# Kräfte, Momente und Streckenlasten

Lasten beschreiben die vorgegebenen Einwirkungen in der zweidimensionalen Biegeebene.

## Punktkraft

Eine Punktkraft `F` wirkt an einer Position `x`, auch an einem Balkenende. Das Vorzeichen gibt die Richtung an.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>F &gt; 0</code> · nach oben</span><span><code>F &lt; 0</code> · nach unten</span></div>
  <img src="../assets/punktkraft-vorzeichen.png" alt="Punktkraftpfeile: links aufwärts, rechts abwärts">
</figure>

## Punktmoment

Ein Punktmoment `M` wirkt an einer Position `x`, ohne dort eine zusätzliche resultierende Querkraft aufzubringen.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>M &gt; 0</code> · gegen den Uhrzeigersinn</span><span><code>M &lt; 0</code> · im Uhrzeigersinn</span></div>
  <img src="../assets/punktmoment-vorzeichen.png" alt="Punktmomente: links gegen den Uhrzeigersinn, rechts im Uhrzeigersinn">
</figure>

Mehrere Punktkräfte und Punktmomente dürfen an derselben Position liegen.

## Konstante Streckenlast

Eine konstante Streckenlast `q` wirkt gleichmäßig von `xStart` bis `xEnd`. Sie ist eine **Kraft pro Länge**; Start und Ende definieren den belasteten Bereich mit `0 ≤ xStart < xEnd ≤ L`.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>q &gt; 0</code> · nach oben</span><span><code>q &lt; 0</code> · nach unten</span></div>
  <img src="../assets/streckenlast-vorzeichen.png" alt="Streckenlastpfeile: links aufwärts, rechts abwärts">
</figure>

Mehrere Streckenlasten dürfen sich überlagern; ihre Intensitäten werden vorzeichenbehaftet addiert. Dreieckige oder veränderliche Streckenlasten gibt es aktuell nicht als eigenen Lasttyp.

<details class="spandraft-details">
  <summary>Resultierende einer konstanten Streckenlast</summary>
  <p>Die Resultierende beträgt <code>Fq = q · (xEnd − xStart)</code> und greift in der Mitte des belasteten Bereichs bei <code>xq = (xStart + xEnd) / 2</code> an. Diese Ersatzkraft beschreibt das globale Gleichgewicht. Für Verformung und Schnittgrößen behandelt SpanDraft die Streckenlast weiterhin als verteilte Last.</p>
</details>

## Grenzen des Lastmodells

Die hinterlegte Dichte erzeugt kein automatisches Eigengewicht. Axiale oder schräge Kräfte sowie Torsionsmomente gehören derzeit nicht zum unterstützten Lastmodell. Mehr dazu: [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

Weiter: [Ergebnisdiagramme und Lagerreaktionen](../ergebnisse/diagramme-reaktionen.md).
