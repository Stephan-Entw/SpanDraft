# SpanDraft – Konzept

> Status: Initiales Projektkonzept  
> Stand: 03.10.2026  
> Dieses Dokument beschreibt den geplanten Scope, die technische Architektur und die Grundprinzipien von SpanDraft. Es ist bewusst als interne Entwicklungsgrundlage auf Deutsch gehalten und wird mit dem Projekt weiterentwickelt.

## 1. Projektidee

SpanDraft ist eine Open-Source-Desktopanwendung zur intuitiven Berechnung gerader Balken und Träger im Maschinenbau.

Das Programm soll die Lücke zwischen klassischer Hand-/Excel-Rechnung und umfangreichen FEM-Systemen schließen. Der Anwender soll einen Balken ähnlich einer technischen Skizze aufbauen, Lager und Lasten direkt am Modell platzieren, Maße numerisch bearbeiten und die mechanischen Ergebnisse unmittelbar sehen.

Der Schwerpunkt liegt nicht auf möglichst allgemeiner FEM-Funktionalität, sondern auf einem schnellen, transparenten und gut nachvollziehbaren Workflow für typische Maschinenbauaufgaben.

## 2. Zielbild

Typischer Anwendungsfall:

- gerader Balken oder Träger,
- bekannte Länge,
- definierter Werkstoff,
- Standard- oder Benutzerquerschnitt,
- mehrere frei positionierbare Lager bzw. Fixierungen,
- mehrere frei positionierbare Lasten,
- direkte Berechnung der Lagerreaktionen, Schnittgrößen, Durchbiegung, Spannungen und eines einfachen Sicherheitsfaktors,
- nachvollziehbare grafische Darstellung,
- exportierbarer Berechnungsreport.

Beispiele für typische Bauteile:

- Rechteck- und Vierkantrohre,
- Vollprofile,
- Rundrohre,
- einfache Maschinentraversen,
- einzelne Rahmenträger,
- Aluminium-Systemprofile, z. B. ITEM-artige Profile mit hinterlegten Herstellerkennwerten.

## 3. Produktprinzip: Sketch-first

Die grafische Darstellung ist nicht nur eine Ergebnisgrafik, sondern der primäre Editor des Modells.

Der Anwender soll:

- einen Balken grafisch anlegen,
- seine Länge und relevante Abstände direkt bemaßen,
- Lager per Drag & Drop platzieren und verschieben,
- Lasten per Drag & Drop platzieren und verschieben,
- Maßwerte und Positionen direkt numerisch editieren,
- Elemente anklicken und deren Eigenschaften bearbeiten,
- dieselben Daten zusätzlich in einer tabellarischen Ansicht sehen und bearbeiten.

Grafik und Tabelle stellen immer dasselbe Modell dar. Änderungen in einer Ansicht müssen unmittelbar in der anderen erscheinen.

Die Bedienung soll sich eher wie eine kleine technische Skizzen-/CAD-Anwendung anfühlen als wie ein Formularrechner.

## 4. Scope der ersten Version

### 4.1 Geometrie

V1 betrachtet einen einzelnen geraden Balken entlang einer Achse.

Geplant sind:

- frei definierbare Balkenlänge,
- beliebig positionierbare Lager innerhalb des Balkens,
- Überhänge vor bzw. hinter Lagern,
- automatisch erzeugte Rechenknoten an relevanten Positionen,
- zunächst konstanter Querschnitt und Werkstoff über die gesamte Balkenlänge.

### 4.2 Lagerungen

Mindestens folgende Lagerarten:

- Einspannung,
- Festlager,
- Loslager.

Der Solver soll drei Freiheitsgrade pro Knoten vorsehen:

- axiale Verschiebung (u),
- transversale Verschiebung (w),
- Rotation (	heta).

Damit lassen sich die Lager physikalisch sauber unterscheiden:

- Einspannung: (u = 0,; w = 0,; 	heta = 0)
- Festlager: (u = 0,; w = 0)
- Loslager: (w = 0)

