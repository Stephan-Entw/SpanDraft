# SpanDraft – Konzept

Stand: 27.10.2026

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

- einen geraden Balken mit Länge, Werkstoff und Querschnitt definieren
- Lager und Lasten direkt platzieren
- Positionen und Werte exakt numerisch bearbeiten
- Lager und Lasten durch Anklicken und Ziehen bearbeiten
- Ergebnisse unmittelbar nach bestätigten Änderungen sehen

Eine spätere Tabellenansicht darf denselben Projektzustand ergänzend darstellen,
aber nicht als zweites Modell führen.

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
- parametrische und manuelle Querschnitte mit wählbarer Biegeachse
- Werkstoffe mit den für Berechnung und Bewertung benötigten Kennwerten

Eigene Werkstoffe und Querschnitte können lokal gespeichert und wiederverwendet
werden. Die vollständigen Formen und fachlichen Verträge stehen in
[DOMAIN.md](DOMAIN.md).

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
- Sicherheitsfaktor gegenüber dem hinterlegten Festigkeitskennwert (Re oder Rp0,2)

Der Sicherheitsfaktor ist eine einfache elastische Bewertung und kein normativer
Festigkeitsnachweis. Details stehen in [ENGINEERING.md](ENGINEERING.md).

Im Desktop werden kompakte Ergebniskennwerte und Lagerreaktionen sowie
Live-Diagramme für Durchbiegung, Querkraft und Biegemoment dargestellt.
Berichtsvorschau, PDF-Ausgabe und Windows-Druck dokumentieren bestätigte
Projekteingaben, vorhandene Ergebnisse und Rechenannahmen. Die Ausgabe verändert
keinen Projektinhalt; die State-Rationale steht in [UI.md](UI.md#berichte-als-eingefrorene-ausgabe).

## 5. Rechenmodell

SpanDraft verwendet einen eigenen transparenten Euler-Bernoulli-Balkensolver
mit dem direkten Steifigkeitsverfahren. Math.NET Numerics übernimmt ausschließlich
generische lineare Algebra. Details stehen in [SOLVER.md](SOLVER.md).

Der Solver ist unabhängig gegen analytische Lösungen, IndeterminateBeam und PyCBA
validiert. Die Evidenz steht in [VALIDATION.md](VALIDATION.md).

## 6. Desktop und Projektmodell

Der Desktop verwendet Avalonia und ist Windows-first ausgelegt. Entwicklung
unter macOS und Linux bleibt möglich; offizielle Release-Pakete sind derzeit
auf Windows x64 ausgerichtet. Release- und Packaging-Abläufe stehen in
[RELEASING.md](RELEASING.md).

Bestätigte Änderungen bilden den Projektzustand. Offene Eingaben und Vorschauen
verändern das Projekt noch nicht. Projekte werden als eigenständige
`.spandraft`-Dateien gespeichert und bleiben von lokalen Bibliotheken unabhängig.

Implementiert sind:

- Project Setup für Balkenlänge, Werkstoff, Querschnitt und Biegeachse
- eingebaute Werkstoffe sowie lokale Werkstoff- und Querschnittsbibliotheken
- direkter Balkeneditor
- Platzieren und Bearbeiten von Lagern und Lasten
- Undo/Redo
- eigenständige Projektdateien
- Projektangaben mit Berechnungstitel, Ersteller und optionalen Projekt- und Referenzangaben
- Crash-Wiederherstellung
- Wiederherstellung bestätigter Werte noch nicht erstellter Projekte
- konfigurierbare Einheiten
- einheitliche numerische Eingabe- und Ergebnisdarstellung

Die Oberfläche ist auf Deutsch und Englisch verfügbar. Sprache, regionales
Zahlenformat und technische Einheiten können unabhängig eingestellt werden.

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
| Desktop | Core, Analysis, Reporting |
| Reporting | keine andere Schicht |

Fachmodell, Solver, Engineering, Analysis und Desktop haben klar getrennte
Verantwortlichkeiten. Core bleibt unabhängig von UI, Numerikbibliotheken,
Persistenz und Reporting. Analysis verbindet Berechnung und Auswertung für den
Desktop; dieser enthält keine eigenen mechanischen Formeln.

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

Neue Funktionen sollen nur aufgenommen werden, wenn sie typische
Balkenberechnungen im Maschinenbau schneller, intuitiver, transparenter oder
zuverlässiger machen.

## 9. Nächste Produktziele

Mögliche spätere Erweiterungen, ohne festgelegte Reihenfolge:

- Zoom/Pan im Balkeneditor
- weitere Streckenlastformen
- Eigengewicht
- zusätzliche Sprachen

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
