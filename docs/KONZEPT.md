# SpanDraft – Konzept

Stand: 09.10.2026

Dieses Dokument legt Produktvision, Scope, Architekturgrundsätze und Roadmap
von SpanDraft fest. Verbindliche Fach-, API- und Dateiverträge stehen in
den jeweils spezialisierten Dokumenten; Implementierungsdetails in Code und Tests.

## 1. Produktidee

SpanDraft ist eine Open-Source-Desktopanwendung für intuitive Balkenberechnungen
im Maschinenbau.

Das Programm soll die Lücke zwischen klassischer Hand-/Excel-Rechnung und
umfangreichen FEM-Systemen schließen. Der Anwender baut einen Balken wie eine
technische Skizze auf, positioniert Lager und Lasten direkt am Modell und erhält
die mechanischen Ergebnisse ohne einen allgemeinen FEM-Workflow aufsetzen zu
müssen.

Die Kernziele sind:

- schneller Modellaufbau
- direkte grafische Bearbeitung
- exakte numerische Eingabe
- transparente mechanische Berechnung
- nachvollziehbare Ergebnisse
- geringer Bedienaufwand

SpanDraft soll eher wie ein kleines technisches Konstruktionswerkzeug wirken
als wie ein Formularrechner.

## 2. Produktprinzip: Sketch-first

Die grafische Balkendarstellung ist der primäre Editor.

Der Anwender soll:

- mit einem vorhandenen geraden Balken beginnen
- Länge, Werkstoff und Querschnitt festlegen
- Lager und Lasten direkt platzieren
- Positionen und Werte exakt numerisch bearbeiten
- Objekte anklicken, ziehen und transaktional bearbeiten
- Ergebnisse unmittelbar nach bestätigten Änderungen sehen

Eine spätere Tabellenansicht darf denselben Projektzustand ergänzend darstellen,
aber nicht als zweites Modell führen.

Direkte Manipulation und numerische Eingabe folgen demselben Grundsatz:
Vorschau und Rohtext sind transient; erst eine bestätigte Änderung verändert
das Projekt und startet gegebenenfalls eine neue Berechnung.

## 3. Aktueller fachlicher Scope

SpanDraft betrachtet derzeit einen einzelnen geraden Balken mit konstantem
Werkstoff und konstantem Querschnitt.

Fachmodell und Rechenkern unterstützen:

- frei definierbare Balkenlänge
- Einspannung, Festlager und Loslager
- beliebig positionierte Lager einschließlich Überhängen
- Punktkräfte
- Punktmomente
- konstante Streckenlasten
- Rechteck
- Rechteck-/Vierkantrohr mit optionaler Außenrundung
- Kreis
- Rundrohr
- I-/H-Profil
- U-Profil
- T-Profil
- Winkelprofil
- manuelle Querschnittskennwerte A/I/W für eine oder zwei explizite Biegeachsen
- explizite Auswahl der verwendeten Biegeachse
- Werkstoff mit E-Modul und Streckgrenze sowie optional Dichte und Poissonzahl

Parametrische Querschnitte behalten ihre ursprünglichen Abmessungen und leiten
Fläche, Schwerpunkt, Flächenträgheits- und Widerstandsmomente aus der gemeinsamen
analytischen Konturgeometrie ab. Unsymmetrische Querschnitte können für eine
Biegeachse unterschiedliche positive und negative Widerstandsmomente besitzen.

Diese fachlichen Möglichkeiten sind über das Project Setup im Desktop
konfigurierbar. Der vollständige Material-, Querschnitts- und Biegeachsenworkflow
umfasst eingebaute Materialien, eigene Materialien, alle parametrischen Formen
und manuelle A/I/W-Eingaben. Eigene Materialien und Querschnittspresets können
in getrennten lokalen Benutzerbibliotheken wiederverwendet werden.

Der Solver verwendet drei Freiheitsgrade je Knoten:

- axiale Verschiebung u
- transversale Verschiebung w
- Rotation θ

Der aktuelle Lastumfang enthält noch keine axialen äußeren Lasten.

