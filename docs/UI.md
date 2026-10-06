# SpanDraft – verbindliche UI-Spezifikation

Stand: 06.10.2026, Stage 3 – Annotation Interaction & Hardening. Diese Spezifikation ergänzt [KONZEPT.md](KONZEPT.md).
Support-, Punktkraft- und Punktmoment-Placement & Editing einschließlich Drag sind
implementiert. Streckenlast bleibt der verbindliche nächste Editor-Schritt.

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
Balkengrafik, Längeneingabe, Support- und Punktlast-Placement/Preview/Snap/Flyout/Editing/Drag/Delete,
Analysis-Anbindung und Ergebnis-/Statusleiste.

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
bei tatsächlicher Änderung der analysis-relevanten Werte einmal und kehrt zum Editor
zurück. Unverändertes Übernehmen erhält die Dokumentidentität und analysiert nicht.
„Abbrechen“ verwirft ausschließlich die
temporäre Auswahl, erhält Dokument und Editor und löst keine Analysis aus.
Vorhandene Lager und Punktlasten mit IDs, Positionen, Typen/Werten und Reihenfolge
bleiben bei Übernehmen und Abbrechen erhalten. Navigation ins Setup verwirft
temporäre Lager- und Lastinteraktionen.

## 3. Editorlayout und Menü

Von oben nach unten:

1. Datei | Bearbeiten | Ansicht | Hilfe
2. horizontale Engineering-Toolbar
3. Projektinformation: Querschnitt · Werkstoff und „Ändern“
4. dominanter BeamEditorSurface
5. kompakte Ergebnis-/Statusleiste

Toolbar-Reihenfolge:

Punktkraft, Moment, Streckenlast | Einspannung, Festlager, Loslager

Die Toolbar enthält kein Längenfeld. Die drei Lagerwerkzeuge sowie Punktkraft und
Moment sind aktiviert und zeigen einen Checked-State; Streckenlast bleibt deaktiviert. Es gibt
weder einen Auswahl- noch einen Entfernen-Button. Neutral ist der Auswahlzustand.

Menü-Platzhalter (einschließlich Neues Projekt, Öffnen, Speichern,
Rückgängig/Wiederholen, Einstellungen, Zoom, Ergebnisse und Über SpanDraft)
sind deaktiviert. Keine anklickbaren No-op-Aktionen.

Einheiten gehören später in „Bearbeiten → Einstellungen…“ (mm/m, N/kN, Nm/kNm,
N/m/kN/m, Pa/MPa, Dezimaldarstellung), nicht prominent in den Editor.
Aktuell gibt es kein Settings-System und keine Persistenz.

## 4. Desktop-Zustand und Rendering

MainWindowViewModel hält den Modus ProjectSetup/Editor und die Navigation.
ProjectSetupViewModel hält eine temporäre Auswahl. EditorViewModel besitzt
das einzige committed EditorDocument mit Length, Material, Section, Supports und Loads.
Core-Objekte bleiben immutable. Kein zweites konkurrierendes Projektmodell.

Das Schematic Layout gemäß [SCHEMATIC_LAYOUT.md](SCHEMATIC_LAYOUT.md) verwendet stabile
Entity-Namen und einen monotonen NamingState im EditorDocument. Die bestehenden
Support-/Load-Drafts halten Namen transaktional; Canvas und Flyouts zeigen sie an.
EditorPresentationState hält relative AnnotationOffsets getrennt vom Dokument.
Er wird produktiv an BeamRenderState übergeben, gehört zur geöffneten Projekt-Session und überlebt Setup-Navigation und
View-/Control-Wechsel; Abbruch einer Pointer-/Draft-Interaktion setzt ihn nicht zurück.
Namen, Zähler und Offsets gelangen nicht ins BeamModel.

StationLayout/StationTransform und AxisLabelPacker sind reine Desktop-Bausteine
ohne Avalonia-/Entity-Abhängigkeiten. Ein separater Adapter liefert die Union
technischer Symbol-Extents; Text und Hit-Padding zählen nicht dazu. BeamLayoutState
in der Surface erzeugt das committed Layout ausschließlich über
StationRequirementBuilder.FromDocument(), StationOuterMargins und StationLayout.
Dokumentidentität und Pane-Breite bestimmen den Cache. BeamCanvas und
CoordinateAxisPane erhalten dieselbe StationLayoutResult-Instanz.

ObservableObject und ActionCommand bilden die kleine lokale ViewModel-Basis.
Es gibt keine externe MVVM-Bibliothek, DI, Event Bus oder allgemeine Draft-Architektur.
Code-behind steuert Pointer-/Tastaturereignisse, Capture, Fokus, visuelles Hit-Testing
und Overlay-/Popup-Positionen. Dokumentregeln und Analysis verbleiben im ViewModel.

BeamEditorSurface enthält eine flexible Interactive Beam Pane mit BeamCanvas und
eine untere Auto-Row mit eigener CoordinateAxisPane. BeamCanvas zeichnet über
DrawingContext Balken, technische Lager-/Stationsglyphs, individuelle Objektlabels
und temporäre Previews. Die Achse zeichnet Ticks für 0, L und jede innere eindeutige
committed Station, ohne regelmäßige Zwischenticks oder Raster. Sie zeigt `x [mm]`;
nur IsDistorted aktiviert den dezenten Hinweis „Schematische Darstellung“.
IsOverconstrained verwendet den deterministischen Stage-1-Fallback ohne weitere Warn-UX.

UiNumbers formatiert die Koordinaten, Avalonia misst die tatsächlichen Textbounds,
AxisLabelPacker bestimmt Bounds, minimale Lanes und dynamische Pane-Höhe. Die
Einheitenangabe ist außerhalb des Packers. Der rechte Endwert ist ein normales
Avalonia-Control mit der vorhandenen LengthInput-Funktion. CoordinateOverlay
bleibt davon getrenntes, temporäres Hover-/Placement-Feedback.

Der physikalische Balken verläuft von x = 0 bis L und existiert immer.
Es gibt kein Balkenzeichenwerkzeug. Die Pixellänge passt sich an den Viewport an.

StationTransform ist die einzige horizontale Abbildung für Rendering, Preview,
Hit-Testing, Snap und Popup-Anker. Die strikt monotone, reversible, stückweise lineare
Abbildung verwendet Meter und DIPs. BeamViewport hält ausschließlich Pane-Abmessungen
und BeamY. Seitliche Ränder berücksichtigen die Endstations-Extents, normalerweise
mindestens 72 DIPs; technische Symbolgrößen bleiben konstant.

