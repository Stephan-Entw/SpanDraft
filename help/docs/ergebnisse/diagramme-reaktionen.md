# Ergebnisdiagramme und Lagerreaktionen

SpanDraft zeigt die berechnete Durchbiegung, Querkraft und das Biegemoment entlang des Balkens. Die horizontale Achse entspricht dabei der Balkenlängskoordinate `x`. Lagerreaktionen und ausgewählte Kennwerte stehen in der Projektübersicht.

## Durchbiegung `w(x)`

![Durchbiegung des Beispielbalkens mit markiertem Extremwert in der Balkenmitte](../assets/diagramm-durchbiegung.png)

`w(x)` zeigt die Querverschiebung der Balkenachse entlang des Balkens. Positive und negative Werte entsprechen den beiden Richtungen der festgelegten `w`-Achse. Eine typische Durchbiegung nach unten erscheint deshalb negativ.

Relevant ist häufig, wo die größte Verformung auftritt und wie groß ihr Betrag ist. Die Extremwerte werden im Diagramm markiert. Die Kennzahl **Maximale Durchbiegung** in der Projektübersicht zeigt den größten Betrag der Querverschiebung.

## Querkraft `V(x)`

![Querkraft des Beispielbalkens mit positiven und negativen Werten und einem Sprung an der mittigen Punktkraft](../assets/diagramm-querkraft.png)

`V(x)` zeigt die innere Querkraft entlang des Balkens. Punktkräfte und vertikale Lagerreaktionen können sichtbare Sprünge erzeugen. Unter einer konstanten Streckenlast ändert sich die Querkraft kontinuierlich.

Positive und negative Werte kennzeichnen unterschiedliche Richtungen nach der verwendeten Vorzeichenkonvention. Das Vorzeichen ist keine Bewertung als „gut“ oder „schlecht“.

## Biegemoment `M(x)`

![Biegemoment des Beispielbalkens mit markiertem Maximum in der Balkenmitte](../assets/diagramm-biegemoment.png)

`M(x)` zeigt den inneren Biegemomentverlauf. Große Beträge kennzeichnen Bereiche hoher Biegebeanspruchung. Positive und negative Werte entsprechen unterschiedlichen Biegerichtungen.

Für die maximale Biegespannung werden beide Richtungen berücksichtigt; maßgebend ist der relevante Momentbetrag. Die Extremwerte werden im Diagramm markiert. Ein aufgebrachtes Punktmoment kann einen Sprung im Momentendiagramm erzeugen.

<span id="lagerreaktionen-in-der-projektubersicht"></span>

## Lagerreaktionen

Lagerreaktionen sind Kräfte beziehungsweise Momente, mit denen die idealisierten Lager die äußeren Lasten im berechneten Modell ausgleichen.

| Positives Ergebnis | Richtung |
| --- | --- |
| `Rx > 0` | → nach rechts |
| `Ry > 0` | ↑ nach oben |
| Reaktionsmoment > 0 | ↺ gegen den Uhrzeigersinn |

Ein negatives Ergebnis wirkt entsprechend in die entgegengesetzte Richtung.

SpanDraft zeigt nur Reaktionskomponenten, die der betreffende Lagertyp übertragen kann. Nicht übertragbare Komponenten bleiben in der Übersicht leer; eine berechnete Reaktionskomponente mit dem Wert null wird dagegen angezeigt.

## Kennwerte in der Projektübersicht

| Kennwert | Bedeutung |
| --- | --- |
| Maximale Durchbiegung | größter Betrag der Querverschiebung |
| Maßgebendes Moment | größter Betrag des Biegemoments |
| [Maximale Biegespannung](spannung-sicherheit.md#biegespannung) | daraus abgeleitete elastische Biegebeanspruchung |
| [Sicherheitsfaktor](spannung-sicherheit.md#sicherheitsfaktor) | Vergleich mit dem hinterlegten Festigkeitskennwert |

Wenn keine Ergebnisse erscheinen, siehe [Probleme erkennen und beheben](../hintergrund/probleme.md).
