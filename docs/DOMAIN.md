# SpanDraft – Fachmodell

Das Modell in `SpanDraft.Core` beschreibt einen einzelnen geraden Balken mit
konstantem Werkstoff und Querschnitt. Es enthält keine Solverlogik.
Produkt-Scope und Zielbild stehen in [KONZEPT.md](KONZEPT.md); numerische Details
und Solververträge in [SOLVER.md](SOLVER.md).

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

Alle gespeicherten Werte müssen endlich sein. Abstände, Flächen sowie
Flächenträgheits- und Widerstandsmomente sind nichtnegativ.
Der Standardwert eines Werttyps ist 0, damit insbesondere
Positionen am Ursprung gültig sind. Balkenlänge, Querschnittsabmessungen,
Querschnittskennwerte, E und Streckgrenze müssen im jeweiligen Fachobjekt
streng positiv sein. Kraft, Moment und Linienlast dürfen auch negativ oder
null sein. `Pressure` ist als Größe vorzeichenbehaftet; das Material setzt
die zusätzliche Positivitätsbedingung durch.

Ungültige Einzelobjekte werden durch Argument-Exceptions verhindert, auch
bei nicht darstellbaren berechneten Querschnittswerten. Alle fachlichen
Objekte haben ausschließlich lesbare Properties.

## Querschnitte

Die bestehende `Section`-Hierarchie implementiert `ISectionDefinition` und stellt
aus Kompatibilitätsgründen genau die Schwerpunkt-Biegeachse Y bereit. Fläche A,
Flächenträgheitsmoment Iy und beide Widerstandsmomente Wy+ = Wy− entsprechen
unverändert den bisherigen Properties `Area`, `SecondMomentOfArea` und
`SectionModulus`. Die analytischen Berechnungsformeln bleiben eigenständig.
Bei Rechtecken ist die Höhe die transversale Abmessung und die Biegeachse
parallel zur Breite. Bei Kreisen ist sie ein Schwerpunktdurchmesser.

Hohlquerschnitte werden über Außenabmessungen und konstante Wandstärke
definiert. Die Innenabmessungen müssen positiv bleiben. Rechteckrohre haben
idealisierte scharfe Ecken. Reale Herstellerkennwerte können mit
`CustomSection` ohne Geometrieapproximation übernommen werden.

## Allgemeiner Querschnitts-Geometriekern

`Core.Sections.Geometry` beschreibt eine zusammenhängende Materialfläche mit
einer einfachen Außenkontur und optionalen einfachen Innenkonturen.
Innenkonturen liegen strikt innen, berühren keine Kontur und dürfen weder
überlappen noch ineinander liegen. Konturen bestehen aus geordneten Geraden
und exakten Kreisbögen; benachbarte Endpunkte müssen exakt übereinstimmen.
Selbstüberschneidungen und überlappende Elemente sind ungültig. Die Konturrichtung
(CW/CCW) ändert die Kennwerte nicht; Löcher werden stets abgezogen.
Geometrien, Konturen und ihre kopierten Elementlisten sind immutable.

Ein `SectionArc` wird durch Mittelpunkt, Startpunkt, Endpunkt und Laufrichtung
eindeutig bestimmt. Gleiche Endpunkte bedeuten einen Vollkreis; verschiedene
Endpunkte müssen eine darstellbare, von null verschiedene Winkeländerung
definieren. Die beiden Radien dürfen nur um Floating-Point-Rundung abweichen
(relativ höchstens `64 * 2.2204460492503131e-16`). Endpunkte werden nicht
verschoben oder repariert. Der Radius stammt aus dem Startpunktabstand;
`Reversed()` erhält Radius und Sweepbetrag desselben analytischen Bogens.
Ein späterer Editor muss gemeinsame Endpunkte wiederverwenden und die
Laufrichtung ausdrücklich festlegen.

Querschnittskoordinaten verwenden y horizontal und z vertikal.
`SectionCoordinate` speichert vorzeichenbehaftete Koordinaten in m;
`Length` bleibt ein nichtnegativer Abstand. Der Schwerpunkt ist (yS, zS).
Für die Schwerpunktkoordinaten Y = y - yS, Z = z - zS gilt:

