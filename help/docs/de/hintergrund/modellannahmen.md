# Was SpanDraft berechnet – und was nicht

SpanDraft berechnet einen geraden Balken in einer zweidimensionalen Biegeebene. Werkstoff und Querschnitt sind innerhalb eines Abschnitts konstant und ändern sich an den Abschnittsgrenzen sprunghaft, Lager werden idealisiert und die Verformungen als klein angenommen. Grundlage ist das linear-elastische Euler-Bernoulli-Balkenmodell.

## Was das Modell abbildet

| Bereich | Annahme in SpanDraft |
| --- | --- |
| Balken | ein gerader Balken |
| Werkstoff | je Abschnitt konstant und linear-elastisch |
| Querschnitt | je Abschnitt konstant, eine ausgewählte Biegeachse je Abschnitt |
| Verformung | kleine Verschiebungen und Rotationen |
| Lager | ideale Einspannung, Festlager oder Loslager |
| Lasten | vertikale Punktkräfte, Punktmomente, konstante und linear veränderliche Streckenlasten und optionales Eigengewicht |
| Ergebnisse | Verformung, Schnittgrößen, Lagerreaktionen und einfache Biegebewertung |

## Wann das Modell gut passt

SpanDraft eignet sich besonders für schlanke Balken, bei denen Biegung dominiert und Schubverformungen vergleichsweise klein sind. Bei sehr kurzen oder tiefen Balken kann Schubverformung relevant werden; sie ist im Modell nicht enthalten.

Bei großen Verformungen, die Geometrie oder Lastwirkung wesentlich verändern, reicht das lineare Modell nicht mehr aus.

## Was nicht enthalten ist

### Räumliche Belastungen

Schräg beziehungsweise frei im Raum gerichtete Kräfte, gleichzeitige Biegung um zwei Achsen und Torsion sind nicht enthalten. SpanDraft führt keine vollständige 3D-Tragwerksberechnung durch.

### Erweiterte Lasten und Lager

Eigengewicht kann als konstante, nach unten gerichtete Streckenlast aus Werkstoffdichte und Querschnittsfläche berücksichtigt werden. Streckenlasten sind konstant oder linear veränderlich; nichtlineare Verläufe und äußere axiale Lasten sind in der aktuellen Benutzeroberfläche keine unterstützten Lasttypen. Federlager sind linear-elastisch mit lastunabhängiger Steifigkeit und in Balkenlängsrichtung starr; eine Axialfeder gibt es nicht. Innere Gelenke sind reine Momentenfreigaben; Quer- oder Längsgelenke, Federgelenke und Auflagerverschiebungen sind nicht enthalten.

### Nichtlineare und dynamische Effekte

Große Verformungen und geometrische Nichtlinearität, plastisches Verhalten, Schwingungen, Stoß und dynamische Lasten sowie zeit- oder temperaturabhängiges Verhalten werden nicht berücksichtigt.

### Weitere Festigkeitsnachweise

Knicken, Beulen, Kippen, Ermüdung und lokale Effekte an Kerben, Bohrungen oder Verbindungen werden nicht nachgewiesen. Ebenso fehlen Schub- und Vergleichsspannungen, vollständige kombinierte Spannungsnachweise sowie normbasierte Sicherheits- und Tragfähigkeitsnachweise. Die einfache Biegebewertung erläutert [Spannung und Sicherheitsfaktor](../ergebnisse/spannung-sicherheit.md).

## Die Biegeachse bleibt zweidimensional

Die Auswahl `y-y`, `z-z`, `u-u` oder `v-v` bestimmt, welche Querschnittswerte für die eine berechnete Biegeebene verwendet werden. Sie dreht den Balken nicht räumlich und erzeugt keine 3D-Kraftzerlegung. Mehr dazu unter [Balken, Werkstoff und Querschnitt](../modell/werkstoff-querschnitt.md).

## Das reale Bauteil bleibt eine Idealisierung

SpanDraft rechnet mit idealisierten Lagern und Lasten sowie einem konstanten Querschnitt. Ein reales Bauteil kann beispielsweise Spiel, Nachgiebigkeit, Bohrungen, Schweißnähte oder lokale Verstärkungen besitzen.

Entscheidend ist deshalb nicht nur, ob das mathematische Modell lösbar ist, sondern ob seine Idealisierung die reale Situation ausreichend beschreibt.