Support-/Load-Placement friert beim Werkzeugstart den sichtbaren Layoutzustand ein;
Support-/Load-Drag beim Press vor der Drag-Schwelle. Während der Pointer-Geste
verwenden Darstellung, Preview und ScreenToPhysical denselben Snapshot, auch bei
Resize. Release verarbeitet die letzte Position noch damit und verwirft ihn danach.
Im normalen New-/Edit-Draft darf Resize das committed Layout neu berechnen; die
physikalische Canvasposition und Textbuffer bleiben erhalten. Wiederholter Drag
beginnt mit einem neuen Snapshot. Cancel, Escape, Capture-Verlust, Werkzeugwechsel
und Detach bereinigen ihn. Preview-Entities verändern keine committed Stationen.

## 5. Transaktionale Längenbearbeitung

Der klickbare rechte Axis-Endwert ist die einzige sichtbare Längeneingabe im Editor und liest
die committed Länge. DimensionLength hält den einzigen temporären Eingabepuffer. Unvollständiger Text wie „1,“ darf während
der Eingabe bestehen und erzeugt weder Core-Objekte noch Analysis-Aufrufe.

Der Axis-Endwert zeigt normal beispielsweise „1000“, die Achse trägt `x [mm]`.
Ein Klick öffnet einen Inline-Editor mit Zahl und statischer Einheit mm.

| Aktion | Verhalten |
| --- | --- |
| Enter, gültig | Commit, genau eine Analysis bei geänderter Länge, Eingabe schließen |
| Enter, ungültig | Dokument erhalten, keine Analysis, Inline-Fehler; Korrektur/Escape möglich |
| Escape | Eingabe verwerfen, committed Wert wiederherstellen, schließen |
| Focus Loss, gültig | Commit wie Enter |
| Focus Loss, Parsingfehler | Letzten committed Wert wiederherstellen, ohne Analysis |
| Focus Loss, dokumentbezogene Ablehnung | Committed Wert wiederherstellen; Konflikt, Geometrie-Preview, Highlights und Fehler entfernen; keine Analysis |
| Unveränderte Länge | Keine erneute Analysis |

Parsing-/Formattinglogik bleibt in UiNumbers. Nach Abschluss kann Focus Loss
nicht nochmals committen. Fehler erscheinen als markiertes Feld mit lokalisiertem
Hilfetext; am Axis-Endwert erscheint zusätzlich ein Inline-Fehlertext.

Parsing verwendet CurrentUICulture und numerische Float-Eingabe ohne
Tausenderseparatoren. Gültig sind endliche positive mm-Werte, die auch in SI
positiv darstellbar bleiben. Null, negative Werte, NaN, Infinity, Überlauf und
Unterlauf auf null werden abgewiesen.

Eine Länge unterhalb einer vorhandenen Lager- oder Punktlastposition wird über den dokumentbezogenen
Commit-Contract abgelehnt: keine Dokumentänderung, kein automatisches Verschieben
oder Löschen und keine Analysis. Enter erhält Input und Fokus zur Korrektur;
Fokusverlust restauriert die committed Länge und entfernt Konflikt, Preview,
Highlights und Fehler vollständig, ohne Analysis.
Escape oder erneute Bearbeitung löscht ihn. Ein Reentranzschutz verhindert
weitere Commits durch Fokusereignisse während einer erfolgreichen Übernahme.

Eine abgelehnte Verkürzung hält zusätzlich einen transienten ConstraintConflictState
im EditorViewModel: angeforderte Länge und defensiv geschützte Entity-IDs aller
Supports und Punktlasten jenseits dieser Grenze. Diese Entities werden mit dem vorhandenen Error-Brush
rot gezeichnet, auch bei Hover; unter der committed Länge bleiben sie fachlich gültig.
Der State gehört nicht zum EditorDocument und löst keine Analysis aus. Er bleibt
während der aktiven abgelehnten Eingabe einschließlich Enter erhalten. Fokusverlust
verwirft die Anfrage samt Highlight und Ablehnungsgrund. Korrektur auf eine
zulässige Länge, Wiederherstellung des Originalwerts, Escape/Cancel oder erneuter
Bearbeitungsbeginn entfernen ihn. Neue ungültige Eingabe entfernt ein überholtes
Highlight. Support- und Punktlast-Commits prüfen verbleibende Konflikte gegen die angeforderte Länge.

Bei einem tatsächlichen Objektkonflikt zeigt die gültige angeforderte Länge zugleich
ein transientes visuelles Balkenende: RequestedLength wird über den committed
StationTransform abgebildet. Der massive Balken endet dort mit einem Error-Endmarker;
der Rest bis zum committed Ende erscheint als dünnes, dezentes gestricheltes
Ghost-Segment. Axis-Ticks, Endwert-Editor, Transform und Entitypositionen bleiben
am committed Layout; blockierende Entities sind an ihren tatsächlichen Positionen rot sichtbar.
Ungültiger/nicht positiver Text erzeugt keine Geometrie-Preview. Korrektur oder
Abbruch oder abgelehnter Fokusverlust entfernt sie vollständig. Dokument und Analysis
werden durch diese Darstellung nicht geändert.

Der Length-Buffer bleibt roher Benutzertext. Das generische Input meldet nur Text-
und Abbruchänderungen; bei einem bestehenden Konflikt prüft der Editor deren
Auswirkung ohne Commit. Formatierung erfolgt erst nach Übernahme/Wiederherstellung.
Die TextBox bleibt während der Session 88 DIPs breit und linksbündig, rund 40 %
schmaler als zuvor. Einheit mm und Schriftgröße bleiben erhalten; außergewöhnlich
langer Text scrollt horizontal innerhalb der Standard-TextBox. Beim Einstieg wird
die erforderliche Axis-Breite und Zeilenhöhe einmal reserviert; Bufferänderungen
lösen kein Axis-Packing oder Stations-Reflow aus. Vollauswahl
erfolgt ausschließlich beim expliziten Einstieg, nicht bei gewöhnlichem Fokusgewinn.
Textbearbeitung, Mausklick-Caret, Pfeiltasten und Backspace/Delete bleiben Avalonia-
Standardverhalten; es gibt keine Caret-Korrektur pro Tastendruck.

Deutsche Dezimalzahlen wie „1000,5“ sind gültig. Ganze mm erscheinen ohne
angehängte Dezimalnullen. Manuelle Eingaben werden nicht auf ganze Millimeter
gerundet. Die Ressourcen folgen ebenfalls CurrentUICulture: Default Englisch,
Deutsch über Strings.de.resx; keine Laufzeit-Sprachumschaltung.

## 6. Analysis und Ergebnisleiste