- A = ∫ dA; yS = ∫ y dA / A; zS = ∫ z dA / A.
- Iy = ∫ Z² dA, Iz = ∫ Y² dA.
- Iyz = ∫ YZ dA, ausdrücklich ohne vorgeschaltetes Minuszeichen.
  `ProductMomentOfArea` speichert dieses vorzeichenbehaftete Moment in m⁴;
  `SecondMomentOfArea` bleibt nichtnegativ.
- Der Übergang von Ursprungs- zu Schwerpunktmomenten ist
  Iy = Iy0 - A zS², Iz = Iz0 - A yS², Iyz = Iyz0 - A yS zS.

`SectionGeometry.CalculateProperties()` liefert Flächen- und Schwerpunktmomente,
Hauptmomente und vier elastische Widerstandsmomente. Geraden und Kreisbögen
werden analytisch über die Kontur integriert; Bögen werden nicht polygonisiert.
Randfaserabstände berücksichtigen auch innere Extrempunkte auf Kreisbögen.

I1 ist das größere, I2 das kleinere Hauptträgheitsmoment. Die gerichtete
erste Hauptachse hat e1 = (cos θ, sin θ), die zweite e2 = (-sin θ, cos θ).
θ läuft von +y nach +z und liegt in [-π/2, π/2); die vertikale erste Achse
zeigt somit nach -z. Diese Achsen diagonalisieren
`[[Iy, -Iyz], [-Iyz, Iz]]`. Bei einem Hauptmomentunterschied von höchstens
`64 * 2.2204460492503131e-16 * max(Iy, Iz)` wird θ deterministisch auf 0
gesetzt. Dies ist ausschließlich eine relative Rundungsregel; I1/I2 und Iyz
werden dadurch nicht künstlich gleichgesetzt beziehungsweise auf null gesetzt.

Für Biegung um Achse 1 ist d1 = (Y, Z) · e2, für Achse 2 ist
d2 = (Y, Z) · e1. Die positiven und negativen Randfaserabstände sind
c1+ = max(d1), c1- = -min(d1), c2+ = max(d2), c2- = -min(d2).
Alle vier Abstände sind positive Beträge. Es gilt W1± = I1 / c1± und
W2± = I2 / c2±. Die Vorzeichen bezeichnen die jeweilige geometrische Seite,
keine Aussage über Zug oder Druck. Eine Achsenumorientierung um π vertauscht
jeweils die positiven und negativen Seiten.

Eingaben und Ergebnisse müssen als endliche double-Werte darstellbar sein;
A, Iy, Iz, I1, I2, Randfaserabstände und W müssen streng positiv bleiben.
Nicht darstellbare Werte werden mit Argument-Exceptions abgewiesen.
Rundungsvergleiche bei analytischen Schnittpunkten sind keine fachlichen
Längen- oder Flächentoleranzen. In den Eingabekoordinaten bereits verlorene
Geometriedetails können nicht rekonstruiert werden.

## Parametrische Querschnittsgeometrien

Die parametrischen Definitionen in `Core.Sections.Parametric` behalten ihre
fachlichen Abmessungen als immutable Längenwerte und erzeugen daraus eine
`SectionGeometry`. Ihre Kennwerte stammen ausschließlich aus dem allgemeinen
Geometriekern. Sie implementieren den fachlichen Vertrag `IParametricSectionDefinition`; das
bestehende `Section`-Modell verwendet weiterhin seinen bisherigen Kennwertvertrag.

Alle Formen verwenden y horizontal und z vertikal. Die Bounding-Box beginnt
links unten bei (0, 0); die Geometrie wird nicht auf ihren Schwerpunkt verschoben.
Breite b und Höhe h bezeichnen die äußeren Abmessungen, bei Kreisformen begrenzt
der Außendurchmesser beide Koordinatenrichtungen.

