# SpanDraft – verbindliche UI-Spezifikation

Stand: 03.10.2026. Diese Spezifikation ergänzt [KONZEPT.md](KONZEPT.md).
Die Abschnitte zum nächsten Meilenstein sind verbindliche Interaktionsregeln,
aber noch keine implementierten Funktionen.

## 1. Design und aktueller Umfang

SpanDraft ist ein helles, kompaktes Engineering-Desktopwerkzeug mit Avalonia
Fluent. Ruhige weiße/hellgraue Flächen, präzise Linien und dezente Separatoren
stellen die technische Zeichnung in den Mittelpunkt. Es gibt keinen rechten
Inspector, keine vertikale Toolbar, keine KPI-Karten und keine freie 2D-CAD-Geometrie.
Das Theme ist ausdrücklich Light; System/Dark folgt später.

Eine MainWindow-Instanz (1250×800, mindestens 1100×650 DIPs) hostet Menü und
Hauptansicht. Die Toolbar bleibt auch bei der Mindestgröße in einer horizontalen
Zeile. Kein Routing- oder Navigationsframework.

Implementiert: Setup, Setup/Editor-Wechsel, Menü, Toolbar, Projektinformation,
Balkengrafik, Längeneingabe, Analysis-Anbindung und Ergebnis-/Statusleiste.

## 2. Startseite und Setup-Modi

Jeder App-Start zeigt ProjectSetupView, ohne Projekt und ohne Analysis-Aufruf.
Zwei kompakte Auswahlbereiche zeigen Querschnitt mit A/I/W und Werkstoff mit E/Re.

Initial ausgewählt:

- Vierkantrohr 100 × 100 × 5 mm: RectangularHollowSection mit idealisierten
  scharfen Kanten. Eine geometrische V1-Vorlage, kein EN-Norm- oder
  Herstellerprofil. A/I/W werden aus dem bestehenden Core-Modell angezeigt,
  nicht aus Hersteller- oder Tabellenwerten.
- S235JR: vorläufige V1-Materialvorlage mit E = 210 GPa und Re = 235 MPa.
  Keine vollständige normative Werkstoffdatenbank oder zusätzliche normative Aussage.

Create-Modus: „Projekt erstellen“ erzeugt das committed EditorDocument mit 1000 mm,
gewählter Section/Material und einem BeamModel ohne Lager/Lasten. Es folgt genau
ein Analysis-Aufruf und der Wechsel zum Editor.

Edit-Modus: „Ändern“ verwendet dieselbe Setup-View mit aktueller Auswahl.
„Übernehmen“ committed Section/Material gemeinsam, erhält die Länge, analysiert
einmal und kehrt zum Editor zurück. „Abbrechen“ verwirft ausschließlich die
temporäre Auswahl, erhält Dokument und Editor und löst keine Analysis aus.
Künftig vorhandene Lager/Lasten müssen bei dieser Bearbeitung ebenfalls erhalten
bleiben; deren Entwurfsarchitektur wird jetzt noch nicht eingeführt.

## 3. Editorlayout und Menü

Von oben nach unten:

1. Datei | Bearbeiten | Ansicht | Hilfe
2. horizontale Engineering-Toolbar
3. Projektinformation: Querschnitt · Werkstoff und „Ändern“
4. dominanter BeamEditorSurface
5. kompakte Ergebnis-/Statusleiste

Toolbar-Reihenfolge:

Punktkraft, Moment, Streckenlast | Einspannung, Festlager, Loslager

Die Toolbar enthält kein Längenfeld. Alle sechs Platzierungswerkzeuge sind deaktiviert.
Es gibt weder einen Auswahl- noch einen Entfernen-Button. Neutrale Interaktion
wird künftig automatisch der Auswahlzustand.

Menü-Platzhalter (einschließlich Neues Projekt, Öffnen, Speichern,
Rückgängig/Wiederholen, Einstellungen, Zoom, Ergebnisse und Über SpanDraft)
sind deaktiviert. Keine anklickbaren No-op-Aktionen.