Nach jeder tatsächlich analysis-relevanten committed Änderung wird ein neues
immutable BeamModel analysiert und ausschließlich BeamAnalysis.Analyze(beam)
aufgerufen. Der zentrale Desktop-Commit-Pfad unterscheidet None, MetadataOnly und
Mechanical anhand einer expliziten BeamModel-Semantik. Rename und reine
Darstellungsänderungen erhalten das bisherige Analysis-Ergebnis. Desktop referenziert direkt
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

## 7. Implementiert: Support Placement & Editing

### 7.1 Committed Lager und stabile Identität

EditorDocument.Supports ist eine defensiv kopierte, schreibgeschützte Sammlung
immutable EditorSupports mit Guid Id, Length Position, Core-SupportType und Name.
Neue Dokumente starten leer. Hinzufügen erzeugt die ID erst beim Commit;
Editieren und Drag erhalten sie. Die ID gehört ausschließlich zum Desktop.
ToBeamModel() erzeugt normale Core-Supports mit exakten Positionen/Typen und
Dokumentreihenfolge; die geordnete Loads-Sammlung wird ebenfalls gemappt. Kein Preview gelangt ins BeamModel.

### 7.2 One-shot-Werkzeuge und temporäre Zustände

Einspannung, Festlager und Loslager sind One-shot-Werkzeuge. Der konkrete
SupportInteraction-Zustand unterscheidet Neutral, Placement, NewDraft, EditDraft
und Drag. Es gibt höchstens ein aktives Werkzeug und einen Draft.

Werkzeug → gültiger Hover → Snap/Preview → Klick → objektgebundenes Flyout →
OK → ein Commit/eine Analysis → neutral. Erneuter Werkzeugklick und Escape
beenden Placement; Werkzeugwechsel verwirft dessen Preview. Pointer Leave
entfernt nur die Hover-Preview. Nach Commit oder Flyout-Abbruch muss das
Werkzeug für ein weiteres Lager erneut gewählt werden.

### 7.3 Snap, Koordinate und Positionsinvariante

SupportSnap ist eine reine Desktop-Hilfslogik ohne PointerEvent-Abhängigkeit.
Placement gilt innerhalb ±18 DIPs um den Balken. Innerhalb 10 DIPs eines
Endpunkts wird zuerst exakt 0 beziehungsweise L gewählt, auch bei L = 1000,5 mm.
Überlappende Fangzonen wählen den näheren Endpunkt, bei Gleichstand den linken.
Sonst: StationTransform.ScreenToPhysical → ganze Millimeter mit MidpointRounding.AwayFromZero.
Außerhalb liegende gerundete Positionen werden abgewiesen. Kein sichtbares Raster.

Hover zeigt dezent „x = … mm“ mit CurrentUICulture. Pro exakt gleicher
committed physikalischer Position ist unabhängig vom Typ nur ein Lager erlaubt.
Eine belegte Position zeigt Error-Preview und lokale Rückmeldung; kein Draft,
Commit oder Analysis, das Werkzeug bleibt aktiv. Beim Editieren wird die eigene
ID aus der Kollisionsprüfung ausgeschlossen. Keine zusätzliche mm-Toleranz.
Die zentrale Positionsprüfung verlangt ausdrücklich 0 ≤ x ≤ L; beide Endpunkte
bleiben inklusive. Parsing und Snap ergänzen diese Regel, ersetzen sie aber nicht.

### 7.4 Symbole, Hit-Testing und Resize

BeamCanvas zeichnet technische Symbole in konstanter DIP-Größe: Festlagerdreieck
mit fester Basis, Loslagerdreieck mit Rollen/Grundlinie und Einspannwand mit
Schraffur. Committed: BeamStroke; Hover/Edit: Accent; ungültig: Error;
Preview: 75 % Opazität. Brushes stammen aus dem zentralen Designsystem.

SupportSymbol definiert gemeinsame DIP-Ausdehnungen für Zeichnung und Hit-Zonen.
Überlappende Hit-Zonen wählen das nächstgelegene Lager, bei Gleichstand das
zuerst im Dokument enthaltene. Neutrales Hover hebt hervor; Klick öffnet Edit.
Supports zeigen ihre bestehenden Namen unter dem technischen Symbol. Individuelle
Labels öffnen bei Click eindeutig das zugehörige Entity; Drag verschiebt
ausschließlich die Annotation (Abschnitt 7.9).
Resize außerhalb einer Pointer-Geste erhält alle physikalischen Werte und
berechnet Zeichenpositionen, Koordinate und Popup-Anker über StationTransform neu.

### 7.5 Transaktionales Flyout

Das 320 DIPs breite, objektgebundene SupportFlyout verwendet vorhandene Surface-,
Border-, Radius-, Typography-, Input- und Button-Tokens. Ohne separate Überschrift
enthält es zuerst „Bezeichnung“/„Name“ mit TwoWay-NameText und bestehender
Namensvalidierung, anschließend „Lagertyp“/„Support type“ mit Dropdown,
„Position X“ mit Input und externer Einheit mm sowie links Löschen und rechts
Abbrechen/OK. Padding 16 und Zeilenabstand 12 DIPs; keine künstliche MinHeight.
Validierung erweitert bei Bedarf den jeweiligen Eingabebereich. Position bleibt
beim Öffnen initial fokussiert. OK trägt ein kleines
Return-Vektoricon mit geerbtem Foreground als Enter-Hinweis. Typ enthält lokalisierte
Einspannung/Festlager/Loslager. Position wird in mm eingegeben: 0 und L sowie
Dezimalwerte sind gültig; TryParsePosition verwendet CurrentUICulture und Float
ohne Tausenderseparatoren. Nicht endliche Werte, ungültiger Text, Werte außerhalb
[0,L] und Positionskollisionen verhindern OK und Enter-Commit.

Positionstext ist ein separater Commit-Buffer: seine validierte Eingabeposition
bewegt weder Symbol noch Flyout-Anker. Die sichtbare physikalische Canvasposition
stammt aus Placement, dem geöffneten Lager oder dem letzten erfolgreichen Drag.
Auch bei schrittweiser Eingabe „7“, „70“, „700“ bleibt sie unverändert.
Ungültiger Text markiert Input und Preview, ohne sie zu verschieben. Gültige
Typwechsel erscheinen sofort an derselben Canvasposition. Beim Editieren ersetzt
die Preview das Original nur in der Darstellung; Resize berechnet den Anker aus
der sichtbaren Position neu.
OK ersetzt Name, Typ und Position gemeinsam bei gleicher ID. Rename-only erhält
das Analysis-Ergebnis. Unverändertes OK schließt
ohne neues Dokument und ohne Analysis; unveränderte SI-Werte bleiben exakt.
Abbrechen, Escape und Outside-Click
verwerfen alles; das Original erscheint wieder. Nur bestehende Lager zeigen
„Löschen“: ein Commit/eine Analysis, danach neutral, kein weiterer Dialog.