- **Rechteck:** `Width` b und `Height` h; ausschließlich gerade Kanten.
- **Rechteckrohr:** `Width` b, `Height` h, `WallThickness` t und `OuterRadius`
  rOuter. Das Loch liegt um t von den äußeren Begrenzungen eingerückt. Es gibt
  nur einen unabhängigen Radius; `rInner = max(0, rOuter - t)`.
- **Rundstab:** `Diameter` d; Mittelpunkt (d/2, d/2).
- **Rundrohr:** `OuterDiameter` d und `WallThickness` t; konzentrisches Loch
  mit Innendurchmesser d - 2t, Mittelpunkt (d/2, d/2).
- **I-/H-Profil:** `Height` h, `Width` b, `WebThickness` tw,
  `FlangeThickness` tf und `Radius` r. Der vertikale Steg liegt horizontal
  mittig; beide horizontalen Flansche sind gleich dick. Das Profil ist doppelt
  symmetrisch. r beschreibt die vier inneren Steg-Flansch-Übergänge.
- **U-Profil:** dieselben fünf Parameter. Der Steg liegt links, beide
  Flansche zeigen nach rechts. Das Profil ist zur horizontalen Mittellinie
  symmetrisch. r beschreibt die beiden inneren Steg-Flansch-Übergänge.
- **T-Profil:** dieselben fünf Parameter. Der Flansch liegt oben und der
  vertikale Steg horizontal mittig. Das Profil ist zur vertikalen Mittellinie
  symmetrisch. r beschreibt die beiden inneren Steg-Flansch-Übergänge.
- **Winkelprofil:** `Width` b, `Height` h, `Thickness` t und `InnerRadius` r.
  Der vertikale Schenkel liegt links, der horizontale unten. Beide besitzen
  dieselbe Dicke; ungleichschenklige Winkel sind zulässig. r beschreibt
  ausschließlich den inneren Übergang zwischen den Schenkeln.

Für jeden Radiusparameter bedeutet r = 0 scharfkantig, r > 0 verrundet.
Rundungen und Kreisformen bestehen aus exakten Kreisbögen. Die Außenkanten
von I-, U-, T- und Winkelprofilen bleiben scharf. Geometrisch gültige
Grenzradien dürfen gerade Reststrecken vollständig aufzehren; diese Strecken
entfallen dann. Positive Materialbreiten, Löcher und Geometriedetails müssen
darstellbar bleiben. Ungültige Eingaben werden abgelehnt und nicht begrenzt,
verkleinert oder durch verschobene Punkte repariert.

## Fachliche Querschnittsdefinitionen und Biegeachsen

`ISectionDefinition` beschreibt Fläche A und die verfügbaren fachlichen
Biegeachsen. `SectionAxisDesignation` identifiziert Y (y-y), Z (z-z), U (u-u)
und V (v-v) ohne führende Stringrepräsentation. Jede Achse liefert über
`SectionAxisProperties` ihr Flächenträgheitsmoment sowie getrennte positive
und negative elastische Widerstandsmomente. Die Vorzeichen bezeichnen
geometrische Seiten, nicht Zug oder Druck. Ein einzelnes W beschreibt den
geometrischen Vertrag asymmetrischer Querschnitte nicht vollständig.

`IParametricSectionDefinition` ergänzt die stabile Formidentität
`SectionShapeKind`, die ursprüngliche Geometrie und deren vollständige
Kennwerte. Die konkreten Definitionen erhalten ihre typisierten Parameter.
Die mathematischen Hauptachsen 1/2 sind weiterhin Rohdaten des Geometriekerns;
sie sind keine fachlichen Achsenbezeichnungen für bekannte Profile.

Rechteck, Rechteckrohr, Kreis, Rundrohr, I-/H-, U- und T-Profil bieten genau
Y und Z an. Dabei gilt Iy = ∫ Z² dA und Iz = ∫ Y² dA unabhängig davon,
welches Moment größer ist. Wy+ gehört zur +z-Seite, Wy− zur −z-Seite,
Wz+ zur +y-Seite und Wz− zur −y-Seite. Jedes Widerstandsmoment ist das
zugehörige Schwerpunktmoment dividiert durch den tatsächlichen positiven
Randfaserabstand auf dieser Seite. Die Werte stammen aus der allgemeinen
Konturgeometrie einschließlich exakter Kreisbögen. Die deterministische
Achsenkonvention des Kerns bleibt auch bei isotropen Formen erhalten.

