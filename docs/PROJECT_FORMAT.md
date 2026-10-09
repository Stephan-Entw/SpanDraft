# SpanDraft-Projektformat

## Eigenständige Projektdateien

Dateiendung: `.spandraft` (beim Speichern ohne Beachtung der Groß-/Kleinschreibung).
Projektdateien sind lesbares, eingerücktes UTF-8-JSON mit `format = "SpanDraft.Project"`,
`formatVersion`, `document` und `presentation`. **V2 ist das einzige aktuelle
Schreibformat; V1 bleibt über seinen eingefrorenen Vertrag lesbar.**
DTOs und Codec-Tests definieren die technischen Felder und Typen; dieser Text
beschreibt die dauerhaften fachlichen Regeln. Core- und Editorobjekte werden
nicht direkt serialisiert. Es gibt weder CLR-Typnamen noch numerische Enumwerte.

Ein Projekt enthält vollständige Snapshots seiner Material- und
Querschnittsdefinition. Zum Öffnen, Berechnen oder Speichern werden keine
Bibliotheken, Presets, lokalen JSON-Kataloge oder externen Quellen benötigt.
Bibliotheks-/Preset-/Quellen-IDs und Herkunfts-/Provenienzverweise gehören nicht
zum Projektformat. Spätere Bibliotheken dienen ausschließlich zur Auswahl und
Wiederverwendung; übernommene Daten bleiben vollständig unabhängig.

## Material, Querschnitt und Biegeachse

Material speichert Name, E und fy sowie optional Dichte ρ und Poissonzahl ν.
Fehlende oder null gesetzte optionale Werte bedeuten tatsächlich unbekannt.
Beim Laden werden keine angenommenen Werte ergänzt. ρ und ν haben derzeit
keine Wirkung auf Solver oder Engineering.

Parametrische Querschnitte speichern ausschließlich die Form und ihre
ursprünglichen Abmessungen einschließlich der unabhängigen Radiusparameter.
V2 kennt `rectangle`, `rectangularHollow`, `circle`, `circularHollow`, `iSection`,
`uSection`, `tSection` und `angle`. Fläche, Schwerpunkt, Flächen-/Hauptmomente,
Hauptachsenwinkel und Widerstandsmomente werden beim Laden durch den Geometriekern
neu berechnet. Auch abgeleitete Radien werden nicht zur zweiten Datenquelle.

`manual` speichert die originale Fläche A und eine oder zwei Achsen mit
Bezeichnung, I und einem eingegebenen Tabellenwert W. Fachlich gilt W+ = W− = W.
Eine einzelne Y/Z/U/V-Achse ist zulässig; zwei Achsen müssen Y/Z oder U/V bilden.
Doppelte Achsen und Mischpaare sind ungültig. Die gespeicherte Reihenfolge ist
kanonisch Y/Z beziehungsweise U/V. Fehlende Geometrie oder Achsen werden nicht ergänzt.

Die ausgewählte Biegeachse gehört separat zum Dokument, nicht zum Querschnitt.
Ihre stabilen Wire-Werte sind `y`, `z`, `u`, `v`. Der Querschnitt muss die
gewählte Achse tatsächlich anbieten; es gibt keine Ersatzachse. Die Identität
bleibt auch bei numerisch gleichen Kennwerten erhalten, etwa Kreis Y und Z.

## SI-Vertrag und übriger Projektinhalt

Physikalische Werte stehen ausschließlich in kanonischen SI-Einheiten:
Länge/Position in m, A in m², I in m⁴, W in m³, E/fy in Pa, Dichte in kg/m³,
Poissonzahl dimensionslos, Kraft in N, Moment in N·m und Linienlast in N/m.
Keine Einheitensymbole, Anzeigeeinheiten oder lokalisierten Zahlstrings.
Anzeigeeinstellungen sind davon unabhängig. Null ist für Positionen und Lasten gültig.

IDs, Namen, Entity-Reihenfolge, Lasten, Lager und Naming-Zähler bleiben erhalten.
Lager verwenden `fixed`, `pinned`, `roller`; Punktlasten `force`, `moment`.
Konstante Streckenlasten werden durch ihr Array bezeichnet.
Naming-Zähler sind positive Ganzzahlen und unabhängig von den Entity-Namen.