Diese Modellierung hält die Architektur außerdem für spätere axiale Lastfälle offen, ohne bereits einen allgemeinen 2D-Rahmensolver zu bauen.

### 4.3 Lasten

Für V1 vorgesehen:

- Einzelkraft,
- Einzelmoment,
- konstante Streckenlast,
- frei editierbare Positionen,
- mehrere Lasten gleichzeitig.

Später mögliche Erweiterungen:

- linear veränderliche bzw. trapezförmige Streckenlast,
- Eigengewicht,
- Lastgruppen bzw. Lastfälle,
- weitere Lasttypen bei tatsächlichem Bedarf.

### 4.4 Querschnitte

Für V1 sinnvoll:

- Rechteck,
- Vollquadrat,
- Rechteckrohr / Vierkantrohr,
- Rundmaterial,
- Rohr,
- benutzerdefinierte Querschnittswerte.

Für komplexe Maschinenbauprofile, insbesondere Aluminium-Systemprofile, sollen reale Herstellerkennwerte wie Flächenträgheitsmoment und Widerstandsmoment hinterlegt werden können. Solche Profile sollen nicht aus einer vereinfachten Außenkontur angenähert werden.

### 4.5 Werkstoffe

Ein Werkstoff enthält mindestens:

- Name,
- Elastizitätsmodul (E),
- Streckgrenze bzw. zulässige Vergleichsgröße für die einfache Sicherheitsbewertung.

Weitere Materialkennwerte können später ergänzt werden, wenn sie für zusätzliche Berechnungsmodelle benötigt werden.

### 4.6 Ergebnisse

V1 soll mindestens liefern:

- Lager- bzw. Reaktionskräfte,
- Querkraftverlauf,
- Biegemomentverlauf,
- Verformungs-/Durchbiegungslinie,
- maximale Durchbiegung,
- maximale Biegespannung,
- Sicherheitsfaktor bezogen auf die hinterlegte Streckgrenze.

Der Sicherheitsfaktor der ersten Version ist eine einfache elastische Festigkeitsbewertung. Er ist kein vollständiger normativer Festigkeitsnachweis.

## 5. Rechenmodell

SpanDraft verwendet einen eigenen, kleinen und transparenten Solver für die 1D-Balkenmechanik.

### 5.1 Grundansatz

Für die erste Version ist die Euler-Bernoulli-Balkentheorie vorgesehen.

Der Balken wird intern automatisch in Elemente unterteilt. Relevante Positionen wie:

- Balkenenden,
- Lager,
- Einzelkräfte,
- Einzelmomente,
- Grenzen von Streckenlasten

erzeugen Rechenknoten.

Die Berechnung erfolgt über das Steifigkeitsverfahren / eine kleine Finite-Elemente-Formulierung.

Grundgleichung:

[
K cdot q = F
]

Dabei werden Elementsteifigkeitsmatrizen zu einer globalen Steifigkeitsmatrix assembliert, Randbedingungen angewendet und anschließend Verschiebungen sowie Reaktionen bestimmt.

### 5.2 Warum ein eigener Solver?

Der Solver ist klein genug, um ihn projektintern verständlich, testbar und vollständig nachvollziehbar zu halten.

SpanDraft soll deshalb nicht nur eine grafische Oberfläche um eine allgemeine FEM-Bibliothek sein.

Externe Bibliotheken werden dort eingesetzt, wo sie generische Numerik lösen, nicht dort, wo die eigentliche technische Mechanik des Programms definiert wird.

### 5.3 Numerische Basis

Für Matrix- und Vektoroperationen sowie lineare Gleichungssysteme ist Math.NET Numerics vorgesehen.

Aufgabentrennung:

- SpanDraft: mechanisches Modell, Elemente, Lasten, Lager, Assembly, Ergebnisinterpretation,
- Math.NET Numerics: numerische lineare Algebra.

### 5.4 Spätere Erweiterung: Timoshenko

Timoshenko-Balkentheorie ist nicht für V1 vorgesehen.

