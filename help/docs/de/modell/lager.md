# Lager und Randbedingungen

Ein Lager legt fest, wie sich der Balken an einer Position bewegen darf. Starre Lager sperren die Längsverschiebung `u`, die Querverschiebung `w` oder die Drehung `θ`. Federn halten `w` oder `θ` elastisch: Die Bewegung ist nicht gesperrt, sondern erzeugt eine Rückstellung. Innere Gelenke sind keine Lager; sie lösen eine Bindung im Balken (siehe [Gelenke](#gelenke)).

Die Position `x` ist ein exakter Modellwert und kann auch im Inneren des Balkens liegen. An einer Position steht höchstens ein Lager; als Ausnahme dürfen dort eine Wegfeder und eine Drehfeder gemeinsam stehen (siehe [Wegfeder und Drehfeder an einer Position](#wegfeder-und-drehfeder-an-einer-position)). Die Bewegungen `u`, `w` und `θ` sind auf [Koordinatensystem und Einheiten](koordinaten-einheiten.md) erklärt.

<div class="spandraft-support-table">
  <table>
    <thead>
      <tr><th scope="col">Lager</th><th scope="col">Symbol</th><th scope="col">Randbedingung</th><th scope="col">Mögliche Reaktionen</th></tr>
    </thead>
    <tbody>
      <tr><th scope="row">Einspannung</th><td><img src="../assets/lager-einspannung.png" alt="Einspannung" width="96" height="76"></td><td><code>u = 0</code>, <code>w = 0</code>, <code>θ = 0</code></td><td><code>Rx</code>, <code>Ry</code>, <code>M</code></td></tr>
      <tr><th scope="row">Festlager</th><td><img src="../assets/lager-festlager.png" alt="Festlager" width="96" height="76"></td><td><code>u = 0</code>, <code>w = 0</code></td><td><code>Rx</code>, <code>Ry</code></td></tr>
      <tr><th scope="row">Loslager</th><td><img src="../assets/lager-loslager.png" alt="Loslager" width="96" height="76"></td><td><code>w = 0</code></td><td><code>Ry</code></td></tr>
      <tr><th scope="row">Wegfeder</th><td><img src="../assets/lager-wegfeder.png" alt="Wegfeder" width="96" height="76"></td><td><code>u = 0</code>; <code>R = −k · w</code></td><td><code>Rx</code>, <code>Ry</code></td></tr>
      <tr><th scope="row">Drehfeder</th><td><img src="../assets/lager-drehfeder.png" alt="Drehfeder" width="96" height="76"></td><td><code>u = 0</code>; <code>C = −kθ · θ</code></td><td><code>Rx</code>, <code>M</code></td></tr>
    </tbody>
  </table>
</div>

`R` ist die Rückstellkraft der Wegfeder, `C` das Rückstellmoment der Drehfeder. Beide wirken auf den Balken; als Reaktion erscheinen sie als `Ry` beziehungsweise `M`.

## Starre Lager

Einspannung, Festlager und Loslager sind ideal: Gesperrte Bewegungen werden als null angesetzt, ein Loslager sperrt immer die Querverschiebung `w`. Reibung, Spiel und vorgegebene Lagerverschiebungen gibt es nicht. Eine Bewegung, die ein Lager nicht sperrt, bleibt frei; dafür entsteht keine Reaktion.

## Elastische Lagerung

Wegfeder und Drehfeder sind eigene Werkzeuge in der Werkzeugleiste. Jede Steifigkeit ist größer als null, die Feder ideal linear und von der Last unabhängig. Das Federlager hält den Balken in Längsrichtung starr (`u = 0`); eine Axialfeder gibt es nicht. `Rx` ist zurzeit immer null, weil SpanDraft keine äußeren Längslasten kennt – die Randbedingung `u = 0` und die Reaktion `Rx` sind aber zwei verschiedene Dinge.

### Wegfeder

Die Wegfeder hält die Querverschiebung elastisch. Ihre Steifigkeit `k` (zum Beispiel in N/mm) erzeugt die Rückstellkraft `R = −k · w`; diese Kraft ist die Reaktion `Ry`. Die Drehung des Balkens bleibt am Lager frei.

### Drehfeder

Die Drehfeder hält die Drehung elastisch. Ihre Steifigkeit `kθ` (zum Beispiel in kN·m/rad) erzeugt das Rückstellmoment `C = −kθ · θ`; dieses Moment ist die Reaktion `M`. Die Querverschiebung bleibt am Lager frei – außer Sie wählen **Starr in w**.

**Starr in w** sperrt die Querverschiebung starr (`w = 0`) und lässt die Drehung elastisch: eine Teileinspannung. Die Reaktionen sind dann `Ry` als starre Lagerkraft und `M` als Federmoment. Die Option gibt es nur bei der Drehfeder, nicht bei der Wegfeder; sie ist eine eigene mechanische Eigenschaft und schaltet keine Feder ein.

### Wegfeder und Drehfeder an einer Position

Wegfeder und Drehfeder sind zwei eigenständige Lager, auch an derselben Position. Jede hat ihren eigenen Namen, ihre eigene Position und ihre eigene Steifigkeit; Sie wählen, verschieben, bearbeiten und löschen sie einzeln. Setzen Sie die zweite Feder auf die Position der ersten, entsteht einfach ein weiteres Lager mit dem nächsten Namen. Gerechnet werden beide gemeinsam; in der Übersicht erscheint dafür eine Reaktionszeile je Position (zum Beispiel `A + B`).

An einer Position sind nur genau eine Wegfeder und eine Drehfeder möglich. Zwei Wegfedern, zwei Drehfedern, eine Feder zusammen mit einem starren Lager und mehr als zwei Lager lehnt SpanDraft ab. **Starr in w** ist an einer Position mit Wegfeder nicht wählbar. Ändern Sie die Art einer Feder auf die Art, die dort schon steht, lehnt SpanDraft das beim Übernehmen ab.

Die Einheiten der Steifigkeiten wählen Sie in den Einstellungen.

## Ausreichend lagern

Der starre Balken darf keine Bewegung ausführen, ohne dass ein Lager sie hält. Dafür muss die Längsverschiebung `u` mindestens einmal gesperrt sein, und die Querverschiebung samt Drehung muss ausgeschlossen sein: durch eine Einspannung, durch Querlagerung an zwei verschiedenen Positionen oder durch Querlagerung zusammen mit einer Drehlagerung. Mehrere Lager erzeugen dabei keine inneren Gelenke; Gelenke setzen Sie ausdrücklich.

- Eine Einspannung genügt allein.
- Ein Festlager allein lässt die Drehung um den Lagerpunkt frei. Ein Festlager mit einem Loslager an einer anderen Position genügt.
- Zwei reine Loslager lassen die axiale Starrkörperbewegung frei. SpanDraft kann dann auch ohne äußere Längslast nicht rechnen.

Federn zählen nach ihrer Wirkrichtung, nicht nach ihrem Wert: Eine Wegfeder wirkt wie eine Querlagerung, eine Drehfeder wie eine Drehlagerung, und jedes Federlager sperrt `u`. Ein Balken allein auf Federn ist deshalb berechenbar, wenn

- zwei Wegfedern an verschiedenen Positionen stehen,
- eine Wegfeder und eine Drehfeder an derselben oder an verschiedenen Positionen stehen oder
- die Drehfeder **Starr in w** hat.

Eine einzelne Wegfeder lässt die Drehung um ihre Position frei, eine einzelne Drehfeder die Verschiebung, und Drehfedern allein halten keine Querverschiebung. Federn lassen sich mit starren Lagern kombinieren; auch dann entscheiden die gesperrten Bewegungen insgesamt. Eine Feder darf an einer Position nur mit der Feder der anderen Art zusammenfallen, nie mit einem starren Lager.

SpanDraft kennt keine Steifigkeitsgrenze und ersetzt kein starres Lager durch eine Feder: Eine sehr steife Feder bleibt eine Feder, auch wenn sie sich starr verhält. Bei extremen Steifigkeiten oder extrem nah beieinanderliegenden Federn kann SpanDraft das Modell als numerisch nicht sicher lösbar ablehnen, statt ein zweifelhaftes Ergebnis zu liefern.

## Gelenke

Ein **Gelenk** ist eine Momentenfreigabe im Balken: Das Biegemoment ist dort auf beiden Seiten null, der Balken darf an dieser Stelle knicken. Durchbiegung, Querkraft und Längskraft werden übertragen. Gerber-Träger sind das typische Beispiel. Das Gelenk steht in der Werkzeugleiste hinter den Lagern; der Ring auf der Balkenachse lässt sich wie ein Lager platzieren, ziehen und bearbeiten.

- Ein Gelenk liegt **im Inneren** des Balkens, nicht an einem Ende, und höchstens eines an derselben Position.
- Es darf auf einem Festlager, Loslager oder einer Wegfeder liegen, an einer Abschnittsgrenze, unter einer Streckenlast und an einer Einzelkraft. Auf einer Einspannung, einem Lager mit Drehfeder (auch neben einer Wegfeder oder mit **Starr in w**) und an einem Einzelmoment ist kein Gelenk möglich. Die Werkzeuge verweigern eine solche Position schon beim Platzieren.
- Jedes Gelenk nimmt dem Balken eine Bindung. Ein Balken mit Gelenken braucht deshalb entsprechend mehr Lager: Festlager–Loslager–Loslager mit einem Gelenk ist bestimmt, mit zwei Gelenken ein Mechanismus. SpanDraft erkennt Mechanismen exakt und meldet dann, dass die Lagerung nicht ausreicht oder zu viele Gelenke gesetzt sind.

In der Übersicht und im Bericht zeigt die Tabelle **Gelenkgrößen** die Rotation links `θ−` und rechts `θ+` des Gelenks und die übertragene Querkraft `V`; an einem Gelenk über einem Lager oder mit einer Last springt die Querkraft, dann stehen beide Werte. Das Durchbiegungsdiagramm zeigt den Knick. Wählen Sie die Lagerart nach den tatsächlichen Bewegungsbeschränkungen; Details stehen unter [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

Weiter: [Kräfte, Momente und Streckenlasten](lasten.md).