Einheiten gehören später in „Bearbeiten → Einstellungen…“ (mm/m, N/kN, Nm/kNm,
N/m/kN/m, Pa/MPa, Dezimaldarstellung), nicht prominent in den Editor.
Aktuell gibt es kein Settings-System und keine Persistenz.

## 4. Desktop-Zustand und Rendering

MainWindowViewModel hält den Modus ProjectSetup/Editor und die Navigation.
ProjectSetupViewModel hält eine temporäre Auswahl. EditorViewModel besitzt
das einzige committed EditorDocument mit Length, Material und Section.
Core-Objekte bleiben immutable. Kein zweites konkurrierendes Projektmodell.

ObservableObject und ActionCommand bilden die kleine lokale ViewModel-Basis.
Es gibt keine externe MVVM-Bibliothek, DI, Event Bus oder allgemeine Draft-Architektur.
Code-behind steuert ausschließlich Fokus, Tastatur und visuelle Overlaypositionen.

BeamEditorSurface enthält BeamCanvas plus interaktive Overlay-Controls.
BeamCanvas zeichnet über DrawingContext Balken, feinere Maßlinie, Pfeile und
Hilfslinien. Die Maßzahl ist ein normales Avalonia-Control, kein gezeichneter
Text mit eigenem Hit-Testing.

Der physikalische Balken verläuft von x = 0 bis L und existiert immer.
Es gibt kein Balkenzeichenwerkzeug. Die Pixellänge passt sich an den Viewport an.

BeamViewport definiert eine gemeinsame reversible Abbildung:

```text
screenX = left + (beamX / L) × (right − left)
beamX   = ((screenX − left) / (right − left)) × L
```

Beam-x und L verwenden Meter, screenX verwendet DIPs. Seitliche Ränder sind
normalerweise 72 DIPs. Resize berechnet die gemeinsame Zeichen-/Overlaygeometrie
neu. Die Maßzahl bleibt über der Mitte des Balkens. Die Abbildung wird später
für Hover, Snapping, Hit-Testing, Lager und Lasten wiederverwendet. Zoom/Pan fehlt
bewusst.

## 5. Transaktionale Längenbearbeitung

Die klickbare Maßzahl ist die einzige sichtbare Längeneingabe im Editor und liest
die committed Länge. DimensionLength hält den einzigen temporären Eingabepuffer. Unvollständiger Text wie „1,“ darf während
der Eingabe bestehen und erzeugt weder Core-Objekte noch Analysis-Aufrufe.

Die Maßzahl zeigt normal beispielsweise „1000 mm“. Ein Klick öffnet einen
Inline-Editor mit Zahl und statischer Einheit mm.

| Aktion | Verhalten |
| --- | --- |
| Enter, gültig | Commit, genau eine Analysis bei geänderter Länge, Eingabe schließen |
| Enter, ungültig | Dokument erhalten, keine Analysis, Inline-Fehler; Korrektur/Escape möglich |
| Escape | Eingabe verwerfen, committed Wert wiederherstellen, schließen |
| Focus Loss, gültig | Commit wie Enter |
| Focus Loss, ungültig | Letzten committed Wert wiederherstellen, ohne Analysis |
| Unveränderte Länge | Keine erneute Analysis |

Parsing-/Formattinglogik bleibt in UiNumbers. Nach Abschluss kann Focus Loss
nicht nochmals committen. Fehler erscheinen als markiertes Feld mit lokalisiertem
Hilfetext; die Maßzahl zeigt zusätzlich einen Inline-Fehlertext.

Parsing verwendet CurrentUICulture und numerische Float-Eingabe ohne
Tausenderseparatoren. Gültig sind endliche positive mm-Werte, die auch in SI
positiv darstellbar bleiben. Null, negative Werte, NaN, Infinity, Überlauf und
Unterlauf auf null werden abgewiesen.