Die Architektur soll jedoch so gestaltet werden, dass später ein alternatives Balkenelement ergänzt werden kann. Das ist insbesondere für kurze bzw. hohe Träger interessant, bei denen Schubverformungen nicht mehr vernachlässigbar sind.

## 6. Bewusste Nicht-Ziele

SpanDraft soll in absehbarer Zeit kein allgemeines FEM-System werden.

Nicht Teil von V1:

- 3D-FEM,
- Flächen- oder Volumenelemente,
- allgemeine 2D-Rahmen aus mehreren frei verbundenen Stäben,
- Kontaktprobleme,
- nichtlineare Materialmodelle,
- plastische FEM,
- dynamische Berechnung,
- Knicken und Stabilitätsnachweise,
- Ermüdungsnachweise,
- vollständige normbasierte Bemessung,
- automatische Optimierung von Profilen.

Diese Punkte können nur dann später aufgenommen werden, wenn sie zur Produktidee passen und den einfachen Workflow nicht zerstören.

## 7. Benutzeroberfläche

### 7.1 Hauptarbeitsbereich

Die Hauptansicht soll den Balken als technische Skizze darstellen.

Wichtige Interaktionen:

- Auswahl per Klick,
- Drag & Drop für Lager und Lasten,
- direkte Bearbeitung von Bemaßungen,
- numerische Eingabe für exakte Positionen,
- Zoom und Pan bei langen Modellen,
- unmittelbare Aktualisierung der Berechnung nach Änderungen,
- klare visuelle Rückmeldung bei ungültigen oder unvollständigen Modellen.

### 7.2 Eigenschaften

Ausgewählte Objekte sollen über eine kompakte Eigenschaftenansicht bearbeitet werden können.

Beispiele:

- Balken: Länge, Material, Querschnitt,
- Lager: Typ, Position,
- Kraft: Betrag, Richtung, Position,
- Moment: Betrag, Position,
- Streckenlast: Betrag und Bereich.

### 7.3 Tabellenansicht

Die Tabelle ist eine ergänzende Ansicht, nicht der primäre Workflow.

Sie soll:

- alle Lager und Lasten übersichtlich auflisten,
- Werte direkt editierbar machen,
- mit der grafischen Auswahl synchronisiert sein,
- bei komplexeren Modellen schnelle numerische Änderungen ermöglichen.

### 7.4 Ergebnisdarstellung

Geplant sind synchronisierte Diagramme für:

- Querkraft,
- Biegemoment,
- Durchbiegung.

Zusätzlich sollen wichtige Maximalwerte und Lagerreaktionen kompakt dargestellt werden.

## 8. Reports und Export

### 8.1 PDF

SpanDraft soll einen PDF-Berechnungsreport erzeugen können.

Der Report soll mindestens enthalten:

- Projektdaten,
- grafische Darstellung des Balkens,
- Werkstoff und Querschnitt,
- Lager und Lasten,
- relevante Reaktionskräfte,
- Ergebnisdiagramme,
- Maximalwerte,
- Spannungs- und Sicherheitsbewertung,
- verwendete Berechnungsannahmen.

Der PDF-Report soll druckbar sein.

Als mögliche .NET-Bibliothek ist PDFsharp/MigraDoc vorgesehen. Die konkrete Reporting-Bibliothek wird erst bei Umsetzung des Reporting-Moduls endgültig festgelegt.

### 8.2 Excel

Berechnungs- und Ergebnisdaten sollen als XLSX exportiert werden können.

Excel selbst muss nicht automatisiert oder über COM gesteuert werden.

Als mögliche Bibliothek ist ClosedXML vorgesehen. Auch diese Abhängigkeit wird erst bei Umsetzung des Exportmoduls endgültig festgelegt.

## 9. Projektdateien

SpanDraft soll Modelle lokal speichern und wieder öffnen können.

Das Dateiformat ist noch nicht endgültig festgelegt. Anforderungen:

- versionierbar,
- robust gegenüber zukünftigen Erweiterungen,
- möglichst gut nachvollziehbar,
- keine Abhängigkeit von Cloud-Diensten.

## 10. Internationalisierung

SpanDraft soll langfristig eine internationale Oberfläche unterstützen.

Daher sollen UI-Texte von Beginn an nicht fest im Code verteilt werden, sondern über eine geeignete Ressourcen-/Lokalisierungsstruktur verwaltet werden.

Die konkrete Liste der unterstützten Sprachen ist noch nicht festgelegt.

Interne Konzept- und Entwicklungsdokumente können zunächst auf Deutsch geführt werden. README und öffentliche Anwenderdokumentation sollen später primär auf Englisch verfügbar sein.

## 11. Plattformstrategie

SpanDraft wird Windows-first entwickelt.

Ziel der ersten veröffentlichten Version ist Windows.

Die Architektur soll macOS und Linux jedoch nicht unnötig blockieren. Insbesondere soll der Entwicklungsworkflow auf macOS möglich bleiben.

Daraus folgt die Wahl eines plattformübergreifenden .NET-UI-Stacks anstelle einer ausschließlich Windows-spezifischen UI-Technologie.

Eine offizielle macOS- oder Linux-Version ist für V1 nicht erforderlich.

## 12. Technologiestack

Geplanter Stack:

- Sprache: C#
- Runtime/Framework: .NET 10
- Desktop-UI: Avalonia
- Numerik: Math.NET Numerics
- Tests: .NET-Testframework, konkrete Auswahl bei Projektsetup
- PDF: voraussichtlich PDFsharp/MigraDoc
- XLSX: voraussichtlich ClosedXML
- Versionsverwaltung: Git / GitHub

## 13. Architektur

Die Fachlogik soll strikt von UI und Reporting getrennt bleiben.

Geplante Solution-Struktur:

```text
SpanDraft
│
├── SpanDraft.Core
│   ├── Beam
│   ├── Material
│   ├── Section
│   ├── Support
│   ├── Load
│   └── Units
│
├── SpanDraft.Solver
│   ├── Elements
│   ├── Mesh
│   ├── Assembly
│   ├── BoundaryConditions
│   ├── Reactions
│   ├── InternalForces
│   └── StressEvaluation
│
├── SpanDraft.Desktop
│   └── Avalonia UI
│
├── SpanDraft.Reporting
│   ├── PDF
│   └── XLSX
│
└── SpanDraft.Tests
```

### 13.1 SpanDraft.Core

Enthält das fachliche Datenmodell, jedoch keine UI-Abhängigkeit und möglichst keine Abhängigkeit von konkreten Solver-Implementierungen.

### 13.2 SpanDraft.Solver

Enthält die technische Mechanik und numerische Berechnung.

Der Solver soll unabhängig von Avalonia verwendbar und vollständig automatisiert testbar sein.

### 13.3 SpanDraft.Desktop

Enthält ausschließlich Desktop-UI, Interaktionslogik und Darstellung.

Die UI soll keine Berechnungsformeln enthalten.

### 13.4 SpanDraft.Reporting

Erzeugt Reports und Exporte aus dem fachlichen Modell und den Solver-Ergebnissen.

### 13.5 SpanDraft.Tests

Enthält analytische Referenzfälle, Regressionstests und Validierung des Solvers.

## 14. Qualität und Validierung

Da SpanDraft ein Engineering-Werkzeug ist, hat die Nachvollziehbarkeit des Rechenkerns hohe Priorität.

Jede zentrale Solver-Funktion soll durch automatisierte Tests abgesichert werden.

Referenzfälle sollen unter anderem bekannte analytische Lösungen enthalten, z. B.:

- einfach gelagerter Balken mit mittiger Einzelkraft,
- Kragarm mit Endlast,
- Kragarm mit Streckenlast,
- einfach gelagerter Balken mit Streckenlast,
- Balken mit Überhang,
- statisch unbestimmter Durchlaufträger.