Annotation-Offsets referenzieren Entities durch GUID und speichern relative
DIPs zum automatischen Anker. Ein fehlender Offset bedeutet automatische
Annotation. Die Presentation-Daten bleiben bei Migration und Roundtrips erhalten.

Nicht persistiert werden Analysis-/Solver-/Engineering-Ergebnisse, History,
RevisionIds/Savepoint, Dateipfad, Hover/Selection/Werkzeuge, Draft-/Textbuffer,
Placement-/Drag-Vorschauen, Fehler-/Konfliktzustände sowie Canvas-/Viewport- und
Stationslayout. Öffnen beginnt mit leerer History und berechnet Analysis neu.

## V1-Migration und Legacy-Kompatibilität

V1 wird beim Laden unmittelbar ins aktuelle In-Memory-Modell migriert:
Rechteck, Rechteckrohr, Kreis und Rundrohr werden parametrische Definitionen;
Rechteckrohre erhalten Außenradius 0. V1-`custom` wird Manual mit originalem
A/I/W und einer Y-Achse. Alle V1-Dokumente verwenden Y; Dichte und Poissonzahl
bleiben null. Ihre mechanischen Kennwerte und Berechnungsergebnisse bleiben erhalten.

Bloßes Öffnen verändert keine Benutzerdatei und macht das geöffnete Projekt
nicht allein wegen der Migration dirty. Erst ein tatsächliches Speichern
schreibt V2; ursprünglicher Dateipfad und normales Save-/Save-As-Verhalten bleiben erhalten.
Es gibt keinen V1-Writer. Noch verwendete Legacy-Sections werden beim V2-Schreiben
auf dieselben kanonischen Formen normalisiert; es gibt keinen Legacy-Wiretyp.

`Load → Save → Load` erhält den fachlichen Inhalt. Der Inhaltsvergleich verwendet
Originalparameter beziehungsweise Manual-Eingaben, vollständige Materialdaten
und Biegeachse. Er ist vom mechanischen Vergleich getrennt: Änderungen nur an
Name/ρ/ν sind Projektänderungen ohne Neuberechnung; die Achsenidentität ist
auch bei gleichen I/W mechanisch relevant.

## Validierung und Recovery

Pflichtfelder müssen vorhanden und dürfen nicht null sein. Nicht endliche Zahlen,
ungültige Domainwerte, unbekannte Versionen/Typen/Achsen und doppelte
JSON-Eigenschaften werden als `ProjectFormatException` abgelehnt. Zusätzliche
unbekannte Felder werden ignoriert. Beschädigte Werte werden nicht korrigiert
oder begrenzt; Domain-Konstruktoren sind die letzte fachliche Validierungsinstanz.

Entity-GUIDs müssen gültig, nichtleer und projektweit eindeutig sein. Namen
müssen nichtleer, getrimmt und ohne Beachtung der Groß-/Kleinschreibung eindeutig
sein. Lagerpositionen müssen verschieden sein; Positionen und Lastbereiche
müssen innerhalb des Balkens liegen. Annotationen dürfen jede vorhandene Entity
höchstens einmal referenzieren. Projekte ohne Lager oder mit unzureichender
Lagerung bleiben persistierbar; Analysis beschreibt ihre Berechenbarkeit.

Recovery ist ein privater lokaler Slot, kein Autosave der Benutzerdatei.
Das bestehende interne Envelope bleibt bei `recoveryVersion = 1`, optionalem
Originalpfad und UTC-Zeitstempel. Sein eingebettetes Projekt verwendet genau
denselben Codec: neue Recovery schreibt V2, historische V1-Projekte bleiben lesbar.
Nur committed Zustände einer dirty Session werden geschrieben; nach Wiederherstellen
bleibt das Projekt dirty. Die Benutzerdatei wird nicht automatisch geändert.
Recovery wird ohne Nachfrage entfernt, wenn ihr vollständiger Projektinhalt
mit der ursprünglichen Projektdatei übereinstimmt.