Deutsche Dezimalzahlen wie „1000,5“ sind gültig. Ganze mm erscheinen ohne
angehängte Dezimalnullen. Manuelle Eingaben werden nicht auf ganze Millimeter
gerundet. Die Ressourcen folgen ebenfalls CurrentUICulture: Default Englisch,
Deutsch über Strings.de.resx; keine Laufzeit-Sprachumschaltung.

## 6. Analysis und Ergebnisleiste

Nach jeder committed Änderung wird ein neues immutable BeamModel erzeugt und
ausschließlich BeamAnalysis.Analyze(beam) aufgerufen. Desktop referenziert direkt
nur Core und Analysis. Es gibt keine eigenen Engineering-Formeln.

AnalysisPresentationState hält die UI-Klassifikation und gegebenenfalls das
ursprüngliche BeamAnalysisResult. Kennwerte werden aus dessen Engineering-Ergebnis
formatiert, nicht als zweites Rechenmodell gespeichert.

| Strukturiertes Ergebnis | Deutsche Darstellung |
| --- | --- |
| InvalidModel + MissingSupports | Berechnung nicht verfügbar · Lagerung fehlt |
| anderes InvalidModel | Berechnung nicht verfügbar · Modell unvollständig |
| UnstableModel | Berechnung nicht verfügbar · Lagerung nicht ausreichend |
| IllConditionedSystem / NumericalFailure | Berechnung nicht möglich |
| Success | Kennwerte aus dem vorhandenen Engineering-Ergebnis |

MissingSupports ist im aktuellen Editor ein normaler Zustand. Keine Exceptionbox,
kein Stacktrace und keine TechnicalMessage als Darstellung oder Klassifikation.

Die dauerhafte horizontale Ergebnisleiste kann |w|max in mm, |M|max in kNm,
σmax in MPa und S anzeigen. Ergebniskennwerte verwenden eine kompakte Darstellung
mit bis zu zwei Nachkommastellen; unendlicher SafetyFactor wird als ∞ dargestellt.
Dies verändert keine Ergebniswerte.

„Ergebnisse“ bleibt in diesem Meilenstein auch bei Success deaktiviert, weil die
Ergebnisansicht noch fehlt. Bei ihrer Einführung darf der Button ausschließlich
mit vorhandenem erfolgreichem BeamAnalysisResult aktiviert werden.

## 7. Nächster Meilenstein: verbindliche Placement-/Flyout-Interaktion

### 7.1 One-shot-Werkzeuge

Jedes Lager-/Lastwerkzeug endet nach einem bestätigten Objekt:

1. Werkzeugbutton anklicken.
2. Placement-Modus beginnen.
3. Cursor über Balken bewegen.
4. Halbtransparente Vorschau und aktuelle x-Koordinate anzeigen.
5. Auf Balken klicken.
6. Objektgebundenes Flyout öffnen; Werte prüfen/ändern.
7. OK committed das Objekt und löst eine Analysis aus.
8. Werkzeug endet; neutraler Auswahlzustand.

Es wird kein weiteres Objekt automatisch vorbereitet. Für ein zweites Festlager
muss „Festlager“ erneut gewählt werden.

### 7.2 Preview und Abbruch

Preview hat etwa 70–80 % Opazität, ist sichtbar uncommitted, gehört nicht zum
EditorDocument und löst keine Analysis aus. OK committed; Abbrechen verwirft die
Preview vollständig. Unbestätigte Flyout-Eingaben bleiben temporär.

### 7.3 Koordinate und Snap

Während Hover/Placement x in mm anzeigen. Mausposition auf den nächstgelegenen
ganzen Millimeter snappen, ohne Nachkommastellen. Beispiel: 207,4 mm → 207 mm.

Bei x = 0 und x = L gibt es eine größere magnetische Fangzone; nahe dem Ende
exakt den Endpunkt bevorzugen. Kein sichtbares 1-mm-Raster. Manuelle
Flyout-Eingaben dürfen Dezimalwerte wie 207,5 mm besitzen.

### 7.4 Objektgebundene Flyouts

Nach Platzierung oder Klick auf ein vorhandenes Objekt ein kleines gebundenes
Flyout/Popover öffnen. Kein permanenter rechter Inspector und kein großes
modales Dialogfenster.

