# SpanDraft-Projektformat

## Eigenständige Projektdateien

Dateiendung: `.spandraft` (beim Speichern ohne Beachtung der Groß-/Kleinschreibung).
Projektdateien sind lesbares, eingerücktes UTF-8-JSON mit `format = "SpanDraft.Project"`,
`formatVersion`, optionalem `info`, `document` und `presentation`. **V5 ist das einzige
aktuelle Schreibformat; V1–V4 bleiben historische Leseverträge.**
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

Der optionale Materialhinweis wird kompatibel ohne Versionswechsel als
`note: { "default": "…", "translations": { "de": "…" } }` gespeichert.
`default` ist bei vorhandenem Hinweis verpflichtend und nichtleer; Übersetzungen
sind optional. Ohne Hinweis entfällt `note`, ohne Übersetzungen entfällt
`translations`. Die Schlüsselreihenfolge ist deterministisch. Der vollständige
Hinweis bleibt auch in Projekt- und Setup-Recovery erhalten. Fehlende historische
Hinweise bleiben null; sie werden niemals aus dem aktuellen Katalog ergänzt.
Hinweise verändern Projektinhalt und History, aber keine Berechnung.


Material speichert Name, Kategorie, optionale Werkstoffnummer, geordnete andere
Normbezeichnungen, E und den numerischen Festigkeitskennwert
(`yieldStrength`) sowie `strengthType` mit den stabilen Stringwerten `re` und
`rp02`. Dichte ρ und Poissonzahl ν bleiben optional. Das Typfeld erweitert die
bestehenden Verträge kompatibel ohne neue Versionsnummer. Historische Dateien
ohne Typ (oder mit null) laden ungeklärt, ohne Ableitung aus Name oder Kategorie.
Unbekannte Typwerte sind ungültig. Reguläres Speichern verlangt einen expliziten
Typ; eine Typänderung gehört zu Inhalt und History, nicht zur Mechanik.
Fehlende oder null gesetzte optionale Werte bedeuten tatsächlich unbekannt.
Beim Laden werden keine angenommenen Werte ergänzt. ρ bestimmt bei ausdrücklich
aktiviertem Eigengewicht die abgeleitete Linienlast;
ohne Eigengewicht bleibt eine Dichteänderung mechanisch irrelevant. ν hat derzeit
keine Wirkung auf Solver oder Engineering. Kategorie, Werkstoffnummer und
Normbezeichnungen erweitern den bestehenden Materialvertrag ebenfalls ohne
Versionswechsel. Historische Projekte ohne Kategorie laden mit `Other`, ohne
Werkstoffnummer mit null und ohne Normliste mit einer leeren Sammlung. Es gibt
keine Ergänzung aus einem heutigen Katalog; unbekannte Kategorien sind ungültig.
Alle Materialmetadaten bleiben auch in Projekt- und Setup-Recovery erhalten.

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
Länge/Position in m, A in m², I in m⁴, W in m³, E/Festigkeitskennwert in Pa, Dichte in kg/m³,
Poissonzahl dimensionslos, Kraft in N, Moment in N·m und Linienlast in N/m.
Keine Einheitensymbole, Anzeigeeinheiten oder lokalisierten Zahlstrings.
Anzeigeeinstellungen sind davon unabhängig. Null ist für Positionen und Lasten gültig.