Das Popup öffnet nach dem vollständigen Platzierungsklick, damit dieser nicht
selbst einen Abbruch auslöst. Typ-Dropdown und Support-Popup verwenden Overlay-
Popups. Das Support-Popup lässt Canvas-Eingaben passieren; die Surface unterscheidet
Flyout samt Dropdown und aktives Symbol vom Outside-Click. Die aktive Symbol-Hit-Zone
folgt sichtbarer Draftposition und Previewtyp. Ein Klick darauf erhält Draft und
Eingabetext, auch bei neuen Placement-Drafts. Freier Canvas oder ein anderes Lager
bricht die Session ab und konsumiert den Klick; es öffnet dabei kein anderes Lager.
Nach Typwahl kehrt der Fokus zum Dropdown zurück; Tab läuft durch Bezeichnung,
Typ, Position und Aktionen. Escape bei Toolbar-Fokus beendet ebenfalls Placement.

### 7.6 Drag-Verschieben

Neutraler Press auf ein Lager oder ein erneuter Press auf das aktive bestehende
Draftlager bereitet eine Pointer-Geste vor. Ab 4 DIPs
horizontaler Bewegung beginnt Drag; der ursprüngliche Greifversatz bleibt erhalten.
Pointer-Capture sichert den Vorgang. Innerhalb der Beam Pane ist Pointer-Y frei;
die vertikale Outside-Pane-Prüfung bleibt bestehen. X berücksichtigt zuerst
den Greifversatz, klemmt auf den sichtbaren Snapshot-Balkenbereich und nutzt dann
dieselben Millimeter-/Endpoint-Snaps und die Kollisionsprüfung ohne eigene ID.
Links außerhalb bleibt das Lager exakt bei 0, rechts exakt bei L, auch wenn L
nicht ganzzahlig ist. Horizontale Surface-Grenzen beschränken Capture und Release
nicht. Rückkehr in den Balkenbereich setzt die normale Bewegung sofort fort.
Unbelegte geklemmte Ziele sind ohne Warnung gültig; belegte Endpunkte bleiben
Kollisionen. Ausschließlich direkte Drag-Manipulation klemmt: manuelle Flyout-
Positionen außerhalb [0,L] werden weiterhin strikt abgelehnt.

Gültiges Loslassen aus Neutral öffnet Edit mit neuer Position, bisherigem Typ und
gleicher ID. Innerhalb einer Edit-Session sind mehrere Drags möglich: derselbe Draft
bleibt erhalten, das Flyout wird während der Geste verborgen und danach erneut
geöffnet. Jede Geste beginnt an der letzten sichtbaren Draftposition. Erfolgreiches
Release aktualisiert Canvasposition und Positionstext, überschreibt vorherige
manuelle Eingabe und erhält den gewählten Typ. Confirm und Delete sind während
der Geste gesperrt; vorübergehendes Popup-Schließen bricht die Session nicht ab.

Erst gültiges OK/Enter übernimmt die letzte Eingabeposition und den Typ gemeinsam;
Abbrechen/Escape stellt das committed Original wieder her. Belegtes oder unzulässiges
Ziel beziehungsweise Loslassen vertikal außerhalb der Beam Pane verwirft einen Drag
aus Neutral mit lokalisierter Rückmeldung. In einer bestehenden Edit-Session stellt
es stattdessen den Zustand unmittelbar vor der Geste samt Positionstext und Typ
wieder her; das Flyout bleibt mit sichtbarer Rückmeldung offen. Escape oder
unerwarteter Capture-Verlust verwirft die gesamte unbestätigte Session.
Auch nach mehreren Drags entsteht erst beim tatsächlichen Commit genau eine Analysis.

### 7.7 Analysis-Zeitpunkte

Analysis ausschließlich nach Hinzufügen + OK, tatsächlichem mechanischen Edit + OK, Löschen,
gültiger geänderter Länge oder analysis-relevantem Setup-Übernehmen. Keine Analysis bei Werkzeugwahl,
Hover, Snap, Draft, Drag, Eingabe, Abbruch, Light-dismiss oder unverändertem OK.
Die bestehende Analysis-Presentation entscheidet über MissingSupports, UnstableModel
und Success. Stabile unbelastete Systeme zeigen 0 mm, 0 kNm, 0 MPa und ∞; keine
künstliche Last und keine mechanische Sonderlogik im UI. „Ergebnisse“ bleibt disabled.

### 7.8 Implementiert: Punktkraft und Punktmoment

EditorDocument.Loads ist eine defensiv kopierte, schreibgeschützte gemeinsame
Sammlung immutable EditorPointLoads. EditorPointForce enthält Guid Id, Length
Position, Force und Name; EditorPointMoment enthält Id, Position, Moment und Name. IDs entstehen
erst beim Hinzufügen-Commit und bleiben bei Edit/Drag erhalten. ToBeamModel bildet
auf die bestehenden Core-Loads ohne IDs ab und erhält die gemischte Reihenfolge.
WithSupports, WithLoads, Längen- und Setup-Änderungen erhalten die übrigen Entities.

Punktkraft und Moment sind One-shot-Werkzeuge mit Default −1000 N beziehungsweise
+100 Nm. Support- und Load-Werkzeuge lösen einander ab; es gibt höchstens eine aktive
Interaktion. Placement verwendet unverändert SupportSnap einschließlich 1-mm-
Rundung, ±18-DIP-Balkenhit und exaktem Endpoint-Snap. Klick öffnet nach Release
das objektgebundene Flyout. OK oder Abbruch führt zurück nach neutral.

Das 320-DIP-Flyout übernimmt die Support-Tokens und enthält zuerst Bezeichnung/Name
mit bestehendem NameText und Namensvalidierung, anschließend Position X/mm,
Kraft F/N beziehungsweise Moment M/Nm sowie Löschen, Abbrechen und OK ↵.
Nur bestehende Loads zeigen Löschen. Position und Wert sind separate transaktionale
Textbuffer. Gültige Werttexte aktualisieren Richtung und Label der temporären
Vorschau; Positionstext bewegt weder Symbol noch Flyout. Ungültiger Text markiert
Feld und Vorschau, erhält aber deren letzte gültige Geometrie und Wertdarstellung.
Der Anker bleibt beim Tippen stabil. CurrentUICulture und Float-Parsing ohne
Tausenderseparatoren gelten für beide Felder; Werte müssen endlich sein, null
ist zulässig. Manuelle Positionen außerhalb [0,L] werden abgewiesen, nicht geklemmt.
Unberührte Eingaben erhalten die exakten SI-Werte.