Lager-Flyout: Typ (Einspannung/Festlager/Loslager), Position, Abbrechen, OK.
Bestehende Lager dürfen Position und Typ gemeinsam ändern, beispielsweise
Festlager bei 207 mm → Loslager bei 200 mm. Erst OK übernimmt beide Änderungen.

### 7.5 Punktkraft und Punktmoment

Punktkraft: Werkzeug → Preview → x klicken → Flyout mit Position und signed Kraft.
−2500 N zeigt einen Pfeil nach unten, +2500 N einen Pfeil nach oben.
Kein separates Richtung-Dropdown; das Vorzeichen bestimmt die Richtung.

Punktmoment: analog mit Position und signed Moment. Vorzeichen bestimmt die
Drehrichtung in Preview und committed Grafik. Gemäß Core ist positiv gegen den
Uhrzeigersinn.

### 7.6 Streckenlast

Werkzeug → Startpunkt klicken → Preview zwischen Start und aktueller Mausposition
→ Endpunkt klicken → Flyout mit Start, Ende und signed Intensität.
Erst OK committed die Last. Das Vorzeichen bestimmt die Richtung.

### 7.7 Löschen und Analysis-Zeitpunkt

Kein permanenter Löschen-Button in der Toolbar. Später Löschen über Objekt-Flyout
und/oder Delete-Taste bei Auswahl.

Analysis ausschließlich nach committed Änderung: OK im Flyout, Löschen,
gültige geänderte Balkenlänge oder Section/Material übernehmen.
Keine Analysis bei Hover, Preview, unvollständiger Texteingabe oder
unbestätigtem Flyout.

## 8. Spätere Ergebnisse-/Reportansicht

„Ergebnisse“ wechselt künftig zu einer eigenen Ansicht innerhalb derselben
MainWindow-Instanz. Geplant: Kennwerte, Reaktionen, Durchbiegung w(x),
Querkraft V(x), Biegemoment M(x), später PDF und XLSX.
Im Editor bleibt die kompakte Ergebnisleiste. Die Ergebnisansicht wird jetzt
nicht implementiert.

## 9. Bewusst offen

Noch nicht implementiert: Placement-State-Machine, Lager-/Lastplatzierung,
Preview, Snap-Engine, Objekt-Hit-Testing, Flyouts, Löschen, Undo/Redo, Zoom/Pan,
Tabellen/Diagramme, Ergebnisse-/Reportseite, PDF/XLSX, Speichern/Laden, Auto-Save,
Settings-Persistenz, Theme-Umschaltung, echte Profil-/Norm-/Herstellerbibliotheken,
Profilimport, eigene Materialien und Querschnittseditor.
Keine neuen Solverfunktionen oder Engineering-Nachweise.

## 10. Prüfung

Reine Desktop-State-/ViewModel-Tests laufen im vorhandenen Produkttestprojekt
ohne GUI-Initialisierung, Headless oder neue Testpakete. Der Analysis-Delegate
ermöglicht das Zählen und Prüfen der übergebenen BeamModels; produktiv ist allein
BeamAnalysis.Analyze angeschlossen.

Das Regression-Gate umfasst Restore/Build/Tests beider Solutions, unveränderte
eingefrorene Bereiche und den bestehenden SolverSourceSha256. GUI-Smoke-Checks
und deren tatsächliche Grenzen werden im Abschlussbericht separat dokumentiert.

### Historische Abnahme des funktionalen UI-Fundaments (03.10.2026)

- Beide Solutions restauriert und mit null Warnungen/Fehlern gebaut.
- 285 Produkttests bestanden: 247 bestehende und 38 neue Desktop-Testfälle.
- Alle 195 Validation-Tests bestanden; keine übersprungenen Tests.
- Core, Solver, Engineering, Analysis, Reporting, der gesamte Validation-Bereich
  einschließlich Cases/Golden References und docs/VALIDATION.md unverändert.
- SolverSourceSha256:
  `6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`.