IDs, Namen, Entity-Reihenfolge, Lasten, Lager und Naming-Zähler bleiben erhalten.
Lager verwenden `fixed`, `pinned`, `roller` oder `spring`; Punktlasten `force`, `moment`.
Ein Federlager speichert optional `translationalStiffness` (N/m) und `rotationalStiffness`
(N·m/rad), beide endlich und streng positiv, sowie `rigidTranslation` (nur `true` wird
geschrieben). Mindestens eine Feder ist verpflichtend; `rigidTranslation` verlangt eine
Drehfeder ohne Wegfeder. Diese Felder sind nur bei `spring` zulässig und lassen
Dateien ohne sie unverändert gültig. Positionen eines Federlagers dürfen nicht mit
denen anderer Lager zusammenfallen.
Ein Balken ist die geordnete Kette seiner Abschnitte. Der erste Abschnitt steht in den
Dokumentfeldern `material`, `section`, `bendingAxis`, `sectionDisplayName`, `segmentId` (optional)
und `length`; `length` ist die Länge dieses ersten Abschnitts, bei einem Abschnitt also die
Balkenlänge. Alle weiteren stehen in der optionalen Liste `furtherSegments`; jeder Eintrag hat
`length` (m, größer null), `id` (optional), `material`, `section`, `bendingAxis` und optional
`sectionDisplayName` mit den Verträgen des Dokuments. Die Balkenlänge ist die Summe der
Abschnittslängen und wird nicht noch einmal gespeichert; Lücken und Überlappungen sind damit nicht
darstellbar. Eine Abschnitts-ID ist nicht leer und eindeutig; fehlt sie, vergibt der Leser eine,
sodass nichts auf einen solchen Abschnitt verweisen kann. Entwürfe dieser Formatversion beschrieben
jeden weiteren Abschnitt durch `start` (m, streng aufsteigend und im Balkeninneren) statt durch
`length`; sie werden weiter gelesen, die Längen folgen aus den Beginnen und der Balkenlänge in
`length`, und beim Schreiben entsteht die Form mit Längen. Ein Eintrag hat entweder `length` oder
`start`, nie beides. Ohne `furtherSegments` (ein Abschnitt) wird die Liste nicht geschrieben.
Eigengewicht bleibt ein Eintrag `selfWeight`; seine Last je Abschnitt wird abgeleitet und verlangt
die Dichte jedes Werkstoffs.
Jede Position eines Lagers, einer Punktlast, eines Gelenks und jedes Endes einer Streckenlast steht
in genau einer von zwei Formen, nie in beiden: als `position` (m), ein fester Abstand vom
Balkenanfang (so schreiben und lesen alle Versionen), oder als `positionRef` mit `segment` (ID eines
Abschnitts) und genau einem von `offset` (m, höchstens die Länge dieses Abschnitts, vom
Abschnittsbeginn gemessen) oder `atEnd: true` (Bindung an das Ende dieses Abschnitts, ohne eigenen
Abstand). Bei Streckenlasten gilt das für `startPosition`/`startPositionRef` und
`endPosition`/`endPositionRef` getrennt. Ein Verweis auf einen unbekannten Abschnitt oder ein
Abstand über den Abschnitt hinaus macht die Datei ungültig. Die globale Position wird beim Lesen
aus Bezug und Abschnittslängen berechnet und nicht gespeichert.
Innere Gelenke (Momentenfreigaben) stehen in der optionalen Liste `momentReleases` des
Dokuments. Jeder Eintrag hat `id`, `name` und `position` (m, streng im Balkeninneren) oder `positionRef`. Ohne Gelenk
wird die Liste nicht geschrieben, sodass sich bestehende Dateien beim Öffnen und Speichern nicht
verändern; eine Datei ohne das Feld hat keine Gelenke. Die Core-Regeln (kein Gelenk an einem
Balkenende, kein zweites an gleicher Position, keines an einer Einspannung, einem Drehfederlager
oder einem Punktmoment) prüft der Leser als `ProjectFormatException`; ob ein Modell mit Gelenken
ein Mechanismus ist, entscheidet der Solver. Die Annotation-GUID eines Gelenks ist seine `id`.
Streckenlasten stehen in `distributedLoads` und speichern Anfangs- und Endposition
sowie `startIntensity` und `endIntensity` (N/m, vorzeichenbehaftet, beide Pflicht).
Die Intensität ändert sich zwischen den Enden linear. Gleiche Werte bezeichnen eine
konstante Last; Null und Vorzeichenwechsel sind gültig. Das Projekt speichert weder
Resultierende noch Schwerpunkt.
Naming-Zähler sind positive Ganzzahlen und unabhängig von den Entity-Namen. Der Zähler der
Gelenke, `namingState.nextMomentReleaseNumber`, ist optional und gilt ohne Feld als 1; er wird nur
geschrieben, wenn er größer als 1 ist.

Annotation-Offsets referenzieren Entities durch GUID und speichern relative
DIPs zum automatischen Anker. Ein fehlender Offset bedeutet automatische
Annotation. Die Presentation-Daten bleiben bei Migration und Roundtrips erhalten.

Nicht persistiert werden Analysis-/Solver-/Engineering-Ergebnisse, History,
RevisionIds/Savepoint, Dateipfad, Hover/Selection/Werkzeuge, Draft-/Textbuffer,
Placement-/Drag-Vorschauen, Fehler-/Konfliktzustände sowie Canvas-/Viewport- und
Stationslayout. Öffnen beginnt mit leerer History und berechnet Analysis neu.

## Eigengewicht