Winkelprofile bieten für Biegung genau U und V an: u-u ist die größere
Hauptachse mit Iu = I1 und Wu± = W1±, v-v die kleinere mit Iv = I2 und
Wv± = W2±. Die Seiten folgen unverändert den gerichteten Hauptachsen des
Geometriekerns. Iy, Iz und Iyz bleiben zusammen mit Schwerpunkt,
Hauptachsenwinkel, Randfaserabständen und allen Hauptachsenkennwerten über
die vollständigen Geometrieeigenschaften erreichbar. Zwischen y/z und u/v
findet keine automatische Umdeutung statt.

`ManualSectionDefinition` übernimmt Tabellenkennwerte ohne angenommene
Geometrie: eine endliche, streng positive Fläche und eine oder zwei
`ManualSectionAxis`-Eingaben mit expliziter Bezeichnung, positivem endlichem
I und W. Weil nur ein W eingegeben wird, gilt ausdrücklich **W+ = W− = W**.
Jede einzelne Y-, Z-, U- oder V-Achse ist zulässig. Zwei Achsen müssen
verschieden sein und das Paar Y/Z oder U/V bilden; Mischpaare sind ungültig.
Die Eingabereihenfolge verändert die Bedeutung nicht. Es werden weder eine
fehlende Achse noch Schwerpunkt, Koordinatenmomente, Hauptachsenwinkel oder
Geometrie ergänzt; gleiche I-Werte bedeuten keine gleiche Achsenidentität.

Achsenlisten sind unveränderlich und kanonisch Y/Z beziehungsweise U/V
geordnet. `GetAxis` verlangt eine explizite Bezeichnung: ungültige Enumwerte
führen zu `ArgumentOutOfRangeException`, nicht vorhandene gültige Achsen zu
`KeyNotFoundException`. Die für einen Balken tatsächlich verwendete Achse
gehört nicht intrinsisch zum Querschnitt. Keine Querschnittsdefinition
speichert daher eine Achsenauswahl.

## Querschnitt und ausgewählte Balkenachse

`BeamModel.Section` enthält die ursprüngliche `ISectionDefinition`.
`BeamModel.BendingAxis` wählt genau eine ihrer verfügbaren Achsen aus;
`BendingAxisProperties` enthält deren bei der Konstruktion einmal aufgelöste
Eigenschaften I, W+ und W−. Es gibt keine automatische Ersatzachse.
Die Properties sind unveränderlich. `BeamSolution.Beam` behält das ursprüngliche
Modell; A/I/W werden nicht redundant in die Lösung kopiert.

Der Konstruktor für allgemeine `ISectionDefinition` verlangt eine explizite
`SectionAxisDesignation`, auch für parametrische und manuelle Definitionen mit
nur einer Achse. Undefinierte Enumwerte werden mit `ArgumentOutOfRangeException`,
für den konkreten Querschnitt fehlende gültige Achsen mit `ArgumentException`
abgewiesen; beide nennen den Parameter `bendingAxis`. Diese intrinsische
Gültigkeit wird vor jedem Solverlauf sichergestellt.

Nur der Kompatibilitätskonstruktor mit einer alten `Section` wählt automatisch Y.
Bestehende Desktop-Dokumente und Projekte V1 nutzen diesen Weg ohne zusätzliche
Achsenauswahl und behalten dieselben Kennwerte und Berechnungsergebnisse.
Die 2D-Balkendarstellung repräsentiert jeweils die ausgewählte Biegeebene,
keine räumliche Einbaulage des Querschnitts.

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
Überhänge werden hier nicht abgelehnt. Stabilitätsprüfung und Zuordnung der
Randbedingungen erfolgen im Solver; Details stehen in [SOLVER.md](SOLVER.md).