Eine echte macOS-GUI wurde über ein temporäres App-Bundle des gebauten Desktops
in /private/tmp geprüft. Die 24 angeforderten Smoke-Check-Punkte wurden überprüft:
Start und Light-Theme, Setup samt vorausgewählten Vorlagen, Erstellung und
Editorlayout, einzeilige Toolbar, Projektinfo, Balken/Maßlinie/1000-mm-Anzeige,
beide Längenzugänge und deren Synchronisation, Resize durch native
Fenstervergrößerung, deutsche Dezimalwerte, ungültige Eingaben, MissingSupports,
deaktivierte Ergebnisse und Setup-Ändern/Abbrechen/Übernehmen mit erhaltener Länge.
Zusätzlich geprüft: Escape, ungültiges Enter mit sichtbarem Fehlerzustand sowie
gültiger und ungültiger Fokusverlust.

Nicht visuell geprüft: Windows-spezifische Darstellung, exakte Mindestfenstergröße
1100×650 und englisches Layout. Diese Prüfungen bleiben auf dem Zielsystem offen.
Die Culture-/Ressourcenlogik für Englisch und Deutsch ist automatisiert geprüft;
die Mindestgröße ist im Fenster festgelegt. Erfolgreiche Ergebnisdarstellung
wurde über reale Analysis-Ergebnisse in State-Tests geprüft, da das aktuelle
Editorprojekt ohne Lager keinen Success erzeugt.


## 11. Visual Design System

### Prinzipien und Struktur

SpanDraft ist ein ruhiges, präzises, kompaktes Engineering-Desktopwerkzeug:
Windows-first, hohe Informationsdichte und eine große technische Arbeitsfläche.
Kein Dashboard, Ribbon, dekoratives Raster, Gradient oder Schatten. Light bleibt
das einzige Theme; keine externe Schrift, UI-, Icon- oder MVVM-Bibliothek.

Vier Dateien unter `src/SpanDraft.Desktop/Styles/` bilden das gemeinsame System:

- `DesignTokens.axaml`: semantische Brushes, Schriftgrößen, Abstände, Radien,
  Borderstärken, Controlhöhen und wiederkehrende Thickness-Kombinationen.
- `Typography.axaml`: benannte Textklassen; die System-UI-Schrift wird geerbt.
- `Controls.axaml`: gemeinsames Button-Template, Varianten, Input-/Menüstile,
  Selection Surfaces und Verbindung der Canvas-/Icon-Controls zu den Tokens.
- `Icons.axaml`: ausschließlich zentrale skalierbare Vektorgeometrien.

App.axaml bindet Ressourcen und Styles nach dem bestehenden FluentTheme ein.
Statische Abstände verwenden StaticResource, semantische Brushes und
Controlwerte DynamicResource. Views enthalten keine lokalen Farben,
Schriftgrößen oder Buttonradien. Technische Zeichengeometrie und konkrete
Layoutbegrenzungen sind bewusst keine allgemeinen Design-Tokens.

### Farb-Tokens

| Token | Wert |
| --- | --- |
| AppBackground | #F5F7FA |
| Surface / SurfaceSubtle | #FFFFFF / #F8FAFC |
| SurfaceHover / SurfacePressed | #F1F4F8 / #E7EDF4 |
| CanvasBackground | #FEFEFF |
| Border / BorderStrong | #D9DFE7 / #C5CDD8 |
| TextPrimary / TextSecondary | #202631 / #5E6878 |
| TextMuted / TextDisabled | #697586 / #737E8C |
| Accent / AccentHover / AccentPressed | #2563EB / #1D4ED8 / #1E40AF |
| AccentSubtle / TextOnAccent | #EFF6FF / #FFFFFF |
| Error / ErrorSubtle | #B42318 / #FEF3F2 |
| BeamStroke / DimensionStroke | #374151 / #697586 |

