# Lager und Randbedingungen

Lager sperren die Längsverschiebung `u`, Querverschiebung `w` oder Rotation `θ` an einer Balkenposition. Ein Federlager hält sie elastisch. Die Position `x` ist ein exakter Modellwert und kann auch innerhalb des Balkens liegen. Je Position ist ein Lager möglich; ein Federlager darf nicht mit einem anderen Lager an derselben Position liegen.

<div class="spandraft-support-table">
  <table>
    <thead>
      <tr><th scope="col">Lager</th><th scope="col">Symbol</th><th scope="col">Gesperrte Bewegungen</th><th scope="col">Mögliche Reaktionen</th></tr>
    </thead>
    <tbody>
      <tr><th scope="row">Einspannung</th><td><img src="../assets/lager-einspannung.png" alt="Einspannung" width="96" height="76"></td><td><code>u</code>, <code>w</code>, <code>θ</code></td><td><code>Rx</code>, <code>Ry</code>, <code>M</code></td></tr>
      <tr><th scope="row">Festlager</th><td><img src="../assets/lager-festlager.png" alt="Festlager" width="96" height="76"></td><td><code>u</code>, <code>w</code></td><td><code>Rx</code>, <code>Ry</code></td></tr>
      <tr><th scope="row">Loslager</th><td><img src="../assets/lager-loslager.png" alt="Loslager" width="96" height="76"></td><td><code>w</code></td><td><code>Ry</code></td></tr>
      <tr><th scope="row">Federlager</th><td><img src="../assets/lager-federlager.png" alt="Federlager" width="96" height="76"></td><td><code>u</code>; <code>w</code> und <code>θ</code> elastisch</td><td><code>Rx</code>, <code>Ry</code> und/oder <code>M</code></td></tr>
    </tbody>
  </table>
</div>

Die Bewegungen `u`, `w` und `θ` sind auf [Koordinatensystem und Einheiten](koordinaten-einheiten.md) erklärt.

## Ausreichend lagern

Eine Einspannung kann den Balken allein ausreichend halten. Typisch ist auch ein Festlager mit einem Loslager an einer anderen Position; ein Festlager allein lässt die Drehung um seinen Lagerpunkt frei. Mehrere Lager sind möglich, ohne dass dadurch innere Gelenke entstehen. Zwei reine Loslager lassen die axiale Starrkörperbewegung frei – dann kann SpanDraft auch ohne äußere Längslast nicht rechnen.

Die Lager sind idealisiert: Gesperrte Bewegungen werden als null angesetzt; ein Loslager sperrt immer die vertikale Verschiebung `w`. Reibung, Spiel und vorgegebene Lagerverschiebungen sind nicht enthalten. Lagernachgiebigkeit gibt es nur dort, wo Sie ein Federlager einsetzen.

## Federlager

Ein Federlager hält den Balken axial starr (`u = 0`) und besitzt eine **Wegfeder**, eine **Drehfeder** oder beide. Jede Feder hat eine Steifigkeit größer als null:

- Die Wegfeder (Steifigkeit `k`, zum Beispiel in N/mm) wirkt mit der Rückstellkraft `R = −k · w`.
- Die Drehfeder (Steifigkeit `kθ`, zum Beispiel in kN·m/rad) wirkt mit dem Rückstellmoment `C = −kθ · θ`.
- Mit einer Drehfeder und ohne Wegfeder hält **Starr in w** die Querverschiebung starr. Das ist eine Teileinspannung: Der Balken kann sich am Lager nicht verschieben, seine Drehung ist aber elastisch.

Die Federkraft beziehungsweise das Federmoment erscheint als Reaktion `Ry` beziehungsweise `M`; `Rx` ist null. Eine einzelne Feder hält den Balken nicht ausreichend: Eine Wegfeder allein lässt die Drehung um ihre Position frei, eine Drehfeder allein die Verschiebung. Ein Balken nur auf Federn ist berechenbar, wenn die Federn zwei Querpositionen oder eine Querposition und eine Drehung halten.

SpanDraft kennt keine Steifigkeitsgrenze und ersetzt kein starres Lager durch eine Feder: Eine sehr steife Feder bleibt eine Feder. Bei extremen Steifigkeiten oder extrem nah beieinanderliegenden Federn kann SpanDraft das Modell als numerisch nicht sicher lösbar ablehnen, statt ein zweifelhaftes Ergebnis zu liefern.

Die Einheiten der Steifigkeiten wählen Sie in den Einstellungen. Wählen Sie die Lagerart nach den tatsächlichen Bewegungsbeschränkungen; Details stehen unter [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

Weiter: [Kräfte, Momente und Streckenlasten](lasten.md).