## 4. Ergebnisse

Der Rechenkern liefert unter anderem:

- Lagerreaktionen
- axiale Verschiebung und Normalkraft
- transversale Verschiebung
- Rotation
- Querkraft
- Biegemoment
- analytische globale Extremwerte

Die Engineering-Auswertung ergänzt:

- maximale Durchbiegung
- maßgebendes Biegemoment
- maximale elastische Biegespannung
- Sicherheitsfaktor gegenüber der hinterlegten Streckgrenze

Für die maximale elastische Biegespannung wird bei unsymmetrischen
Querschnitten das kleinere der beiden Widerstandsmomente der ausgewählten
Biegeachse maßgebend. Der Sicherheitsfaktor ist eine einfache elastische
Bewertung und kein normativer Festigkeitsnachweis.

Im Desktop werden kompakte Ergebniskennwerte und Lagerreaktionen sowie
Live-Diagramme für Durchbiegung, Querkraft und Biegemoment dargestellt.

## 5. Rechenmodell

SpanDraft verwendet einen eigenen transparenten Euler-Bernoulli-Balkensolver.

Relevante Positionen wie Balkenenden, Lager, Punktlasten und Grenzen von
Streckenlasten erzeugen automatisch Rechenknoten. Die Lösung erfolgt mit dem
direkten Steifigkeitsverfahren.

Math.NET Numerics übernimmt ausschließlich generische lineare Algebra.
Mechanisches Modell, Elementformulierung, Lasten, Randbedingungen und
Ergebnisinterpretation bleiben Bestandteil von SpanDraft.

Details und numerische Verträge stehen in [SOLVER.md](SOLVER.md).

Der Solver ist für den aktuellen Scope unabhängig gegen analytische Lösungen,
IndeterminateBeam und PyCBA validiert. Details stehen in
[VALIDATION.md](VALIDATION.md).

## 6. Desktop und Projektmodell

Der Desktop verwendet Avalonia und ist Windows-first ausgelegt. Entwicklung
unter macOS und Linux bleibt möglich; offizielle Builds für diese Plattformen
sind für die erste Veröffentlichung nicht erforderlich.

Der Editor besitzt einen einzigen committed Projektzustand. Dazu gehören die
fachlichen Eingaben sowie persistierbare Darstellungsdaten wie manuelle
Annotation-Offsets.

Temporäre Eingaben, Hover, Vorschauen und berechnete Ergebnisse gehören nicht
zum gespeicherten Projektzustand.

Implementiert sind:

- transaktionales Project Setup für Werkstoff, Querschnitt und Biegeachse
- eingebaute Materialien und lokale Benutzerbibliotheken für Materialien
  und parametrische beziehungsweise manuelle Querschnittspresets
- direkter Balkeneditor
- Platzieren und Bearbeiten aller aktuellen Lager- und Lasttypen
- Undo/Redo
- versionierte, eigenständige `.spandraft`-Projektdateien
- V1-Lesekompatibilität und aktuelles V2-Schreibformat
- Savepoints und Dirty-State
- private Crash-Recovery
- globale Einheitenprofile und benutzerdefinierte Einheitenkombinationen
- einheitliche numerische Eingabe- und Ergebnisdarstellung

Projektdateien enthalten vollständige Snapshots von Werkstoff, Querschnitt und
ausgewählter Biegeachse. Sie bleiben von lokalen Bibliotheken, Presets oder
Katalogen unabhängig; das Löschen oder Ändern eines Bibliothekseintrags darf
bestehende Projekte nicht verändern.

UI-Texte werden über lokalisierbare Ressourcen verwaltet. Aktuell sind Englisch
als Default und Deutsch vorhanden; Eingabe und Formatierung folgen der aktuellen
UI-Kultur. Eine Laufzeit-Sprachumschaltung ist noch nicht implementiert. Öffentliche
Dokumentation bleibt englisch, interne Fachdokumentation zunächst deutsch.

Hintergründe zum Editorzustand stehen in [UI.md](UI.md), der Projektdateivertrag in
[PROJECT_FORMAT.md](PROJECT_FORMAT.md).