Für geeignete Fälle können Ergebnisse zusätzlich gegen unabhängige Open-Source-Solver wie FEALiTE2D oder IndeterminateBeam verglichen werden. Diese dienen als Entwicklungs- und Validierungsreferenz, nicht als Runtime-Abhängigkeit.

Toleranzen für numerische Vergleiche sollen explizit definiert werden.

## 15. Transparenz der Berechnung

SpanDraft soll Ergebnisse nicht als Blackbox präsentieren.

Langfristig soll nachvollziehbar sein:

- welches Rechenmodell verwendet wurde,
- welche Eingabewerte in die Berechnung eingeflossen sind,
- welche Randbedingungen gelten,
- welche Maximalwerte gefunden wurden,
- welche Annahmen und Grenzen der Berechnung bestehen.

Der Report soll diese Informationen soweit sinnvoll dokumentieren.

## 16. Lizenz

SpanDraft wird unter der GNU General Public License v3.0 (GPL-3.0) veröffentlicht.

Kommerzielle Nutzung ist zulässig. Bei Weitergabe abgeleiteter GPL-Versionen gelten die Copyleft-Bedingungen der GPL.

Projektname und Branding sind getrennt von der Code-Lizenz zu betrachten. Eine konkrete Branding-/Trademark-Regelung kann vor einer breiteren Veröffentlichung ergänzt werden.

## 17. Grobe Umsetzungsreihenfolge

### Phase 0 – Projektfundament

- .NET-Solution anlegen,
- Projektstruktur erstellen,
- Avalonia-Grundprojekt einrichten,
- Testprojekt einrichten,
- CI-Grundlage vorbereiten.

### Phase 1 – Fachmodell und Solver

- Einheiten und Grundtypen,
- Balkenmodell,
- Querschnitte,
- Materialien,
- Lager,
- Lasten,
- automatische Knotenerzeugung,
- Euler-Bernoulli-Element,
- globale Matrixassemblierung,
- Randbedingungen,
- Lösung von (Kq = F),
- Reaktionen,
- Schnittgrößen,
- Durchbiegung,
- Spannungen und Sicherheitsfaktor,
- analytische Referenztests.

### Phase 2 – Interaktiver Editor

- Balkendarstellung,
- Auswahlmodell,
- Lager hinzufügen/verschieben,
- Lasten hinzufügen/verschieben,
- direkte Bemaßung,
- Eigenschaftenansicht,
- Tabellenansicht,
- Undo/Redo frühzeitig berücksichtigen.

### Phase 3 – Ergebnisse

- Reaktionskräfte,
- Querkraftdiagramm,
- Momentendiagramm,
- Durchbiegungslinie,
- Ergebniskennwerte,
- grafische Synchronisation zwischen Modell und Ergebnissen.

### Phase 4 – Persistenz und Ausgabe

- Projekt speichern/laden,
- PDF-Report,
- XLSX-Export,
- Druckworkflow für Reports.

### Phase 5 – Ausbau

Mögliche spätere Funktionen:

- Timoshenko-Balken,
- weitere Streckenlastformen,
- Eigengewicht,
- Profilbibliothek,
- importierbare Herstellerprofile,
- weitere Werkstoffdaten,
- zusätzliche Sprachen,
- macOS/Linux-Builds,
- zusätzliche Festigkeits- oder Gebrauchstauglichkeitsprüfungen.

## 18. Leitlinien für Scope-Entscheidungen

Neue Funktionen sollen nur aufgenommen werden, wenn sie mindestens eines dieser Ziele unterstützen:

1. typische Balkenberechnungen im Maschinenbau schneller machen,
2. die Modellierung intuitiver machen,
3. die Berechnung transparenter oder zuverlässiger machen,
4. wiederkehrende manuelle Excel-/Handrechnungen sinnvoll ersetzen.

Funktionen, die SpanDraft in Richtung eines allgemeinen FEM- oder Bauwerksbemessungssystems ziehen, sollen kritisch hinterfragt werden.

Die Einfachheit des Workflows ist ein Kernbestandteil des Produkts.