OK/Enter validiert und committed Name, Position und Wert gemeinsam. Rename-only
erhält das Analysis-Ergebnis. Ungültiges Enter
hält das Flyout offen. Unverändertes OK schließt ohne Dokumentänderung oder
Analysis. Escape, Abbrechen und Outside-Click verwerfen den Draft; ein Klick auf
das aktive Symbol erhält ihn. Freier Canvas oder ein anderes Objekt verwirft die
Session und konsumiert den Klick. Toolbar-Werkzeugwechsel aktiviert nach dem
Verwerfen unmittelbar das gewählte Werkzeug.

Die bestehende Drag-Geometrie wird für Punktlasten wiederverwendet: 4-DIP-Schwelle,
Greifversatz, Pointer-Capture, horizontales Clamp auf 0/L und normaler Snap nach
Rückkehr. Das Flyout ist während Drag verborgen. Mehrere Drags erhalten dieselbe
Draft-Session und ID. Gültiges Release ersetzt den Positionsbuffer, erhält den
Wertbuffer und öffnet das Flyout wieder. Ungültiges Release restauriert bei einem
bestehenden Draft dessen Zustand vor der Geste samt Rohtexten; aus Neutral wird die
Geste verworfen. Confirm/Delete sind während Drag gesperrt. Escape und unerwarteter
Capture-Verlust verwerfen die gesamte unbestätigte Session.

PointLoadSymbol definiert gemeinsame Darstellung und Hit-Zonen je exakter Station
und Load-Art. Kräfte liegen an ihrer Station mit 1 DIP Abstand zwischen gezeichneter
Kontur und oberem Balkenrand; der konstante Schaft liegt oberhalb von BeamY.
Der Abstand berücksichtigt die zentral definierte Balkendicke und die Symbolkontur.
Positive Kräfte zeigen nach oben, negative nach unten.
Gleichgerichtete Kräfte teilen einen Glyph; beide Vorzeichen ergeben einen
Doppelpfeil. Zero zeigt keine Richtungsspitze. Momente sind exakt auf BeamY
zentriert: Die positive Grundform verläuft von 110° gegen den Uhrzeigersinn über
280° bis zur Spitze bei 190°. Die negative Form ist an der senkrechten Achse
gespiegelt und verläuft von 250° im Uhrzeigersinn bis zur Spitze bei 170°.
Dabei gilt 12 Uhr = 0°, im Uhrzeigersinn zunehmend. Beide Vorzeichen ergeben
einen gemeinsamen 340°-Bogen mit beiden Spitzen; überlappende Bogenstücke werden
nur einmal gezeichnet. Zero hat einen neutralen 280°-Bogen ohne Richtungsspitze.
Kraft- und Momentpfeilspitzen sind gleich große gefüllte Dreiecke mit 1,5-DIP-Kontur.
Es gibt keine Leader-Lines oder per-load Symbol-Lanes. Entities, Werte und Labels
bleiben getrennt; es wird keine Resultierende gebildet.

Labels zeigen „F1 = 1000 N“ beziehungsweise „M1 = -50 Nm“ mit CurrentUICulture,
positive Werte ohne Plus. BeamRenderState misst Textbounds und ordnet individuelle
Labels deterministisch in kompakten Reihen oberhalb der Loads und unterhalb der
Supports an. Rendering und Hit-Testing verwenden diesen gemeinsamen State.
Committed verwendet BeamStroke, Hover/Draft Accent, Constraint-Konflikt oder
ungültige Eingabe Error; Konflikt hat Vorrang. Individuelle Labels öffnen ihr
Entity eindeutig; Drag verschiebt nur dessen Annotation. Einzelglyphs öffnen/ziehen direkt;
Shared Glyphs verwenden das bereits eindeutig editierte Entity. Sonst öffnet ein
kleines Surface-lokales Standard-Popup mit Entity-Buttons den bestehenden EditLoad-Pfad,
ohne willkürliche Auswahl oder allgemeines Selection-Framework.

Die Canvas-Mindesthöhe folgt committed Objektlabels mit einer reservierten
Preview-Zeile. Hover/Pointer-Leave verändern weder Stationslayout noch Beam-Lage.
Ein vertikaler ScrollViewer hält umfangreiche Beschriftungen erreichbar, ohne
Zoom/Pan oder Änderung der physikalischen Werte. Manuelle AnnotationOffsets und
Label-Drag sind produktiv angebunden; die Axis bleibt davon unabhängig.

Analysis erfolgt ausschließlich nach neuer Load + OK, tatsächlichem Edit + OK
oder Delete. Alle Transienten einschließlich Drag und unverändertem OK bleiben
analysisfrei. Längenkonflikte nutzen den bestehenden ConstraintConflictState und
Ghost-Balken gemeinsam für Supports und beide Punktlasttypen. Abgelehnter
Fokusverlust entfernt weiterhin Anfrage, Preview, Highlights und Fehler vollständig.

### 7.9 Entity-Annotationen: Auto und ManualOffset

BeamRenderState erhält den immutable EditorPresentationState der geöffneten
Projekt-Session. Fehlender Offset bedeutet Auto; ein Eintrag bedeutet
ManualOffset(dx, dy) in DIPs. AutoBounds ist der offset-unabhängig berechnete
automatische Referenzanker. Sichtbare manuelle Bounds sind exakt AutoBounds plus
Offset; absolute Canvas-Koordinaten werden nicht gespeichert.

Zuerst entstehen die gemessenen automatischen Referenzpositionen. Danach werden
manuelle Offsets angewendet und ausschließlich Auto-Labels um diese festen
Hindernisse herum in kompakte Reihen gelegt. Das gilt auch zwischen Support- und
Load-Bereich. Zwei manuelle Labels dürfen sich bewusst überlappen. Bei identischem
Hit gewinnt das zuletzt gezeichnete Label. Entity-Labels haben vor Glyphs Vorrang;
Shared-Glyph-Auswahl und Positionsgesten behalten ihre bestehenden Regeln.

PointerDown auf ein committed Entity-Label merkt Entity-ID, Pointerstart,
AutoBounds, sichtbare Startposition und ursprünglichen nullable Offset und
nimmt Capture. Unterhalb von 4 DIPs räumlicher Bewegung bleibt es ein Click:
Release öffnet das Entity über den bestehenden Edit-Pfad. Oberhalb der Schwelle
ändert die Geste ausschließlich SetAnnotationOffset(), niemals Position, Typ,
Kraft/Moment, Dokument oder Analysis. Der Offset ergibt sich aus sichtbarer
Startposition minus Auto-Referenzanker plus Pointerbewegung. Dadurch springt ein
durch Auto-Collision-Layout verdrängtes Label beim Wechsel zu Manual nicht.
Normales Release behält den Offset und öffnet kein zusätzliches Flyout.

