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

## Streckenlast

Eine Streckenlast wirkt von `xStart` bis `xEnd` und ist eine **Kraft pro Länge**; Start und Ende definieren den belasteten Bereich mit `0 ≤ xStart < xEnd ≤ L`. Sie hat zwei Werte: `q₁` am Start und `q₂` am Ende. Dazwischen ändert sich die Intensität linear.

- Bei `q₁ = q₂` ist die Last konstant.
- Ist einer der Werte null, entsteht eine Dreieckslast.
- Sind beide Werte ungleich null und verschieden, entsteht eine Trapezlast.
- Haben `q₁` und `q₂` verschiedene Vorzeichen, wechselt die Last innerhalb des Bereichs ihre Richtung.

<figure class="spandraft-load-signs">
  <div class="spandraft-load-sign-labels"><span><code>q &gt; 0</code> · nach oben</span><span><code>q &lt; 0</code> · nach unten</span></div>
  <img src="../assets/streckenlast-vorzeichen.png" alt="Streckenlastpfeile: links aufwärts, rechts abwärts">
</figure>

Beim Anlegen einer Streckenlast folgt `q₂` dem Wert von `q₁`, solange Sie `q₂` nicht selbst ändern. So geben Sie eine konstante Last mit einem Wert ein; für eine Dreiecks- oder Trapezlast tragen Sie `q₂` zusätzlich ein. In der Zeichnung ist die Pfeilhöhe proportional zum Betrag der Intensität, bezogen auf den größeren der beiden Randwerte; die Pfeilrichtung folgt dem Vorzeichen.

Mehrere Streckenlasten dürfen sich überlagern; ihre Intensitäten werden vorzeichenbehaftet addiert.

<details class="spandraft-details">
  <summary>Resultierende einer Streckenlast</summary>
  <p>Die Resultierende beträgt <code>Fq = (q₁ + q₂) / 2 · (xEnd − xStart)</code>. Bei konstanter Last greift sie in der Mitte des belasteten Bereichs an. Bei Vorzeichenwechsel kann sie null sein, obwohl die Last ein Moment erzeugt, etwa bei einer antisymmetrischen Dreieckslast; deshalb zeigt SpanDraft keinen Angriffspunkt der Resultierenden an. Diese Ersatzkraft beschreibt das globale Gleichgewicht. Für Verformung und Schnittgrößen behandelt SpanDraft die Streckenlast weiterhin als verteilte Last.</p>
</details>

## Eigengewicht

Eigengewicht ist eine optionale Streckenlast aus dem aktuellen Werkstoff und Querschnitt.

Wenn es aktiviert ist, berechnet SpanDraft aus Werkstoffdichte `ρ`, Querschnittsfläche `A` und Standardfallbeschleunigung eine konstante, nach unten gerichtete Last über die gesamte Balkenlänge:

`q_g = −ρ · A · g₀`, mit `g₀ = 9.80665 m/s²`.

Der Werkstoff benötigt eine Dichte, bevor Eigengewicht aktiviert werden kann.

Die Eigengewichtslast wird als `g` dargestellt. Dieser Name ist für Eigengewicht reserviert; vorhandene Lager oder Lasten mit dem Namen `g` oder `G` müssen zuvor umbenannt werden. Der Lastwert folgt der aktuellen Werkstoffdichte und Querschnittsfläche. Ausblenden verändert nur die Darstellung; Deaktivieren entfernt Eigengewicht aus dem mechanischen Modell.

## Grenzen des Lastmodells

Axiale oder schräge Kräfte sowie Torsionsmomente gehören derzeit nicht zum unterstützten Lastmodell. Mehr dazu: [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

Weiter: [Ergebnisdiagramme und Lagerreaktionen](../ergebnisse/diagramme-reaktionen.md).
