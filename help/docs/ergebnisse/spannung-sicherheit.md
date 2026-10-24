# Spannung und Sicherheitsfaktor

SpanDraft ermittelt aus dem Biegemoment und dem gewählten Querschnitt eine maximale elastische Biegespannung und vergleicht diese mit dem im Werkstoff hinterlegten Festigkeitskennwert.

## Biegespannung

Das Biegemoment allein sagt noch nicht, wie stark der Querschnitt beansprucht wird. Bei gleichem Moment führt ein größeres Widerstandsmoment `W` zu einer kleineren elastischen Biegespannung.

<math xmlns="http://www.w3.org/1998/Math/MathML" display="block" aria-label="Maximale Biegespannung gleich größter Momentbetrag geteilt durch maßgebendes Widerstandsmoment">
  <mrow>
    <msub><mi>σ</mi><mtext>b,max</mtext></msub>
    <mo>=</mo>
    <mfrac>
      <msub><mrow><mo>|</mo><mi>M</mi><mo>|</mo></mrow><mtext>max</mtext></msub>
      <msub><mi>W</mi><mtext>maßgebend</mtext></msub>
    </mfrac>
  </mrow>
</math>

Bei unsymmetrischen Querschnitten berücksichtigt SpanDraft für den maximalen Spannungsbetrag die ungünstigere der beiden Profilseiten `W+` und `W−`.

<details class="spandraft-details">
  <summary>Unsymmetrische Querschnitte</summary>
  <p><code>W+</code> und <code>W−</code> gehören zu den beiden geometrischen Profilseiten. Sie bedeuten nicht automatisch Zug- beziehungsweise Druckseite. Für den maximalen Spannungsbetrag wird die ungünstigere Seite berücksichtigt.</p>
</details>

## Sicherheitsfaktor

SpanDraft vergleicht die maximale elastische Biegespannung mit dem hinterlegten Festigkeitskennwert: Streckgrenze `Re` oder
0,2-%-Dehngrenze `Rp0,2`. Die konkrete Formel im Bericht zeigt den gewählten Typ.

<math xmlns="http://www.w3.org/1998/Math/MathML" display="block" aria-label="Sicherheitsfaktor gleich gewählter Festigkeitskennwert geteilt durch maximale Biegespannung">
  <mrow>
    <mi>S</mi>
    <mo>=</mo>
    <mfrac>
      <mtext>Re oder Rp0,2</mtext>
      <msub><mi>σ</mi><mtext>b,max</mtext></msub>
    </mfrac>
  </mrow>
</math>

**S > 1:** Biegespannung liegt unter dem hinterlegten Festigkeitskennwert.<br>
**S = 1:** Der hinterlegte Festigkeitskennwert ist erreicht.<br>
**S < 1:** Biegespannung liegt über dem hinterlegten Festigkeitskennwert.

**S > 1 bedeutet nicht automatisch, dass das reale Bauteil ausreichend dimensioniert ist.** Der Wert bewertet ausschließlich die hier berechnete elastische Biegespannung gegenüber dem hinterlegten Festigkeitskennwert.

Bei verschwindender Biegespannung kann mathematisch `S = ∞` erscheinen. Das bedeutet nicht „unendlich sicher“.

Angezeigte Werte können gerundet sein; die Berechnung verwendet die ungerundeten Werte. Mehr dazu unter [Ergebnisdarstellung](../modell/koordinaten-einheiten.md#standard-oder-detailliert).

## Was der Sicherheitsfaktor nicht bewertet

- **Andere Beanspruchungen:** z. B. Schub oder kombinierte Axialkraft und Biegung.
- **Stabilität:** z. B. Knicken, Beulen oder Kippen.
- **Lokale, dynamische und zeitabhängige Effekte:** z. B. Kerben, Verbindungen, Ermüdung oder Stoßbeanspruchung.
- **Normative und konstruktive Anforderungen:** z. B. zulässige Durchbiegung oder erforderliche Sicherheitsbeiwerte.

Mehr dazu unter [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

Der Sicherheitsfaktor ist nur zusammen mit einem plausiblen Modell, den [Ergebnisdiagrammen und Lagerreaktionen](diagramme-reaktionen.md) und den für das reale Bauteil relevanten weiteren Nachweisen sinnvoll zu bewerten.