Label-Cancel wird vor generischem Surface-/Flyout-Cancel behandelt, einschließlich
Escape im Tunnel vor dem Flyout-Tastaturhandling. Escape und CaptureLost
restaurieren ausschließlich den ursprünglichen Offset und beenden die Geste.
Ursprüngliches Auto wird wieder durch einen fehlenden Eintrag dargestellt. Ein
offener Support-/Load-Draft und seine Rohtextbuffer bleiben erhalten. Capture
wird erst nach Bereinigung der aktiven Geste gelöst, damit normales Release keinen
nachträglichen Rollback auslöst. Detach und Session-Wechsel bereinigen unfertige
Gesten; eine später abgebrochene Entity-Draft-Session entfernt keinen bereits
abgeschlossenen Label-Offset.

Während der Label-Geste bleiben Layout-Frame, Pane-Höhe und BeamY eingefroren.
Nach Release darf die benötigte Höhe anhand final sichtbarer Annotation-Bounds
wachsen; der vorhandene vertikale Scrollbereich hält diese erreichbar. Resize,
schematische Entzerrung, mechanischer Commit und geänderte Textbreiten berechnen
AutoBounds neu; gespeicherte Offsets bleiben numerisch gleich. Offsets werden
nicht geklemmt oder durch Auto-Layout verändert. Delete bereinigt verwaiste
Einträge; eine neue Projekt-Session startet leer.

Neue Draft-Labels ohne committed ID sind nicht draggable. Coordinate-Axis-Labels
bleiben read-only, ausgenommen der bestehende L-Endwert; sie besitzen keine
AnnotationOffsets. Ein sichtbarer Reset-Befehl wird nicht vorgezogen.

### 7.10 Nächster Schritt: Streckenlast

Streckenlast bleibt deaktiviert. Geplant sind Start-/Endpunkt, signed Intensität,
Bereichs-Preview und transaktionales Flyout nach denselben Grundprinzipien.

## 8. Spätere Ergebnisse-/Reportansicht

„Ergebnisse“ wechselt künftig zu einer eigenen Ansicht innerhalb derselben
MainWindow-Instanz. Geplant: Kennwerte, Reaktionen, Durchbiegung w(x),
Querkraft V(x), Biegemoment M(x), später PDF und XLSX.
Im Editor bleibt die kompakte Ergebnisleiste. Die Ergebnisansicht wird jetzt
nicht implementiert.

## 9. Bewusst offen

Noch nicht implementiert: Streckenlastplatzierung/-preview/-flyout/-bearbeitung/-löschung,
Delete-Taste, Undo/Redo, Zoom/Pan,
Tabellen/Diagramme, Ergebnisse-/Reportseite, PDF/XLSX, Speichern/Laden, Auto-Save,
Settings-Persistenz, Theme-Umschaltung, echte Profil-/Norm-/Herstellerbibliotheken,
Profilimport, eigene Materialien und Querschnittseditor.
Keine neuen Solverfunktionen oder Engineering-Nachweise.

## 10. Prüfung

Reine Desktop-State-/ViewModel-Tests laufen im vorhandenen Produkttestprojekt
ohne GUI-Initialisierung, Headless oder neue Testpakete. Textmessungs-Integrationstests
initialisieren vorhandenes Avalonia/Skia/HarfBuzz ohne Fenster und prüfen
Formatierung → reale Messung → Packer sowie endliche Bounds. Konkrete Lane-Zahlen
verwenden weiterhin deterministisch gemessene Bounds in reinen Tests, keine
plattformabhängigen Pixelbreiten. Der Analysis-Delegate
ermöglicht das Zählen und Prüfen der übergebenen BeamModels; produktiv ist allein
BeamAnalysis.Analyze angeschlossen.

Das Regression-Gate umfasst Restore/Build/Tests beider Solutions, unveränderte
eingefrorene Bereiche und den bestehenden SolverSourceSha256. GUI-Smoke-Checks
und deren tatsächliche Grenzen werden im Abschlussbericht separat dokumentiert.

Aktueller Stand (06.10.2026): **678 Produkttests PASS**, **195 Validation-Tests
PASS**, **18 Acceptance-Fälle PASS**; Release-Builds mit null Warnungen/Fehlern.
Die folgenden Abnahmen dokumentieren ausdrücklich frühere Entwicklungsstände;
maßgeblich für die heutige Interaktion sind die Abschnitte 4, 5 und 7.

### Abnahme Stage 3: Annotation Interaction & Hardening (06.10.2026)

- Product Restore/Release Build: 0 Warnungen, 0 Fehler; 678 Tests PASS,
  einschließlich 37 neuer Annotation-Layout-, Gesture- und ViewModel-Integrationsfälle.
  Alle 641 bestehenden Tests und deren Assertions bleiben erhalten.
- Validation Restore/Release Build: 0 Warnungen, 0 Fehler; 195 Tests PASS;
  Acceptance 18/18 PASS. Keine übersprungenen Tests.
- SolverSourceSha256 unverändert:
  `6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`.
- AutoBounds und sichtbare Bounds sind getrennt. ManualOffsets sind produktive
  Session-Metadaten, bleiben relativ bei Reflow und lösen keine Analysis aus.
  Die Tests prüfen gemeinsame Hindernisse, bewusste Überlappung, Click/Drag,
  Rollback mit offenen Draft-/Rohtextbuffern, Height-/Transform-Stabilität,
  Delete, neue Session und bestehende Glyph-/Axis-Verträge.
- Reine und ViewModel-Integrationstests benötigen keine native GUI und keine
  plattformabhängigen Fontmetriken. Die vorhandene echte Textmessung bleibt erhalten.
  Geroutete Surface-Events erfordern eine Cursor-/Windowing-Plattform; zusätzliche
  Testpakete oder eine eigene Test-Plattform wurden nicht eingeführt.
- Native UI-Abnahme vom Nutzer bestätigt: Oberfläche geprüft, alles in Ordnung.
  Nach dem nicht freigegebenen Computer-Use-Aufruf wurde die Prüfung vom Nutzer
  selbst übernommen; kein erfolgreicher eigener Computer-Use-Prüflauf behauptet.
- Alle 138 getrackten Frozen Files und Packages unverändert. Stage 3 ist abgenommen;
  UDL bleibt der nächste Editor-Meilenstein. Persistenz, Undo/Redo, Zoom/Pan und
  allgemeine Manager bleiben außerhalb des Scopes.

### Abnahme Stage 2: Produktives Schematic Layout (06.10.2026)

- Product Restore/Release Build: 0 Warnungen, 0 Fehler; 641 Tests PASS,
  einschließlich 46 neuer Rendering-, Axis-, Glyph-, Snapshot-, Naming- und
  Textmessungsfälle. Stage-1-Layout-/Packer-/Naming-Tests bleiben erhalten.