Accent dient Primäraktion, Fokus und künftig aktiven Werkzeugen, nicht großen
Flächen oder Überschriften. TextPrimary auf Surface erreicht 15,18:1,
TextSecondary auf SurfaceSubtle 5,38:1, TextMuted auf Surface 4,68:1 und
TextOnAccent auf Accent 5,17:1. Disabled-Text auf Surface erreicht 4,12:1;
er bleibt bei voller Opazität sichtbar. Dezente Flächentrenner sind keine
alleinigen Interaktionsindikatoren: Fokus verwendet einen 2-DIP-Accentrahmen,
Fehler zusätzlich lokalisierten Text.

### Typografie und Größen

Alle Angaben sind logische DIPs, unabhängig von Bildschirm-Skalierung.

| Textklasse | Größe / Gewicht |
| --- | --- |
| pageTitle | 24 / Semibold |
| sectionTitle | 17 / Semibold |
| body | 13 / Normal |
| bodyStrong / technicalValue | 13 / Semibold |
| secondary / caption | 12 / Normal |
| Toolbar | 13 / Normal |

Es gibt keine feste FontFamily und keine eingebettete Fontdatei. Die Spacing-Skala
lautet 4, 8, 12, 16, 24, 32. Wiederkehrende Padding-/Margin-Kombinationen werden
zentral benannt. Radien: XS 4, S 6, M 8; normale Controls verwenden S.
Borders: normal 1, Fokus/Fehler 2. Fokusrahmen verändern das Contentlayout nicht.

| Höhentoken | DIPs |
| --- | --- |
| CompactControl | 30 |
| StandardControl | 34 |
| PrimaryAction | 38 |
| ToolbarButton | 36 |

### Buttons, Inputs und Zustände

- `primary`: Accent-Fläche, heller Semibold-Text, 38 DIPs hoch.
- `secondary`: weiße Fläche und dezente Kontur, 34 DIPs hoch.
- `ghost`: sekundäre Aktion ohne dauerhafte Fläche, 30 DIPs hoch.
- `toolbar`: flaches Icon plus Label, 36 DIPs hoch.
- `iconToolbar`: quadratische Variante gleicher Höhe, für spätere Nutzung.

Alle Varianten besitzen Hover-, Pressed-, Focused- und Disabled-Zustände.
Der gemeinsame Button-Fokusrahmen ersetzt den Standard-Fokusindikator;
Primärbuttons erhalten einen inneren hellen Fokusrahmen. Es gibt keine
Pressed-Skalierung. `.selected` und `ToggleButton:checked` bereiten die
Werkzeugauswahl visuell vor, ohne eine neue Interaktion zu implementieren.
Disabled hat Vorrang vor Hover/Pressed/Selected; Icons erben die Textfarbe.

TextBox und ComboBox teilen Höhe, Schriftgröße, Border, Radius und Fokusfarbe.
Fluent übernimmt weiterhin Textbearbeitung, Auswahl und Dropdown-Verhalten.
Die ComboBox erhält beim Fokus keine gefüllte blaue Fläche. `inlineDimension`
ist 30 DIPs hoch, rechtsbündig und Semibold; ihre Breite wächst mit dem Text bis
zur begrenzten Maximalbreite. Sehr lange Eingaben bleiben im Textfeld horizontal
zugänglich. Die Einheit mm steht separat. Fehler verwenden Error/ErrorSubtle,
einen 2-DIP-Rahmen und eine lokalisierte Meldung.

### Setup und Editor

Setup: maximal 1000 DIPs breit, horizontal zentriert, 32 DIPs Außenabstand,
24 DIPs zwischen zwei gleich breiten Selection Surfaces. Titel, Auswahl und
Kennwerte haben abgestufte Priorität. Symbole stehen links, Werte rechts an
einer gemeinsamen Achse; V1-Hinweise schließen beide Flächen unten ab.
Die bestehenden Kennwertformate bleiben erhalten. Erstellen/Übernehmen steht
rechts; Abbrechen ist im Edit-Modus eine sekundäre Aktion.