## 7. Architektur

Die Schichten bleiben klein und getrennt. Direkte Projektreferenzen:

| Schicht | Referenziert |
| --- | --- |
| Core | keine andere Schicht |
| Solver | Core |
| Engineering | Core, Solver |
| Analysis | Core, Solver, Engineering |
| Desktop | Core, Analysis |
| Reporting | derzeit Core; Analysis erst bei Implementierung |

Analysis orchestriert Solver und Engineering. Desktop nutzt diesen
Anwendungseinstieg für Berechnungen.

Verantwortlichkeiten:

- **`SpanDraft.Core`:** Fachmodell, Einheiten, Werkstoffe, Querschnitte, Lager, Lasten und
  Modellvalidierung. Unabhängig von UI, Numerikbibliotheken, Persistenz und Reporting.

- **`SpanDraft.Solver`:** Mechanik und numerische Lösung. Keine Desktop-Abhängigkeit.

- **`SpanDraft.Engineering`:** Fachliches Postprocessing einer vorhandenen BeamSolution.

- **`SpanDraft.Analysis`:** Anwendungseinstieg, der Solver und Engineering zu einem strukturierten
  Success-/Failure-Ergebnis verbindet.

- **`SpanDraft.Desktop`:** UI, Interaktion, Projektzustand, Persistenz, lokale Bibliotheken
  und Darstellung. Keine eigenen mechanischen Formeln.

- **`SpanDraft.Reporting`:** reserviert für spätere Reports und Exporte.

Lokale Material- und Querschnittsbibliotheken sind Anwendungsdaten des Desktops.
Sie erzeugen Core-Domainobjekte, werden aber selbst nicht Teil von Core und
erzeugen keine dauerhafte Referenz aus einem Projekt auf einen Bibliothekseintrag.

## 8. Bewusste Nicht-Ziele

SpanDraft soll kein allgemeines FEM-System werden.

Nicht zum aktuellen Produktziel gehören:

- allgemeine 2D-Rahmenmodelle
- 3D-FEM
- Flächen- oder Volumenelemente
- Kontakt
- nichtlineare Materialmodelle
- plastische FEM
- Dynamik
- Knicken und Beulen
- Ermüdungsnachweise
- vollständige normbasierte Bemessung
- automatische Profiloptimierung
- Import eines FreeCAD-Profilkatalogs

Neue Funktionen sollen nur aufgenommen werden, wenn sie typische
Balkenberechnungen im Maschinenbau schneller, intuitiver, transparenter oder
zuverlässiger machen.

## 9. Nächste Produktziele

Für den ersten nutzbaren Release steht die Arbeit im Programm selbst im
Vordergrund.

Als nächster Ausbauschritt folgt das verbleibende App-Shell-Polishing mit
sinnvollen Menüfunktionen.

Ein eingebauter importierter Normprofilkatalog ist dafür nicht erforderlich.
Querschnittsbibliotheken dienen zunächst der Wiederverwendung eigener
parametrischer Definitionen und manueller Tabellenkennwerte.

PDF-, XLSX- und Druckexport sind für den ersten Release keine Voraussetzung.
Reports sollen später Eingaben, Ergebnisse und Rechenannahmen nachvollziehbar
dokumentieren.

Danach mögliche Erweiterungen:

- Tabellenansicht
- Zoom/Pan
- weitere Streckenlastformen
- Eigengewicht
- zusätzliche Sprachen
- Timoshenko-Balkentheorie
- weitere gezielte Engineering-Auswertungen

## 10. Transparenz und Qualität

SpanDraft soll Ergebnisse nicht als Blackbox präsentieren.

Nachvollziehbar bleiben sollen insbesondere:

- verwendetes Rechenmodell
- Eingabewerte
- Randbedingungen
- Vorzeichenkonventionen
- maßgebende Ergebnisse
- Grenzen der Berechnung

Der numerische Kern wird automatisiert und unabhängig validiert. Änderungen an
Solververträgen benötigen entsprechende Tests und eine erneute fachliche Prüfung.