- Validation Restore/Release Build: 0 Warnungen, 0 Fehler; 195 Tests PASS;
  Acceptance 18/18 PASS. Keine übersprungenen Tests.
- SolverSourceSha256 unverändert:
  `6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`.
- Gemeinsamer committed Layoutpfad und temporäre Pointer-Snapshots sind produktiv.
  Lineares BeamViewport-Mapping, DimensionY, Maßlinien/Guides und DimensionOverlay
  wurden entfernt. CoordinateAxisPane ersetzt die klassische Bemaßung.
- Überholte Tests für Maßlinien-Mittelpunkte und 50-DIP-Symbol-Lanes wurden auf
  Axis-/Conflict-Geometrie, Shared Glyphs und individuelle Label-Hits umgestellt;
  fachliche Positionen, Werte, Commit- und Analysis-Assertions bleiben erhalten.
- Native macOS-Prüfung im temporären Release-App-Bundle begonnen; anschließend
  bestätigt der Nutzer nach ausführlicher Interface-Prüfung: in Ordnung,
  keine Auffälligkeiten. Keine zusätzliche Screenshot-Schleife.
- Core, Solver, Engineering, Analysis, Reporting, Validation und Packages
  unverändert. Kein Commit und kein Push.
- Stage 3/4 übernehmen die weiteren Schematic-Interaktionen und manuelle
  Label-Offsets/Label-Drag; keine allgemeine Selection-/Annotation-Architektur
  oder Persistenz vorweggenommen.

### Abnahme Stage 1: Schematic-Layout-Fundament (06.10.2026)

- Product Restore/Release Build: 0 Warnungen, 0 Fehler; 595 Tests PASS,
  davon 125 neue Layout-, Transform-, Packer-, Naming-, Commit- und Session-Fälle.
- Validation Restore/Release Build: 0 Warnungen, 0 Fehler; 195 Tests PASS;
  Acceptance 18/18 PASS. Keine übersprungenen Tests.
- SolverSourceSha256 unverändert:
  `6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`.
- Die Analysis-Semantik wird explizit zentral gepflegt, ohne Reflection-Schema.
  Unbekannte Domain-Varianten sind konservativ analysis-relevant. Zusätzliche
  Material-/Querschnittsmetadaten sind nicht zusätzlich analysis-relevant,
  soweit ihre aktuelle Wirkung durch E/Re beziehungsweise A/I/W erfasst ist.
- Unverändertes Setup ist nun ein No-op. Der ausdrücklich freigegebene Test
  wurde auf diesen Vertrag umgestellt; ein bestehender Entity-Erhaltungstest
  verwendet beim Apply eine echte Materialänderung und behält seine Assertions.
- Core, Solver, Engineering, Analysis, Reporting und Validation unverändert;
  keine Packages ergänzt. Keine native GUI-/Screenshot-Abnahme, kein Commit/Push.
- Stage 2 übernimmt die produktive Layout-/Achsen-Anbindung. Rendering,
  sichtbare Namen, Label-Drag und Interaktions-Snapshots bleiben vorbereitet.

### Abnahme Punktkraft/Punktmoment (04.10.2026)

- Product: 470 Tests PASS (396 bestehende + 74 neue), keine übersprungenen Tests.
- Validation: 195 Tests PASS; Acceptance 18/18 PASS. Beide Release-Builds mit
  null Warnungen/Fehlern; SolverSourceSha256 unverändert.
- Core, Solver, Engineering, Analysis, Validation-Code und Goldens unverändert;
  keine Packages hinzugefügt.
- Ein abschließender nativer macOS-Abnahmelauf über ein temporäres Release-App-Bundle:
  Punktkraft-/Moment-Placement, N-/Nm-Flyouts, signed Texte, One-shot-Abschluss,
  mehrere gestaffelte Kräfte und Momentvorschau geprüft. Support-OK erzeugte
  außerdem eine erfolgreiche Analysis mit aktualisierter Ergebnisleiste.
- Flyout-Textfelder wurden über ihre Accessibility-Setter gesteuert; geometrische
  AX-Klicks waren uneindeutig und wurden anhand einer Diagnoseaufnahme geprüft.
  Die abschließende Aufnahme liegt lokal unter
  `.artifacts/point-loads/native-point-loads.png` (2500×1656 physische Pixel).
- Wiederholtes Drag, Capture-Abbruch, Clamp/Rückkehr, große Stapel und gemischte
  Längenkonflikte sind durch Desktop-State-/Geometrietests abgesichert; keine
  zusätzliche native Drag-/Resize-Testserie durchgeführt.

### Historische Abnahme des funktionalen UI-Fundaments (03.10.2026)

- Beide Solutions restauriert und mit null Warnungen/Fehlern gebaut.
- 285 Produkttests bestanden: 247 bestehende und 38 neue Desktop-Testfälle.
- Alle 195 Validation-Tests bestanden; keine übersprungenen Tests.
- Core, Solver, Engineering, Analysis, Reporting, der gesamte Validation-Bereich
  einschließlich Cases/Golden References und docs/VALIDATION.md unverändert.
- SolverSourceSha256:
  `6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`.

Eine echte macOS-GUI wurde über ein temporäres App-Bundle des gebauten Desktops
geprüft. Die 24 angeforderten Smoke-Check-Punkte wurden überprüft:
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
wurde über reale Analysis-Ergebnisse in State-Tests geprüft, da das damalige
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
| BeamStroke / AxisStroke | #374151 / #697586 |

Accent dient Primäraktion, Fokus, aktiven Werkzeugen und Lagerpreviews, nicht großen
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
Pressed-Skalierung. `.selected` und `ToggleButton:checked` zeigen aktive
Lagerwerkzeuge im gemeinsamen Designsystem an.
Disabled hat Vorrang vor Hover/Pressed/Selected; Icons erben die Textfarbe.

TextBox und ComboBox teilen Höhe, Schriftgröße, Border, Radius und Fokusfarbe.
Fluent übernimmt weiterhin Textbearbeitung, Auswahl und Dropdown-Verhalten.
Die ComboBox erhält beim Fokus keine gefüllte blaue Fläche. `inlineDimension`
ist 30 DIPs hoch, linksbündig und Semibold; ihre Breite ist während der Session
auf 88 DIPs festgelegt. Sehr lange Eingaben bleiben im Textfeld horizontal
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
Alle sechs Werkzeuge besitzen lokalisierte Accessibility-Namen. Lagerwerkzeuge
sowie Punktkraft/Moment sind aktivierbar, Streckenlast bleibt deaktiviert.
Bei 1100 DIPs Mindestbreite erfolgt kein Umbruch.
Die Projektinfo ist eine Textzeile mit BodyStrong und Ghost-Aktion „Ändern“.

