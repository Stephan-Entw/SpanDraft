# SpanDraft-Projektformat

## Eigenständige Projektdateien

Dateiendung: `.spandraft` (beim Speichern ohne Beachtung der Groß-/Kleinschreibung).
Projektdateien sind lesbares, eingerücktes UTF-8-JSON mit `format = "SpanDraft.Project"`,
`formatVersion`, optionalem `info`, `document` und `presentation`. **V3 ist das einzige
aktuelle Schreibformat; V1 und V2 bleiben historische Leseverträge.**
DTOs und Codec-Tests definieren die technischen Felder und Typen; dieser Text
beschreibt die dauerhaften fachlichen Regeln. Core- und Editorobjekte werden
nicht direkt serialisiert. Es gibt weder CLR-Typnamen noch numerische Enumwerte.

Ein Projekt enthält vollständige Snapshots seiner Material- und
Querschnittsdefinition. Zum Öffnen, Berechnen oder Speichern werden keine
Bibliotheken, Presets, lokalen JSON-Kataloge oder externen Quellen benötigt.
Bibliotheks-/Preset-/Quellen-IDs und Herkunfts-/Provenienzverweise gehören nicht
zum Projektformat. Bibliotheken dienen ausschließlich zur Auswahl und
Wiederverwendung; übernommene Daten bleiben vollständig unabhängig.

## Material, Querschnitt und Biegeachse

Material speichert Name, E und fy sowie optional Dichte ρ und Poissonzahl ν.
Fehlende oder null gesetzte optionale Werte bedeuten tatsächlich unbekannt.
Beim Laden werden keine angenommenen Werte ergänzt. ρ und ν haben derzeit
keine Wirkung auf Solver oder Engineering.

Parametrische Querschnitte speichern ausschließlich die Form und ihre
ursprünglichen Abmessungen einschließlich der unabhängigen Radiusparameter.
Der seit V2 unveränderte Dokumentvertrag kennt `rectangle`, `rectangularHollow`,
`circle`, `circularHollow`, `iSection`, `uSection`, `tSection` und `angle`. Fläche, Schwerpunkt, Flächen-/Hauptmomente,
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

Das Dokument kann zusätzlich einen optionalen Querschnittsanzeigenamen enthalten.
Fehlend oder null bedeutet eine automatisch aus der Definition erzeugte Bezeichnung;
diese folgt der aktuellen Sprache und den Anzeigeeinheiten. Ein gesetzter Name
ist nichtleer und getrimmt. Innerhalb des übernommenen Querschnittssnapshots
bleibt er bei Änderungen seiner Abmessungen erhalten, bis der Anwender ihn
ändert oder zur automatischen Bezeichnung zurückkehrt. Drafts verschiedener
Grundformen sind transienter UI-Zustand und besitzen getrennte Namen; nur der
Anzeigename des aktuell übernommenen Querschnitts wird im Projekt gespeichert.
Benutzer- und übernommene Bibliotheksnamen verwenden denselben Metadatenvertrag;
sie enthalten keine Herkunftsreferenz und sind keine mechanische Eigenschaft.
Der optionale Anzeigename ist bereits Teil des historischen V2-Vertrags;
ältere Dateien benötigen dieses Feld nicht. Namensänderungen gehören zu
Projektinhalt, Dirty-State und Undo/Redo,
lösen jedoch keine mechanische Neuberechnung aus.

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

## Projektangaben

V3 ergänzt `info` außerhalb von Dokument und Presentation. Es enthält
`calculationTitle` (Berechnung des konkreten Bauteils/Modells), `author`
(Ersteller als natürliche Person), optional `projectTitle` (übergeordnetes
Projekt) und `reference` (freie externe Zuordnung, etwa Zeichnungs- oder
Auftragsnummer). Berechnung und Ersteller sind nach Trim nichtleer; optionale
Leerwerte werden null. Alle gespeicherten Angaben sind getrimmt. Es gibt keine
weiteren Dokumentmanagement-Felder oder Ableitungen aus dem Dateinamen.

Neue Projekte besitzen vollständige Angaben. Bei migrierten Legacy-Projekten
darf `info` fehlen oder null sein, auch nach erneutem Speichern als V3. Ein
vorhandenes `info` muss beide Pflichtwerte gültig enthalten. Legacy-Bearbeitung
mit vier leeren Angaben erhält null; bei angelegten Angaben werden beide
Pflichtwerte verlangt. Projektangaben gehören zu Inhaltsvergleich, Dirty-State,
Undo/Redo und Recovery. Ihre Änderung allein löst keine Analysis aus.

