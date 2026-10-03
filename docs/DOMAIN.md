# SpanDraft – Fachmodell

Das Modell in `SpanDraft.Core` beschreibt einen einzelnen geraden Balken mit
konstantem Werkstoff und Querschnitt. Es enthält keine Solverlogik.
Die verbindliche Projektgrundlage bleibt [KONZEPT.md](KONZEPT.md).

## Lokale Koordinaten und Vorzeichen

- Die Balkenachse x verläuft von links nach rechts; x = 0 ist das linke Ende.
- Die positive transversale Richtung zeigt nach oben.
- Positive Rotation und positive aufgebrachte Momente sind gegen den Uhrzeigersinn.
- Einzelkräfte und Streckenlasten sind nach oben positiv, nach unten negativ.
- Alle Positionen sind Abstände vom linken Ende. Beide Balkenenden sind zulässig.

## Einheiten und Einzelobjekte

Immutable `readonly record struct`-Werttypen speichern kanonisch m, N, N·m, Pa,
m², m⁴, m³ bzw. N/m. Explizite Factories und benannte Properties ermöglichen
die benötigten Umrechnungen; implizite Konvertierungen und allgemeine
Einheitenarithmetik sind nicht vorgesehen.

Alle gespeicherten Werte müssen endlich sein. Geometrische Größen sind
nichtnegativ. Der Standardwert eines Werttyps ist 0, damit insbesondere
Positionen am Ursprung gültig sind. Balkenlänge, Querschnittsabmessungen,
Querschnittskennwerte, E und Streckgrenze müssen im jeweiligen Fachobjekt
streng positiv sein. Kraft, Moment und Linienlast dürfen auch negativ oder
null sein. `Pressure` ist als Größe vorzeichenbehaftet; das Material setzt
die zusätzliche Positivitätsbedingung durch.

Ungültige Einzelobjekte werden durch Argument-Exceptions verhindert, auch
bei nicht darstellbaren berechneten Querschnittswerten. Alle fachlichen
Objekte haben ausschließlich lesbare Properties.

## Querschnitte

`Section` stellt Fläche A, Flächenträgheitsmoment I und elastisches
Widerstandsmoment W für eine einzige Schwerpunkt-Biegeachse bereit.
Bei Rechtecken ist die Höhe die transversale Abmessung und die Biegeachse
parallel zur Breite. Bei Kreisen ist sie ein Schwerpunktdurchmesser.

Hohlquerschnitte werden über Außenabmessungen und konstante Wandstärke
definiert. Die Innenabmessungen müssen positiv bleiben. Rechteckrohre haben
idealisierte scharfe Ecken. Reale Herstellerkennwerte können mit
`CustomSection` ohne Geometrieapproximation übernommen werden.

## Modellvalidierung

`BeamModel` kopiert übergebene Lager- und Lastlisten und stellt sie lesbar
bereit. Unvollständige oder in sich widersprüchliche Modelle dürfen erzeugt
werden, solange die Einzelobjekte intrinsisch gültig sind.

`BeamModelValidator.Validate` liefert alle gefundenen Fehler mit Enum-Code,
Nachricht und Property-Pfad; Listenindizes sind nullbasiert. Geprüft werden:

- fehlende Lager,
- Lager, Einzelkräfte und Einzelmomente außerhalb des Balkens,
- teilweise oder vollständig außerhalb liegende Streckenlasten,
- identische Lager mit gleichem Typ und exakt gleicher Position.

Bereichsgrenzen und Duplikate werden ohne numerische Toleranz verglichen.
Negative Positionen werden bereits vom Längenwerttyp ausgeschlossen.
Unterschiedliche Lagertypen am gleichen Ort, überlappende Lasten und
Überhänge werden hier nicht abgelehnt. Statische Bestimmtheit, Stabilität
und die Zuordnung von Randbedingungen bleiben Aufgabe des späteren Solvers.