Der Canvas verwendet CanvasBackground ohne Cardrahmen. BeamCanvas erhält
BeamBrush, GhostBrush, AccentBrush und ErrorBrush als render-invalidierende
StyledProperties; die separate CoordinateAxisPane verwendet AxisStroke. Der Balken
ist aktuell 3 DIPs stark (`SchematicMetrics.BeamStrokeWidth`), die Achse 1 DIP.
Die reversible x-Abbildung übernimmt allein
StationTransform. Die einzige Längenbearbeitung sitzt am rechten Axis-Endwert.
Anzeige und Inline-Eingabe verwenden die gepackten Endpoint-Bounds;
Hover, Tastaturfokus und Fehler sind sichtbar. Enter, Escape, Culture-Parsing,
Fokusverlust und Analysis nur nach Commit verwenden den bestehenden Pfad; die
dokumentbezogene Längenablehnung ist in Abschnitt 5 beschrieben.

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

### Historische Abnahme Visual Design Foundation (03.10.2026)

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

Screenshots und maschinenlesbarer Dateivergleich wurden als temporäre lokale
Abnahmeartefakte erstellt; sie sind keine dauerhaften Projektressourcen.
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

## 12. Historische Erstabnahme: Support Placement & Editing einschließlich Drag (04.10.2026)

Dieser Abschnitt beschreibt den damaligen Stand vor den Support-/Length-
Refinements. Der damalige Gesamtstand umfasste 396 Produkttests; der aktuelle
Stand steht in Abschnitt 10.
Spätere Änderungen an Positionstext, Edit-Drags und Focus Loss sind in den
Abschnitten 5 und 7 verbindlich beschrieben.

Beide Solutions restauriert und im Release-Modus gebaut: **0 Warnungen, 0 Fehler**.
Produkttests: **342 PASS** (285 bestehende + 57 neue Desktop-Lager-Testfälle).
Validation: **195 PASS**, keine übersprungenen Tests. Der unveränderte Acceptance-
Runner meldet **18 Fälle PASS** gegen die bestehenden Golden References.
SolverSourceSha256 bleibt exakt:

`6f4a3bf5a3e283680d57c087491a623af715e164089e3219c6bed77b586c06df`

SHA-256-Vergleich aller **138 getrackten eingefrorenen Dateien** mit dem sauberen
Ausgangsstand: keine Änderungen. Core, Solver, Engineering, Analysis, Reporting,
Validation einschließlich Cases/Golden References, docs/VALIDATION.md und
.github/workflows/ci.yml sind unverändert. Generierte Build-/Cache-Dateien sind
kein Teil dieses Vergleichs. Keine Paketänderungen, Solver-/Tolerance-Änderungen,
Referenzregeneration, Python-Ausführung, Commits oder Pushes.

Neue Tests in DesktopSupportTests decken die 31 angeforderten Verhaltensfälle ab,
zusätzlich defensive Collection-Kopien, Kollisionskorrektur, letzte Preview bei
ungültigem Text, deterministisches Hit-Testing, Drag-Schwelle/Greifversatz,
transaktionalen Drag-Abschluss, ungültige Ziele, wiederholte Abschlussaktionen,
invaliden Typ, direkte Setup-Navigation, reentranten Fokusverlust und unveränderte
SI-Positionen ohne Millimeter-Roundtrip.

Echte native macOS-Abnahme mit temporärem Bundle des Release-Desktops:

- Lagerbuttons aktiv/Checked; Lastbuttons und Ergebnisse disabled.
- Festlager-Hover bei 207 mm mit Preview/x, Endpunktsnap, kompaktes New-Flyout,
  Einspannung-Abbruch und Festlager-Commit bei exakt 200 mm; danach neutral.
- Zweites Loslager am Balkenende; reale unbelastete Success-Leiste mit Nullwerten/∞.
- Editieren, Typwechsel, deutsche Dezimalposition 200,5 mm, Live-Preview,
  transaktionales Cancel und gemeinsames OK geprüft.
- Escape bei Toolbar-Fokus und im Flyout, ungültiges Enter mit disabled OK,
  Light-dismiss sowie Tab-Zyklus Typ → Position → Aktionen geprüft.
- Drag 200 → 250 mm: Loslassen öffnet Draft; Escape restauriert 200 mm;
  erneutes Ziehen mit OK übernimmt 250 mm. Drag auf belegtes Ziel stellt Original
  wieder her und zeigt eine lokalisierte Rückmeldung.
- Duplicate-Placement: Error-Preview/Rückmeldung, kein zweites Lager.
- Löschen: Symbol entfernt und Analysis-Status aktualisiert.
- Verkürzung unter Endlager abgelehnt; Input/Fokus bleiben bei Enter erhalten.
  Damals restaurierte Fokusverlust 1000 mm mit sichtbarem Fehler; heute verwirft
  er Konflikt und Fehler vollständig (Abschnitt 5).
- Setup-Abbrechen und -Übernehmen erhalten beide Lager.
- Native Fenstervergrößerung und Rückkehr: 200,5-mm-Position bleibt erhalten;
  geöffnetes Edit-Flyout und Preview folgen dem Viewport.

In der GUI-Abnahme korrigiert: Popup erst nach vollständigem Platzierungsklick
öffnen; Typ-Dropdown im Overlay halten und Fokus restaurieren; abgelehnten
Längeninput vor der Validierung nicht ausblenden. Der damalige Produkttestlauf
bestand mit 342 Fällen.

Screenshots und Berichte dieser Erstabnahme waren temporäre lokale Artefakte.
Standardaufnahmen: 2500×1656 physische Pixel einschließlich nativer Titelleiste
bei 1250×800 DIPs Inhalt; Resize-Aufnahme: 2940×1710 Pixel. Lokale Artefaktpfade
werden nicht als dauerhafte Projektdokumentation geführt.

Prüfgrenzen: Windows/Linux, englisches Flyout-Layout und exakt 1100×650 DIPs
wurden nicht visuell geprüft. Englisch/Deutsch und der fraktionale Endpunkt
L = 1000,5 mm sind automatisiert geprüft. Unerwarteter Capture-Verlust und Escape
während eines noch gehaltenen Drag wurden nicht separat nativ provoziert; der
Cancel-State und ungültige Releases sind automatisiert abgedeckt.
Keine bekannten Layoutfehler in den tatsächlich geprüften Ansichten.

Nächster Meilenstein: Punktkraft, Punktmoment und Streckenlast mit eigenem
Placement, Preview, transaktionalem Flyout und Editing/Delete. Kein allgemeines
Tool-Framework vorweggenommen; Undo/Redo, Delete-Taste, Tabellen, Diagramme,
Persistenz und Export bleiben offen.