## V1-/V2-Migration und Legacy-Kompatibilität

V1 und V2 werden mit fehlenden Projektangaben (`Info == null`) gelesen.
Der Dokument-/Presentation-Vertrag aus V2 bleibt in V3 unverändert.
V1 wird beim Laden unmittelbar ins aktuelle In-Memory-Modell migriert:
Rechteck, Rechteckrohr, Kreis und Rundrohr werden parametrische Definitionen;
Rechteckrohre erhalten Außenradius 0. V1-`custom` wird Manual mit originalem
A/I/W und einer Y-Achse. Alle V1-Dokumente verwenden Y; Dichte und Poissonzahl
bleiben null. Ihre mechanischen Kennwerte und Berechnungsergebnisse bleiben erhalten.

Bloßes Öffnen verändert keine Benutzerdatei und macht das geöffnete Projekt
nicht allein wegen der Migration dirty. Erst ein tatsächliches Speichern
schreibt V3; ursprünglicher Dateipfad und normales Save-/Save-As-Verhalten bleiben erhalten.
Es gibt keinen V1-/V2-Writer. Noch verwendete Legacy-Sections werden beim V3-Schreiben
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
denselben Codec: neue Recovery schreibt V3; historische V1-/V2-Projekte bleiben lesbar.
Nur committed Zustände einer dirty Session werden geschrieben; nach Wiederherstellen
bleibt das Projekt dirty. Die Benutzerdatei wird nicht automatisch geändert.
Recovery wird ohne Nachfrage entfernt, wenn ihr vollständiger Projektinhalt
mit der ursprünglichen Projektdatei übereinstimmt.

Create-Drafts verwenden unabhängig davon den privaten Slot `setup-recovery.json`
mit `recoveryVersion = 2` und UTC-Zeitstempel; Version 1 bleibt ohne
Projektangaben lesbar. Er enthält ausschließlich
bestätigte, auch unvollständige Setup-Werte in SI sowie einen optionalen expliziten
Querschnittsnamen sowie die vier einzeln bestätigten Projektangaben, auch wenn
Berechnung oder Ersteller noch fehlen; keine Session, Eingabepuffer oder UI-Zustände.
Querschnitte und Materialien verwenden die bestehenden V2-DTOs. Beim Start wird zuerst die
Projekt-Recovery bearbeitet, danach der Create-Draft über dem restaurierten Editor
oder eigenständig ohne Session geöffnet. Cancel im Recovery-Dialog erhält den Slot.
Bewusstes Abbrechen des Drafts, erfolgreiches Öffnen eines anderen Projekts,
normales Beenden und erfolgreiche Projekterstellung entfernen die Setup-Recovery;
eine abgebrochene oder fehlgeschlagene Erstellung erhält sie.

## Lokal zuletzt verwendeter Ersteller

User Settings schreiben Version 3 mit optionalem `lastAuthor`. V1/V2 bleiben
lesbar und liefern keinen zuletzt verwendeten Ersteller. Bisheriges Entwicklungs-V3
mit `defaultAuthor` bleibt lesbar; ist `lastAuthor` vorhanden, hat es auch bei null
Vorrang. Neue Dateien schreiben ausschließlich `lastAuthor`. Leerwerte werden null,
andere Werte getrimmt. Der Wert ist ein lokaler Komfortwert, keine sichtbare
Benutzereinstellung und niemals Teil einer Projektrevision.

Ausschließlich erfolgreiche Neuanlagen aktualisieren den Wert mit dem normalisierten
Ersteller des aktivierten Projekts. Die lokale Persistierung erfolgt best-effort;
ein Schreibfehler beeinflusst weder das erstellte Projekt noch den laufenden
Komfortwert. Eingaben, Abbruch, Edit, Öffnen, Speichern, Undo/Redo und normale
Projekt-Recovery ändern ihn nicht. Ein frischer Create-Draft erhält einmalig eine
eigene Kopie. Fehlt der lokale Komfortwert, wird unter Windows der aktuelle
Anmeldename als bearbeitbarer Vorschlag verwendet; ist er nicht verfügbar, bleibt
das Feld leer. Auch dieser Vorschlag wird erst nach erfolgreicher Neuanlage als
`lastAuthor` gespeichert. Bei Create-Recovery gelten ausschließlich die gespeicherten
Werte, auch ein fehlender Ersteller; bestehende Projekte erhalten keine Vorbelegung.