Eigengewicht ist ein optionaler eigener Dokumentzustand, keine benutzerdefinierte
Streckenlast. `document.selfWeight` enthält ausschließlich eine nichtleere,
projektweit eindeutige `id` und `enabled`. Fehlend oder null bedeutet aus, ohne
angelegte Entity. Die ID bleibt beim Deaktivieren und erneuten Aktivieren erhalten.
Intensität, Wirkungsbereich und Systembezeichnung werden nicht gespeichert:
Aktives Eigengewicht wird gemäß [DOMAIN.md](DOMAIN.md#materialien) in Core
abgeleitet und der Analyse als normale konstante Streckenlast über `0 … L`
zugeführt. Eine bekannte Materialdichte ist dafür zwingend; auch beim
Materialwechsel darf sie nicht entfallen.

Die feste Systembezeichnung `g` ist kein frei vergebener Entity-Name und verbraucht
keinen Naming-Zähler. Neue normale Entities dürfen weder `g` noch `G` heißen;
automatische Lagernamen überspringen `G`. Bereits vorhandene solche Namen bleiben
beim Laden unverändert. Eigengewicht darf erst nach ihrer Umbenennung aktiviert
werden; aktives Eigengewicht mit einem solchen Konflikt ist ungültig.

`presentation.selfWeightVisible` ist optional und gilt ohne Feld als true.
Ausblenden beeinflusst ausschließlich die Darstellung, nicht die mechanische
Aktivierung. Der relative Label-Offset verwendet dieselbe Annotation-GUID wie
andere Entities. Auch deaktiviertes Eigengewicht bleibt eine gültige
Annotationreferenz und erhält Offset und Sichtbarkeit. Diese Daten gehören zum
Inhaltsvergleich, Dirty-State, Undo/Redo und zur vollständigen Projekt-Recovery.

Diese optionalen Felder erweitern V3 kompatibel ohne Versionssprung. Historische
V1-/V2-/V3-Dateien ohne Eigengewicht behalten ihre bisherigen Analyseergebnisse;
der gemeinsame Dokumentreader für V2–V5 akzeptiert die optionalen Erweiterungen.

## Projektangaben

V3 ergänzt `info` außerhalb von Dokument und Presentation. Es enthält
`calculationTitle` (Berechnung des konkreten Bauteils/Modells), `author`
(Ersteller), optional `projectTitle` (übergeordnetes Projekt) und `reference`
(freie externe Zuordnung, etwa Zeichnungs- oder Auftragsnummer).
Der historische V3-Lesevertrag verlangt bei vorhandenem `info` weiterhin einen
nach Trim nichtleeren Berechnungstitel und Ersteller. Ein fehlender, null
oder leer gesetzter Ersteller ist dort ungültig.

V4 macht `author` optional: fehlende, null und nach Trim leere Werte werden null.
`calculationTitle` bleibt bei vorhandenem `info` verpflichtend und nach Trim
nichtleer. Auch die übrigen optionalen Leerwerte werden null. Alle gespeicherten
Angaben sind getrimmt. Es gibt keine weiteren Dokumentmanagement-Felder oder
Ableitungen aus dem Dateinamen.

Neue Projekte besitzen einen Berechnungstitel; ein Ersteller ist freiwillig.
Bei migrierten Legacy-Projekten darf `info` fehlen oder null sein, auch nach
erneutem Speichern als V5. Legacy-Bearbeitung mit vier leeren Angaben erhält null;
sobald Angaben angelegt werden, wird ausschließlich der Berechnungstitel verlangt.
Projektangaben gehören zu Inhaltsvergleich, Dirty-State, Undo/Redo und Recovery.
Ihre Änderung allein löst keine Analysis aus.

## V1-/V2-Migration und Legacy-Kompatibilität

V1 und V2 werden mit fehlenden Projektangaben (`Info == null`) gelesen.
V3 und V4 verwenden weiterhin die Dokument-/Presentation-Struktur aus V2.
V5 ersetzt das Streckenlastfeld `intensity` durch `startIntensity`
und `endIntensity` und erweitert Lager additiv um den Typ `spring` mit seinen optionalen
Feldern (V1–V4 kennen ihn nicht und lehnen ihn ab) sowie das Dokument um die optionalen Listen
`furtherSegments` (mit Längen und IDs), `momentReleases`, die Positionsbezüge `positionRef` und den optionalen Naming-Zähler der Gelenke; beim Lesen von V1–V4 wird die einzelne Intensität zu beiden
Enden. V5 ist eine reine Erweiterung des Dokumentformats, ältere Programmstände
lehnen V5-Dateien als nicht unterstützt ab, auch wenn alle Lasten konstant sind.
V1 wird beim Laden unmittelbar ins aktuelle In-Memory-Modell migriert:
Rechteck, Rechteckrohr, Kreis und Rundrohr werden parametrische Definitionen;
Rechteckrohre erhalten Außenradius 0. V1-`custom` wird Manual mit originalem
A/I/W und einer Y-Achse. Alle V1-Dokumente verwenden Y; Dichte und Poissonzahl
bleiben null. Ihre mechanischen Kennwerte und Berechnungsergebnisse bleiben erhalten.

Bloßes Öffnen verändert keine Benutzerdatei und macht das geöffnete Projekt
nicht allein wegen der Migration dirty. Erst ein tatsächliches Speichern nach
expliziter Klärung des Kennwerttyps schreibt V5; ursprünglicher Dateipfad und normales Save-/Save-As-Verhalten bleiben erhalten.
Es gibt keinen V1-/V2-/V3-/V4-Writer. Noch verwendete Legacy-Sections werden beim V5-Schreiben
auf dieselben kanonischen Formen normalisiert; es gibt keinen Legacy-Wiretyp.

`Load → Save → Load` erhält den fachlichen Inhalt. Der Inhaltsvergleich verwendet
Originalparameter beziehungsweise Manual-Eingaben, vollständige Materialdaten
und Biegeachse. Er ist vom mechanischen Vergleich getrennt: Änderungen nur an
Name/Kategorie/Werkstoffnummer/Normbezeichnungen/Kennwerttyp/ν sind
Projektänderungen ohne Neuberechnung; für ρ gilt dies nur ohne aktives Eigengewicht.
Die Achsenidentität ist
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
denselben Codec: neue Recovery schreibt V5; historische V1-/V2-/V3-/V4-Projekte bleiben lesbar.
Ausschließlich private Recovery darf ungeklärte Legacy-Materialien weiterhin
verlustfrei sichern; dieselben Materialmappings erhalten den fehlenden Typ.
Das gilt auch für Setup-Recovery. Reguläres Projektspeichern und neue
Reportausgabe bleiben bis zur Typklärung gesperrt.
Nur committed Zustände einer dirty Session werden geschrieben; nach Wiederherstellen
bleibt das Projekt dirty. Die Benutzerdatei wird nicht automatisch geändert.
Recovery wird ohne Nachfrage entfernt, wenn ihr vollständiger Projektinhalt
mit der ursprünglichen Projektdatei übereinstimmt.

Create-Drafts verwenden unabhängig davon den privaten Slot `setup-recovery.json`
mit `recoveryVersion = 2` und UTC-Zeitstempel; Version 1 bleibt ohne
Projektangaben lesbar. Er enthält ausschließlich
bestätigte, auch unvollständige Setup-Werte in SI sowie einen optionalen expliziten
Querschnittsnamen sowie die vier einzeln bestätigten Projektangaben, auch wenn
der Berechnungstitel noch fehlt; keine Session, Eingabepuffer oder UI-Zustände.
Querschnitte und Materialien verwenden die bestehenden V2-DTOs. Beim Start wird zuerst die
Projekt-Recovery bearbeitet, danach der Create-Draft über dem restaurierten Editor
oder eigenständig ohne Session geöffnet. Cancel im Recovery-Dialog erhält den Slot.
Bewusstes Abbrechen des Drafts, erfolgreiches Öffnen eines anderen Projekts,
normales Beenden und erfolgreiche Projekterstellung entfernen die Setup-Recovery;
eine abgebrochene oder fehlgeschlagene Erstellung erhält sie.

## Lokal zuletzt verwendeter Ersteller

User Settings schreiben Version 4 mit optionalem `lastAuthor`; V1–V3 bleiben
lesbar. V1/V2 liefern keinen zuletzt verwendeten Ersteller. Historisches
Entwicklungs-V3 mit `defaultAuthor` bleibt lesbar; ist `lastAuthor` vorhanden,
hat es auch bei null Vorrang. Neue Dateien schreiben ausschließlich `lastAuthor`.
Leerwerte werden null, andere Werte getrimmt. Der Wert ist ein lokaler Komfortwert,
keine sichtbare Benutzereinstellung und niemals Teil einer Projektrevision.
Die Migration der numerischen Anzeigeeinstellungen steht in
[ENGINEERING.md](ENGINEERING.md#numerische-darstellung-in-app-und-bericht);
Settings-Version und Projektformatversion sind unabhängig.

Ausschließlich erfolgreiche Neuanlagen aktualisieren den Wert mit dem normalisierten
Ersteller des aktivierten Projekts, auch wenn dieser null ist. Die lokale
Persistierung erfolgt best-effort;
ein Schreibfehler beeinflusst weder das erstellte Projekt noch den laufenden
Komfortwert. Eingaben, Abbruch, Edit, Öffnen, Speichern, Undo/Redo und normale
Projekt-Recovery ändern ihn nicht. Ein frischer Create-Draft erhält einmalig eine
eigene Kopie. Fehlt der lokale Komfortwert, wird unter Windows der aktuelle
Anmeldename als bearbeitbarer Vorschlag verwendet; ist er nicht verfügbar, bleibt
das Feld leer. Auch dieser Vorschlag wird erst nach erfolgreicher Neuanlage als
`lastAuthor` gespeichert. Bei Create-Recovery gelten ausschließlich die gespeicherten
Werte, auch ein fehlender Ersteller; bestehende Projekte erhalten keine Vorbelegung.