Die Menüstruktur bleibt desktoptypisch. Die einzeilige Toolbar beginnt mit
Punktkraft, Moment und Streckenlast; ein feiner Separator trennt die Lagergruppe.
Alle sechs Werkzeuge bleiben deaktiviert und besitzen lokalisierte
Accessibility-Namen. Bei 1100 DIPs Mindestbreite erfolgt kein Umbruch.
Die Projektinfo ist eine Textzeile mit BodyStrong und Ghost-Aktion „Ändern“.

Der Canvas verwendet CanvasBackground ohne Cardrahmen. BeamCanvas erhält
BeamBrush, DimensionBrush und GuideBrush als render-invalidierende
StyledProperties. Der Balken ist 5 DIPs stark, die Maßlinie 1 DIP, der vertikale
Abstand beträgt 40 DIPs. Die reversiblen x-Koordinatentransformationen bleiben
unverändert. Die einzige Längenbearbeitung sitzt als Control-Overlay mittig an
der Maßlinie. Anzeige und Inline-Eingabe teilen die vertikale Position;
Hover, Tastaturfokus und Fehler sind sichtbar. Enter, Escape, Culture-Parsing,
Fokusverlust und Analysis nur nach Commit bleiben unverändert.

Die Ergebnis-/Statusleiste bleibt horizontal, mit SurfaceSubtle, feiner oberer
Trennlinie, Status beziehungsweise vorhandenen Kennwerten links und deaktivierter
Ergebnisaktion rechts. Keine Ergebnis-Karten oder neue Ergebnisseite.

### Engineering-Icon-Grammatik

EngineeringIcon zeichnet zentrale StreamGeometry-Ressourcen über DrawingContext.
Alle Symbole teilen ein festes 24×24-Artboard, 1,5-DIP-Striche, runde Linienenden
und Linienverbindungen. Größenänderungen skalieren das ganze Artboard; die
individuellen Geometriegrenzen werden nicht separat aufgezogen. Die Farbe folgt
Foreground des Controls; es gibt keine Füllflächen, Rasterbilder oder Unicodeicons.

| Ressource | Technische Darstellung |
| --- | --- |
| PointForce | Vertikaler Kraftpfeil mit offener Spitze |
| PointMoment | Einzelner Kreisbogenpfeil mit markiertem Drehzentrum |
| DistributedLoad | Obere Bezugslinie mit drei gleichen parallelen Kraftpfeilen |
| FixedSupport | Anschlusslinie an eine vertikale schraffierte Wand |
| PinnedSupport | Dreieck mit schraffierter fester Basis |
| RollerSupport | Dasselbe Dreieck mit zwei Rollen und Grundlinie |

SectionProfile ist ein zusätzliches zentrales Querschnittspiktogramm.
Generische Aktionen bleiben textbasiert. Kein zusätzliches Icon-Package.

### Abnahme Visual Design Foundation (03.10.2026)

Restore, Build und Tests beider Solutions wurden ausgeführt. Beide Builds:
0 Warnungen, 0 Fehler. Produkttests: **285 PASS**, Validation-Tests:
**195 PASS**, jeweils keine übersprungenen Tests. Nach der abschließenden
Menü-Fokuskorrektur wurden Produkt-Build und alle 285 Produkttests erneut
fehlerfrei ausgeführt.

Der bisherige Zwei-Eingaben-Test wurde durch zwei Inline-Commit-Kulturfälle
(de-DE/en-US) ersetzt. Escape-, Fokusverlust- und Setup-Tests verwenden jetzt
DimensionLength; die bestehende Viewport-Erwartung wurde von 64 auf 40 DIPs
Maßlinienabstand angepasst. Keine fachlichen Tests gelöscht oder abgeschwächt.

Ein SHA-256-Vergleich aller 366 eingefrorenen Dateien gegen den Arbeitsstand vor
diesem Meilenstein ergab **keine Änderungen**. Geprüft: Core, Solver, Engineering,
Analysis, Reporting, Validation und docs/VALIDATION.md; generierte Build-/Cache-
Dateien wurden ausgenommen. SolverSourceSha256 bleibt:

`6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`

Echte GUI-Prüfung auf macOS, mit temporären Bundles des gebauten Desktops:

- Deutsche und englische Setup-/Editoransicht bei 1250×800 DIPs Inhalt:
  Hierarchie, Kennwertachsen, Hinweise, Auswahlfelder, Primäraktion, einzeilige
  Toolbar, sechs Vektoricons, Projektinfo und Statusleiste visuell geprüft.
- ComboBox-Dropdown geöffnet; Tastaturnavigation und sichtbare Fokusrahmen an
  Auswahlfeld, Menü, GhostButton und Inline-Eingabe geprüft.
- Inline-Edit: 1000-mm-Startwert, deutsches Dezimalkomma und englischer
  Dezimalpunkt, gültiges Enter, Escape, ungültiges Enter mit Fehlermeldung,
  gültiger und ungültiger Fokusverlust sowie eine längere Dezimaleingabe geprüft.
- Setup-Edit, Abbrechen und Übernehmen mit erhaltener Länge geprüft.
- Disabled-Werkzeuge und deaktivierte Ergebnisaktion sichtbar geprüft.
- Native Fenstervergrößerung: Balken und Maßzahl passen sich korrekt an;
  Toolbar und Status bleiben horizontal.

**Prüfgrenzen:** Windows/Linux wurden nicht visuell geprüft. Die exakte
Mindestfenstergröße 1100×650 ließ sich über die verfügbare native GUI-Steuerung
nicht zuverlässig herstellen; sie bleibt als Zielsystemprüfung offen. Die
Mindestgröße ist weiterhin im Fenster definiert. Separate Aufnahmen der kurzen
Hover-/Pressed-Zustände fehlen; die Styles sind zentral implementiert. Die sechs
Toolbuttons wurden dafür bewusst nicht aktiviert. Keine bekannten offenen
Layoutfehler in den tatsächlich geprüften Ansichten.

Screenshots unter `/private/tmp/spandraft-visual-design-foundation/`:

| Datei | Ansicht |
| --- | --- |
| 01-project-setup-de.jpg | Neues Projekt, Deutsch |
| 02-editor-de.jpg | Editor direkt nach Erstellung, Deutsch |
| 03-inline-edit-de.jpg | Kompakte Inline-Längenbearbeitung |
| 04-editor-focus-de.jpg | Fokus auf Ändern; deaktivierte Toolbar sichtbar |
| 05-combobox-focus-de.jpg | Auswahlfeld mit Fokusrahmen |
| 06-editor-resized-de.jpg | Editor nach nativer Fenstervergrößerung |
| 07-project-setup-en.jpg | Neues Projekt, Englisch |
| 08-editor-en.jpg | Editor, Englisch |
| 09-inline-error-en.jpg | Englischer Inline-Fehlerzustand |

Der maschinenlesbare Dateivergleich liegt daneben in `frozen-verification.json`.
Die Standardaufnahmen enthalten zusätzlich die native Titelleiste und haben bei
2×-Skalierung 2500×1656 physische Pixel. Die native Titelleiste und der sichtbare
Automationszeiger gehören nicht zum SpanDraft-Design-System.

**Dateiinventar dieses Meilensteins** (relativ zu src/SpanDraft.Desktop, soweit
nicht anders angegeben):

- Neu: Styles/DesignTokens.axaml, Styles/Typography.axaml, Styles/Controls.axaml,
  Styles/Icons.axaml und Controls/EngineeringIcon.cs.
- Überarbeitet: App.axaml, MainWindow.axaml, Views/ProjectSetupView.axaml,
  Views/EditorView.axaml, Controls/BeamCanvas.cs, Controls/BeamViewport.cs,
  Controls/BeamEditorSurface.axaml und zugehöriges Code-behind,
  Controls/LengthInput.axaml, ViewModels/EditorViewModel.cs und
  ViewModels/LengthInputViewModel.cs.
- Aktualisiert: tests/SpanDraft.Tests/DesktopStateTests.cs und docs/UI.md.

Der vorher vorhandene uncommittete UI-Arbeitsstand wurde weitergeführt.
Keine Paketänderungen, kein Commit und kein Push.
