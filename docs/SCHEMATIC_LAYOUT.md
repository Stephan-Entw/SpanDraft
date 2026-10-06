# SpanDraft – Schematische Balkendarstellung und Stationslayout

> Status: verbindliche Desktop-Spezifikation für die nächste UI-Ausbaustufe  
> Stand: 06.10.2026  
> Geltungsbereich: `SpanDraft.Desktop`  
> Fachliche Vorzeichen und physikalische Größen bleiben durch `docs/DOMAIN.md` und das Core-Modell definiert.  
> Bei Widersprüchen zu älteren Rendering-/Bemaßungsregeln in `docs/UI.md` hat dieses Dokument für Balkendarstellung, Stationslayout, Koordinatenachse und Annotationen Vorrang.

## 1. Ziel

Die Balkengrafik von SpanDraft ist eine technische, schematische Darstellung des Rechenmodells. Sie muss physikalische Positionen, Reihenfolge und Werte eindeutig wiedergeben, muss aber nicht überall streng maßstabgetreu sein.

Das Konzept verfolgt fünf Ziele:

1. nahe beieinanderliegende Lager und Lasten bleiben lesbar,
2. Koordinaten bleiben die maßgebende geometrische Information,
3. Punktkräfte und Momente werden nach üblicher technischer Diagrammkonvention direkt am Balken dargestellt,
4. automatische Darstellung bleibt für normale Maschinenbau-Modelle kompakt und robust,
5. spätere Streckenlasten und Querschnittsgrenzen können ohne neue grundlegende Layoutarchitektur ergänzt werden.

Die mechanische Berechnung bleibt vollständig unabhängig von dieser Darstellung. Solver, Core und Analysis erhalten ausschließlich die realen physikalischen Werte.

---

## 2. Bewusster Scope

Diese Spezifikation beschreibt:

- schematische x-Abbildung,
- eindeutige Stationen,
- Mindestabstände zwischen Stationen,
- eine separate Koordinatenachse,
- automatische Koordinaten-Label-Verteilung,
- Darstellung von Lagerungen, Punktkräften und Punktmomenten,
- stabile technische Bezeichnungen,
- automatisch und manuell positionierte Objektbeschriftungen,
- Interaktionsregeln für Placement, Drag und Editing,
- Längenkonflikte,
- definierte Robustheit bei extremer Dichte.

Nicht Bestandteil dieses Meilensteins:

- Streckenlast-Implementierung,
- Zoom/Pan,
- Einheiten-Einstellungen,
- Speichern/Laden,
- Undo/Redo,
- Ergebnisdiagramme,
- Reaktionsdarstellung,
- segmentierte Balken oder Wellen,
- Solveränderungen.

V1 verwendet weiterhin einen konstanten Werkstoff und einen konstanten Querschnitt über die gesamte Balkenlänge.

---

## 3. Begriffe

### 3.1 Physikalische Koordinate

`x` ist die reale Position entlang des Balkens. Sie wird in Core/SI unverändert gespeichert und berechnet.

### 3.2 Station

Eine Station ist eine eindeutige physikalische x-Koordinate, an der mindestens ein darstellungsrelevantes Merkmal liegt.

Aktuelle Quellen:

- Balkenanfang `x = 0`,
- Balkenende `x = L`,
- Lager,
- Punktkräfte,
- Punktmomente.

Später können unter anderem hinzukommen:

- Anfang und Ende einer Streckenlast,
- Querschnittsgrenzen,
- Materialgrenzen.

Exakt gleiche physikalische Koordinaten bilden exakt dieselbe Station. Unterschiedliche Koordinaten bleiben unterschiedliche Stationen, auch wenn ihr Abstand sehr klein ist.

Die Gleichheit folgt der bereits verwendeten exakten Domain-/Desktop-Position. Es wird keine zusätzliche geometrische Toleranz für das Zusammenfassen von Stationen eingeführt.

### 3.3 Stationsanker

Jede Station besitzt einen horizontalen Bildschirmanker. Alle technischen Symbole dieser Station beziehen sich auf denselben Anker.

### 3.4 Symbol-Bounds

Die technische Symbolgruppe einer Station besitzt horizontale Ausdehnungen:

- `LeftExtent`,
- `RightExtent`.

Dazu zählen technische Symbole wie Lager, Momentbogen und Kraftpfeil, aber keine Textbeschriftungen.

### 3.5 Annotation

Eine Annotation ist sichtbarer Text zu einem Entity, z. B.:

- `A`,
- `F1 = 1000 N`,
- `M2 = -50 Nm`.

Annotationen verändern das mechanische Modell nicht.

### 3.6 Coordinate Axis

Die Coordinate Axis ist der separate Bereich unterhalb der Balkengrafik. Sie zeigt reale Stationskoordinaten, ist aber bei aktiver Entzerrung keine lineare maßstäbliche Skala.

---

## 4. Architektonische Verantwortlichkeiten

Die Darstellung wird in klar getrennte Ebenen aufgeteilt.

```text
EditorDocument
  ├─ Engineering-Entities mit stabilen IDs
  ├─ stabile technische Bezeichnungen
  └─ NamingState / monotone Auto-Zähler

EditorPresentationState
  └─ manuelle AnnotationOffsets und spätere reine Darstellungsoptionen

            ↓

Station requirements
            ↓

pure StationLayout
            ↓

StationLayoutResult / StationTransform
  ├─ PhysicalToScreen(x)
  ├─ ScreenToPhysical(screenX)
  ├─ Stations
  ├─ IsDistorted
  └─ IsOverconstrained

        ↙                         ↘
Interactive Beam Pane       Coordinate Axis Pane
Symbole + Annotationen      Ticks + Koordinatenlabels
```

### 4.1 EditorDocument

`EditorDocument` bleibt die committed Projektwahrheit für fachlich relevante Desktop-Entities und ihre stabile Identität.

Zu einem Entity gehören insbesondere:

- stabile `Guid`,
- reale Position,
- fachliche Werte bzw. Typ,
- stabile technische Bezeichnung.

Namen werden beim Mapping zu Core ignoriert.

### 4.2 EditorPresentationState

Reine Darstellungsmetadaten werden nicht mit mechanischen Daten vermischt.

Dazu gehören insbesondere:

- manuelle Label-Offsets,
- später eventuell Axis-Sichtbarkeit oder vergleichbare UI-Präferenzen.

Diese Daten dürfen später mit einem Projekt gespeichert werden, sind aber kein Bestandteil von `BeamModel`.

### 4.3 Mechanical vs. metadata-only changes

Nicht jede committed Desktop-Änderung erfordert Analysis.

Mechanische Änderung:

- Position,
- SupportType,
- Kraftwert,
- Momentwert,
- Balkenlänge,
- Material/Querschnitt.

Folge: bei tatsächlicher Änderung genau eine Analysis.

Metadata-only:

- Rename eines Entity,
- AnnotationOffset,
- reine Darstellungsoption.

Folge: keine Analysis.

Wenn ein Flyout gleichzeitig einen Namen und einen mechanischen Wert ändert, entsteht ein gemeinsamer Dokument-Commit und genau eine Analysis.

Diese Unterscheidung muss explizit im Desktop-Commit-Pfad abgebildet werden und darf nicht zufällig vom jeweiligen Control abhängen.

---

## 5. Stabile technische Bezeichnungen

### 5.1 Automatische Bezeichnungen

Supports:

```text
A, B, C, ... Z, AA, AB, ...
```

PointForces:

```text
F1, F2, F3, ...
```

PointMoments:

```text
M1, M2, M3, ...
```

Für eine spätere Streckenlast ist vorgesehen:

```text
q1, q2, q3, ...
```

Die Bezeichnung wird im Flyout vorausgefüllt und bleibt editierbar.

### 5.2 Monotone Vergabe

Automatische Bezeichnungen werden pro Projekt monoton vergeben.

Beispiele:

- F2 wird gelöscht → die nächste automatische Kraft wird nicht wieder F2.
- B wird gelöscht → die automatische Lagerfolge läuft weiter.
- Umbenennen rollt keinen Counter zurück.
- Löschen rollt keinen Counter zurück.

Ein Auto-Kandidat wird erst beim erfolgreichen Commit eines neuen Entity verbraucht. Ein abgebrochener New-Draft verbraucht keinen Namen.

Wird ein neuer Draft automatisch als `F3` vorgeschlagen, der Benutzer benennt ihn vor dem ersten Commit in `Motorlast` um und committed, gilt F3 trotzdem als verbraucht. Die nächste automatische Kraft ist F4.

### 5.3 Keine Ableitung aus sichtbaren Strings

Der nächste Auto-Name darf nicht aus den aktuell vorhandenen Labels abgeleitet werden.

Das Projekt hält separate monotone Zähler, sinngemäß:

```text
NextSupportOrdinal
NextForceNumber
NextMomentNumber
später NextDistributedLoadNumber
```

Diese Zustände gehören langfristig zum persistierbaren Projektzustand.

### 5.4 Manuelle Namen und Kollisionen

Manuelle Namen werden:

- getrimmt,
- dürfen nicht leer sein,
- projektweit case-insensitive eindeutig gehalten.

Damit bleiben spätere Tabellen, Reports und Ergebnisreferenzen eindeutig.

Wenn ein Benutzer einen zukünftigen Auto-Kandidaten manuell belegt, z. B. `F7`, überspringt die automatische Vergabe diesen Kandidaten später und läuft monoton weiter. Bereits übersprungene bzw. verbrauchte Kandidaten werden nicht wiederverwendet.

### 5.5 Flyouts

Support:

```text
Bezeichnung   [ A          ]
Typ           [ Festlager ▼]
Position X    [ 250        ] mm
```

PointForce:

```text
Bezeichnung   [ F1         ]
Position X    [ 500        ] mm
Kraft F       [ -1000      ] N
```

PointMoment:

```text
Bezeichnung   [ M1         ]
Position X    [ 500        ] mm
Moment M      [ 100        ] Nm
```

Keine zusätzliche Flyout-Überschrift.

Rename ist Teil derselben transaktionalen Draft-Session. Cancel/Escape verwirft auch die Namensänderung.

---

## 6. Neutrale Layout-Eingaben

`StationLayout` darf keine fachlichen Entity-Typen kennen.

Die aufrufende Desktop-Schicht reduziert die sichtbaren Objekte auf neutrale Anforderungen, sinngemäß:

```text
StationRequirement
- PhysicalX
- LeftExtent
- RightExtent
```

Mehrere Entities an exakt derselben Position werden vor dem Layout zu einer Station zusammengeführt. Ihre technischen Symbol-Bounds bilden die Union der erforderlichen horizontalen Ausdehnung.

Textlabels sind kein Bestandteil dieser Extents.

### 6.1 Zukunftserweiterung für Intervallobjekte

Die Layoutgrenze soll eine optionale neutrale Intervallanforderung zulassen:

```text
SpanRequirement
- StartPhysicalX
- EndPhysicalX
- MinimumScreenWidth
```

Die aktuelle PointForce-/PointMoment-/Support-Implementierung benötigt keine `SpanRequirement`.

Sie wird als Erweiterungspunkt vorgesehen, damit eine spätere sehr kurze Streckenlast eine minimale sichtbare Breite verlangen kann, ohne dass `StationLayout` den Typ `UniformDistributedLoad` kennen muss.

Die konkrete Verarbeitung nichtleerer SpanRequirements wird erst mit dem Streckenlast-Meilenstein verbindlich implementiert, sofern sie vorher nicht benötigt wird.

---

## 7. Stationslayout

### 7.1 Harte Invarianten

Für sortierte eindeutige Stationen:

```text
x0 < x1 < ... < xn
```

muss gelten:

```text
screen0 < screen1 < ... < screenn
```

Zusätzlich:

- x = 0 bleibt erster Balkenanker,
- x = L bleibt letzter Balkenanker,
- gleiche physikalische Station → gleicher Bildschirmanker,
- kein Layout darf die Stationsreihenfolge vertauschen,
- kein Layoutwert darf NaN oder Infinity werden.

### 7.2 Mindestabstand

Zwischen zwei benachbarten Stationen ergibt sich der technische Mindestabstand aus:

```text
RightExtent(left)
+ Clearance
+ LeftExtent(right)
```

`Clearance` ist eine zentrale Desktop-Konstante und entspricht ungefähr der sichtbaren Breite eines normalen Kraftpfeils.

Es wird keine willkürliche per-Entity-Konstante wie `50 px` verteilt über den Code verwendet.

Technische Symbolgrößen und Clearance werden zentral definiert.

### 7.3 Möglichst proportionale Verteilung

Seien:

```text
d_i = x_(i+1) - x_i
m_i = erforderlicher Mindest-Screenabstand
W   = verfügbare Screenbreite zwischen den Balkenendankern
```

Wenn die Mindestabstände in den Viewport passen, wird ein gemeinsamer Faktor `k` so gewählt, dass:

```text
s_i = max(m_i, k * d_i)
Σ s_i = W
```

Damit gilt:

- ohne aktive Mindestabstandsbegrenzung bleibt die Abbildung linear,
- zu kleine reale Abschnitte werden nur so weit wie nötig aufgeweitet,
- der verbleibende Platz wird weiterhin proportional zu den physikalischen Abständen verteilt.

`k` wird deterministisch bestimmt, z. B. per Active-Set oder monotoner Bisektion.

### 7.4 Endstationen und Außenränder

Symbole an x = 0 oder x = L dürfen nicht am Fensterrand abgeschnitten werden.

Der Interactive Beam Pane reserviert links und rechts ausreichend Außenraum:

```text
max(BaseSideMargin, endpoint symbol extent + outer padding)
```

Erst innerhalb dieser Endanker wird `W` für das Stationslayout bestimmt.

### 7.5 Overconstrained

Wenn:

```text
Σ m_i > W
```

ist das Layout `IsOverconstrained = true`.

Dann:

- Reihenfolge bleibt strikt erhalten,
- keine negativen Abstände,
- keine NaN/Infinity,
- Mindestabstände dürfen kontrolliert unterschritten werden,
- Verteilung bleibt deterministisch.

Als Best-Effort-Fallback werden die Mindestabstände proportional auf die verfügbare Breite skaliert.

Zoom/Pan wird dadurch nicht vorgezogen. Der Zustand wird jedoch explizit im Layoutresultat geführt und automatisiert getestet.

### 7.6 IsDistorted

`IsDistorted = true`, sobald mindestens ein sichtbarer Segmentabstand von der rein linearen Abbildung abweicht.

Dieser Zustand steuert den dezenten UI-Hinweis:

```text
Schematische Darstellung
```

Wenn die Abbildung linear bleibt, wird kein Hinweis gezeigt.

---

## 8. StationTransform

`StationLayoutResult` liefert eine immutable reversible Abbildung.

### 8.1 PhysicalToScreen

An Stationen wird der berechnete Stationsanker verwendet.

Für eine beliebige Position zwischen zwei Stationen wird stückweise linear interpoliert:

```text
x_i <= x <= x_(i+1)

t = (x - x_i) / (x_(i+1) - x_i)

screen(x) =
    screen_i
    + t * (screen_(i+1) - screen_i)
```

Dadurch können auch Positionen abgebildet werden, die selbst keine Station sind.

### 8.2 ScreenToPhysical

Die inverse Abbildung verwendet dasselbe Segment stückweise linear.

Damit bleiben möglich:

- Hover,
- Placement,
- Drag,
- Hit-Testing,
- spätere Intervallobjekte.

Clamping auf `[0, L]` bleibt eine explizite Interaktionsentscheidung des Callers und wird nicht versteckt in die Transformlogik eingebaut.

### 8.3 Keine zweite x-Wahrheit

Interactive Beam Pane und Coordinate Axis Pane verwenden dieselbe `StationTransform`.

Es darf keine separate Achsenskalierung geben.

---

## 9. Stabile Pointer-Interaktionen

Ein schematisches Layout darf sich während einer laufenden Mausgeste nicht selbst unter dem Pointer verschieben.

### 9.1 Snapshot-Regel

Beim Start von Placement bzw. Pointer-Drag wird ein immutable Layout-Snapshot verwendet.

Während der Geste:

- `screen → physical` ausschließlich über diesen Snapshot,
- Preview ebenfalls über diesen Snapshot,
- kein vollständiger Reflow aufgrund des gerade bewegten Preview-Objekts.

Nach Commit oder Cancel:

- neuer committed Zustand,
- neues `StationLayout`,
- kontrollierter Reflow.

### 9.2 Placement

Eine neue Preview-Station beeinflusst das committed Stationslayout während des Hover nicht.

Nach erfolgreichem Commit darf die Darstellung neu verteilt werden.

### 9.3 Drag

Auch beim Drag eines bestehenden Entity bleibt der Ausgangstransform für die gesamte Geste stabil.

Das gilt insbesondere, wenn:

- ein Entity eine gemeinsame Station verlässt,
- zwei Stationen durch den Drag zusammenfallen,
- eine nahe Station nach dem Commit entzerrt werden muss.

Bestehende Regeln für Snap, Endpoint-Clamp und transaktionalen Commit bleiben bestehen.

---

## 10. Aufteilung des Zeichenbereichs

`BeamEditorSurface` wird konzeptionell in zwei gekoppelte horizontale Bereiche geteilt.

```text
┌────────────────────────────────────────────┐
│                                            │
│        Interactive Beam Pane               │
│  Loads / Annotationen / Balken / Lager     │
│                                            │
├────────────────────────────────────────────┤
│        Coordinate Axis Pane                │
│  Axis / Ticks / Koordinaten / LengthEdit   │
└────────────────────────────────────────────┘
```

Praktisch bietet sich ein Avalonia-`Grid` mit:

- flexiblem oberen Row,
- automatisch hoher Coordinate-Axis-Row

an.

### 10.1 Interactive Beam Pane

Interaktiv:

- Symbol-Hit-Testing,
- Placement,
- Drag,
- Flyouts,
- Label-Drag.

### 10.2 Coordinate Axis Pane

Automatisiert und grundsätzlich read-only:

- Achse,
- Ticks,
- Koordinatenlabels,
- Distortion-Hinweis.

Einzige bewusste Interaktionsausnahme ist der rechte Balkenlängenwert.

Eine spätere Ein-/Ausblendfunktion des gesamten Pane bleibt möglich, wird aber jetzt nicht implementiert.

---

## 11. Coordinate Axis

### 11.1 Darstellung

Die bisherige klassische Gesamtlängenbemaßung entfällt.

Die Coordinate Axis zeigt:

- x = 0,
- jede eindeutige innere Station,
- x = L.

Beispiel:

```text
──────────────────────────────────── x [mm]
0       250       496   501        1000
```

Nur reale Stationen erhalten Ticks.

Es gibt:

- keine regelmäßigen Zwischen-Ticks,
- kein Raster,
- keine grafische Aussage „gleiche Pixel = gleiche reale Länge“.

Bei entzerrtem Layout sind die Zahlen maßgebend, nicht der optische Abstand.

### 11.2 Einheit

Im aktuellen Meilenstein bleibt die bestehende mm-Darstellung.

Die Achsenlogik wird jedoch so getrennt, dass der Label-Packer nur fertig formatierte Strings erhält. Eine spätere Einheitenwahl kann damit `1200` durch `1,2` ersetzen, ohne den Packing-Algorithmus zu ändern.

Es wird jetzt kein Settings-/Einheitensystem implementiert.

### 11.3 Axis-Titel

`x [mm]` ist eine feste Achsenannotation außerhalb des Stations-Label-Packings.

Sie erzeugt keine zusätzliche Label-Lane.

### 11.4 Balkenlänge

Der rechte Endwert ersetzt die bisherige sichtbare Längenbemaßung.

Normal:

```text
──────────────────────────────────── x [mm]
0       250       500              1000
```

Beim Edit:

```text
──────────────────────────────────── x [mm]
0       250       500             [1000]
```

Die vorhandene `LengthInput`-Semantik bleibt bestehen.

Während einer Edit-Session bleibt für das Eingabefeld eine stabile feste Breite reserviert. Tippen darf kein Axis-Reflow pro Tastendruck erzeugen.

---

## 12. Automatisches Packing der Koordinatenlabels

Koordinatenlabels werden vollständig automatisch angeordnet.

Sie sind nicht manuell verschiebbar.

### 12.1 Trennung von Textmessung und Packing

Avalonia misst zuerst den fertig formatierten Text.

Der reine Layoutalgorithmus erhält nur neutrale Eingaben, sinngemäß:

```text
AxisLabelRequirement
- StationAnchorX
- MeasuredWidth
- StableOrderKey
- EndpointRole
```

`AxisLabelPacker` kennt weder Avalonia-Controls noch Fonts noch Einheiten.

### 12.2 Horizontaler Label-Bounds

Für ein inneres Label:

```text
left  = anchorX - width / 2
right = anchorX + width / 2
```

plus zentral definiertem horizontalem Mindestpadding.

Endlabels werden innerhalb des sichtbaren Pane gehalten:

- `0`: nach innen clamped,
- `L`: nach innen clamped.

Der Tick selbst bleibt unverändert am Stationsanker.

### 12.3 Minimale Lane-Anzahl

Die fertigen horizontalen Labelintervalle werden nach ihrer linken Kante sortiert; Tie-Breaker ist die stabile Stationsreihenfolge.

Jedes Label wird in die niedrigste bereits existierende Lane gelegt, deren letztes belegtes Intervall inklusive Mindestpadding links vom neuen Intervall endet.

Nur wenn keine vorhandene Lane passt, wird eine neue Lane erzeugt.

Damit werden existierende Lanes konsequent wiederverwendet.

Beispiel bei passenden gemessenen Breiten:

```text
Stations: 1200, 1250, 1300, 1400

Lane 0:   1200        1300
Lane 1:        1250        1400
```

Eine dritte Lane ist hier nicht zulässig, wenn zwei kollisionsfreie Lanes genügen.

Dieses Intervall-Packing verwendet für die bereits festgelegten horizontalen Labelbounds die minimale notwendige Lane-Anzahl.

### 12.4 Dynamische Höhe

Die Pane-Höhe ergibt sich aus:

```text
AxisBaseHeight
+ LaneCount * AxisLabelLineHeight
+ Padding
```

Ein einfaches Modell bleibt kompakt.

Ein dichteres Modell darf zwei oder drei Lanes verwenden, wenn sie tatsächlich benötigt werden.

Es existiert keine feste Regel „maximal zwei Reihen“.

### 12.5 Stabilität

Gleiche Eingaben müssen unabhängig von Dictionary-/Collection-Iteration exakt dieselbe Lane-Belegung ergeben.

---

## 13. Punktkräfte

### 13.1 Lage

Alle Punktkraftsymbole befinden sich im Lastbereich oberhalb des Balkens.

Keine Punktkraft wird zur Kollisionsbehandlung unter den Balken verschoben.

Die Kraft greift an ihrer Station an. Das Symbol bezieht sich vertikal auf den oberen
Balkenrand und bleibt mit seiner gezeichneten Kontur 1 DIP darüber. Bei einer negativen
Kraft ist dies die Pfeilspitze, bei einer positiven Kraft das untere Schaftende.
Die Balkendicke ist zentral in `SchematicMetrics.BeamStrokeWidth` definiert (aktuell 3 DIP);
Pfeilposition, Hitbereich und Beschriftungsbereich berücksichtigen sie automatisch.
Die bisherige gestrichelte Verbindung zwischen Symbol und Balken entfällt.

### 13.2 Vorzeichen

Core bleibt verbindlich:

```text
positiv = nach oben
negativ = nach unten
```

Positive Kraft:

```text
      ↑
      │

───────────────
```

Negative Kraft:

```text
      │
      ↓

───────────────
```

Die Pfeillänge ist eine konstante Darstellung und nicht proportional zum Kraftbetrag.
Kraftpfeilspitzen sind gefüllte Dreiecke mit 1,5-DIP-Kontur und derselben Größe
wie die Momentpfeilspitzen.

### 13.3 Label

Beispiel:

```text
F1 = 1000 N
F2 = -500 N
```

Positive Werte erhalten kein sichtbares `+`.

Negative Werte behalten `-`.

Das Vorzeichen wird damit durch Zahl und technische Pfeilrichtung konsistent sichtbar.

### 13.4 Nullkraft

Null bleibt fachlich zulässig.

Ein Entity mit `0 N`:

- behält Station und Label,
- erzeugt keinen irreführenden Richtungs-Pfeilkopf,
- bleibt über Label und einen ausreichenden Stations-Hitbereich auswählbar.

---

## 14. Mehrere Punktkräfte an derselben Station

Mehrere PointForces bleiben immer getrennte Desktop- und Core-Entities.

Sie werden nicht zu einer resultierenden Kraft zusammengeführt.

### 14.1 Gleiche Richtung

Mindestens eine positive und keine negative Kraft:

```text
F1 = 1000 N
F2 = 500 N

      ↑
      │

───────────────
```

Es wird ein gemeinsamer Up-Glyph gezeichnet.

Mindestens eine negative und keine positive Kraft:

- ein gemeinsamer Down-Glyph.

### 14.2 Beide Richtungen

Positive und negative Kräfte an derselben Station:

```text
F1 = 1000 N
F2 = -500 N

      ↑
      │
      ↓

───────────────
```

Es entsteht ein gemeinsamer Doppelpfeil.

Keine zusätzlichen kleinen Richtungspfeile werden neben Labels ergänzt.

Nullkräfte beeinflussen die Richtungs-Glyphbildung nicht.

---

## 15. Punktmomente

### 15.1 Lage

Das Zentrum eines Moments liegt exakt auf der Balkenachse an seiner Station.

Das Moment wird nicht über eine Hilfslinie oberhalb des Balkens „aufgehängt“.

### 15.2 Grundsymbol

Das Momentzeichen ist ein technischer Kreisbogen über 280°.

Winkelkonvention: 12 Uhr = 0°, 3 Uhr = 90°, 6 Uhr = 180°, 9 Uhr = 270°.
Die positive Grundform beginnt bei 110° und verläuft gegen den Uhrzeigersinn über 280° bis 190°.
Das negative Moment ist die vollständige Spiegelung dieser Grundform an der senkrechten
Achse durch das Momentzentrum (`x → −x`). Sein Bogen verläuft im Uhrzeigersinn von 250° bis 170°.
Die gefüllte Pfeilspitze mit 1,5-DIP-Kontur wird ebenfalls gespiegelt; ihre Neigung zur
Bogentangente beträgt bei beiden Vorzeichen 16°. Kraft- und Momentpfeilspitzen verwenden
dieselbe Größe und denselben Öffnungswinkel.

Vorzeichenkonvention gemäß Core:

```text
positiv = gegen Uhrzeigersinn
negativ = im Uhrzeigersinn
```

Darstellung:

```text
negativ / clockwise:
Pfeilspitze bei 170°

positiv / counterclockwise:
Pfeilspitze bei 190°
```

Der Bogen darf den Balken kreuzen.

### 15.3 Mehrere Momente an derselben Station

Nur positive Momente:

- gemeinsamer 280°-Kreisbogen mit Pfeilspitze bei 190°.

Nur negative Momente:

- gemeinsamer gespiegelter 280°-Kreisbogen mit Pfeilspitze bei 170°.

Beide Drehrichtungen vorhanden:

- Vereinigung beider Bögen zu einem einzigen 340°-Kreisbogen,
- Pfeilspitzen gleichzeitig bei 170° und 190°,
- überlappende Bogenstücke werden nur einmal gezeichnet.

Einzelrichtungen verwenden 48 Liniensegmente; der gemeinsame 340°-Bogen verwendet 59.

Die einzelnen Momente bleiben separate Entities und separate Labels.

### 15.4 Label

```text
M1 = 100 Nm
M2 = -50 Nm
```

Positive Werte ohne `+`.

### 15.5 Nullmoment

Ein Nullmoment behält Station, Label und den neutralen 280°-Bogen der positiven Grundform,
erzeugt aber keinen irreführenden Richtungs-Pfeilkopf. An einer gemeinsamen Station beeinflussen
Nullmomente die Vereinigung der vorhandenen Richtungen nicht.

---

## 16. Lagerdarstellung

Fest- und Loslager teilen dieselbe Bodenlinie: 40 DIP lang, 2 DIP stark und
20 DIP unter der Balkenachse. Vier gleichmäßig verteilte Schraffurstriche mit
jeweils 9 × 9 DIP bei 45° verlaufen von dieser Linie nach links unten. Die
Schraffurgröße wird mit der Einspannung geteilt; ihre Strichanzahlen bleiben getrennt.

Beide Lager besitzen einen weiß gefüllten Gelenkkreis mit Radius 3 DIP direkt
auf der Balkenachse. Er wird zuletzt gezeichnet, damit die Balkenlinie und die
Dreiecksspitze seine weiße Fläche nicht durchziehen. Kreis, Dreiecksseiten,
Loslager-Basislinie und Schraffur verwenden 1,5 DIP Strichstärke.

Beim Festlager liegen die Dreiecksbasispunkte bei x = ±14 DIP und y = 20 DIP;
die Bodenlinie schließt das Dreieck. Beim Loslager ist das Dreieck auf 80 %
verkleinert: Basispunkte bei x = ±11,2 DIP und y = 16 DIP. Seine separate,
36 DIP lange Basislinie liegt ebenfalls bei y = 16 DIP, mit 4 DIP Abstand zur
Bodenlinie. Es werden keine zusätzlichen unteren Rollenkreise gezeichnet.

Eine gemeinsame, reine Desktop-Geometrie bestimmt Zeichnung, Symbolausdehnungen
und Hitbereiche für committed Lager sowie Placement-, Bearbeitungs- und
Drag-Vorschauen. Beide Lager reservieren inklusive Strichstärken 21 DIP je Seite.
Die sichtbaren vertikalen Grenzen liegen bei −3,75 und +29,75 DIP; mit dem
bestehenden Hit-Padding von 5 DIP gilt x = ±26 DIP und y = −8,75 bis +34,75 DIP.
Diese Lager werden an den Balkenenden nicht gespiegelt.

Die erste automatische Beschriftungszeile beginnt unverändert 40 DIP unter
der Balkenachse. Dieser Annotation-Anker ist unabhängig von der Bodenlinie;
manuelle AnnotationOffsets und die Pane-Höhenreservierung bleiben erhalten.

Die Einspannung besitzt eine 48 DIP hohe, 2 DIP breite Vertikale und fünf
Schraffurstriche mit jeweils 9 × 9 DIP bei 45° und der gemeinsamen Symbolstrichstärke
von 1,5 DIP. Ihre Ansatzpunkte liegen relativ zur Balkenachse bei −24, −14,25,
−4,5, 5,25 und 15 DIP. Der unterste Strich endet bei +24 DIP auf Höhe des Wandendes.

Bei exakt `x = L` wird die Einspannung horizontal gespiegelt: Die Schraffur liegt
rechts außerhalb des Balkens. Bei `x = 0` und im Balkeninneren liegt sie links.
Die Entscheidung verwendet die exakte physikalische Position ohne zusätzliche
Toleranz und gilt auch für Placement-, Bearbeitungs- und Drag-Vorschauen.
Eine Längenänderung bestimmt die Orientierung erneut anhand der committed Länge;
eine blockierte Längenänderung verändert die Orientierung nicht.

Layout und Außenränder verwenden die orientierten Symbolausdehnungen inklusive
Strichstärken: regulär links 9,75 / rechts 1 DIP, gespiegelt links 1 / rechts 9,75 DIP.
Der großzügige Symbol-Hitbereich wird ebenfalls horizontal gespiegelt.
Support-Bezeichnungen und ihre AnnotationOffsets werden nicht gespiegelt.

Jedes committed Lager zeigt zusätzlich seine stabile Bezeichnung:

```text
A
B
C
```

Die Bezeichnung wird nahe dem Support-Symbol automatisch positioniert und kann wie andere Entity-Annotationen später manuell verschoben werden.

Die Benennung ist Grundlage für spätere Ergebnisreferenzen wie:

```text
R_A
R_B
```

oder komponentenbezogene Varianten. Reaktionsdarstellung selbst gehört nicht in diesen Meilenstein.

---

## 17. Render-Reihenfolge an einer Station

Mehrere Entity-Arten dürfen dieselbe Station teilen.

Die x-Koordinate wird nicht künstlich getrennt.

Empfohlene deterministische Render-Reihenfolge:

1. Balken,
2. Support-Glyphs,
3. Moment-Glyphs,
4. Force-Glyphs,
5. Entity-Annotationen und Selection-/Hover-Zustände.

Damit bleiben Lastpfeile und Momentpfeilspitzen gegenüber dem Balken eindeutig sichtbar.

Die technischen Symbol-Bounds einer Station sind die Union aller sichtbaren Glyphs an dieser Station.

---

## 18. Entity-Annotationen

### 18.1 Zwei Zustände

Jede Annotation befindet sich entweder in:

```text
Auto
```

oder:

```text
ManualOffset(dx, dy)
```

`dx/dy` sind DIPs relativ zum jeweils aktuellen automatisch bestimmten Entity-Anker.

Es werden keine absoluten Canvas-Koordinaten gespeichert.

### 18.2 Auto-Position

Auto-Labels werden mit gemessenen Textbounds möglichst kollisionsarm verteilt.

Die Auto-Logik darf pragmatisch bleiben; sie muss nicht jeden theoretischen Extremfall global optimal lösen.

Sie soll:

- deterministisch sein,
- Labels derselben Station geordnet darstellen,
- offensichtliche Überschneidungen vermeiden,
- manuell gesetzte Labels als feste Hindernisse respektieren.

### 18.3 ManualOffset

Der Benutzer darf ein Entity-Label direkt ziehen.

Dabei ändert sich nur `EditorPresentationState`.

Nicht geändert werden:

- Entity-Position,
- Kraft/Moment,
- SupportType,
- BeamModel,
- Analysis.

Ein manueller Offset bleibt bei:

- Resize,
- Station-Reflow,
- schematischer Entzerrung

relativ zum aktuellen Entity-Anker erhalten.

Zwei manuell übereinandergelegte Labels werden nicht automatisch wieder getrennt.

Eine spätere Aktion „Beschriftung zurücksetzen“ kann `ManualOffset` auf `Auto` zurücksetzen; ein eigener UI-Befehl dafür ist in diesem Meilenstein nicht zwingend.

---

## 19. Hit-Testing und Mehrdeutigkeit

### 19.1 Label

Ein einzelnes Entity-Label gehört eindeutig zu genau einem Entity.

- Klick → Entity öffnen/selektieren,
- Drag → nur Annotation verschieben.

### 19.2 Eindeutiges Symbol

Repräsentiert ein Glyph genau ein Entity:

- Klick/Drag verhält sich wie bisher und bearbeitet dieses Entity.

### 19.3 Gemeinsames Symbol

Repräsentiert ein Glyph mehrere Entities derselben Station, darf kein Entity willkürlich gewählt werden.

Regel:

- ist bereits genau eines der repräsentierten Entities selektiert, darf der gemeinsame Glyph dieses Entity ziehen,
- ohne eindeutige Auswahl öffnet der Glyph eine kleine, objektbezogene Auswahl der Entities an dieser Station,
- die Auswahl verwendet stabile Labels und Creation-/ID-Reihenfolge,
- kein allgemeiner Inspector wird eingeführt.

Während ein ausgewähltes Entity aus einer gemeinsamen Station weggezogen wird, bleiben die anderen Entities committed an der alten Station. Der Drag verwendet den eingefrorenen Transform-Snapshot.

---

## 20. Coordinate Axis und Entity-Annotationen sind getrennte Systeme

Coordinate-Axis-Labels:

- vollautomatisch,
- nicht per Maus verschiebbar,
- minimale dynamische Lane-Anzahl,
- reine Koordinateninformation.

Entity-Annotationen:

- automatische Ausgangslage,
- optionaler ManualOffset,
- gehören zu konkreten Supports/Loads.

Es wird kein gemeinsamer „General Annotation Manager“ gebaut, der beide Probleme vermischt.

---

## 21. Balkenlängenbearbeitung und Conflict Preview

Die bestehende transaktionale LengthInput-Semantik bleibt fachlich unverändert.

### 21.1 Erfolgreiche Änderung

Bei gültigem Commit:

- neues `EditorDocument`,
- neues StationLayout,
- bei tatsächlicher mechanischer Änderung genau eine Analysis.

### 21.2 Blockierte Verkürzung

Wenn die gewünschte Länge ein committed Entity außerhalb des neuen Endes zurücklassen würde:

- kein Dokument-Commit,
- keine Analysis,
- blockierende Supports/PointForces/PointMoments werden Error-markiert,
- committed StationTransform bleibt während der Edit-Session stabil.

Die gewünschte physikalische Länge wird über den vorhandenen Transform stückweise auf Screen-X abgebildet.

Visuell:

- massiver Balken bis zur RequestedLength,
- Ghost-Segment von RequestedLength bis zur committed Länge,
- transienter Endmarker an RequestedLength,
- keine zweite konkurrierende Koordinatenzahl erforderlich; der aktuelle Requested-Wert steht bereits im LengthInput.

Die Axis-Ticks der committed Entities bleiben an ihren committed Stationen.

### 21.3 Rejected Enter

- Editor bleibt aktiv,
- Error bleibt sichtbar,
- Conflict Preview bleibt sichtbar,
- keine Analysis.

### 21.4 Rejected Focus Loss

- committed Länge wiederherstellen,
- Error entfernen,
- Conflict entfernen,
- Ghost/Highlight entfernen,
- keine Analysis.

### 21.5 Kein Reflow pro Tastendruck

Der committed Transform wird während der laufenden Längeneingabe nicht anhand des transienten RequestedLength vollständig neu gelayoutet.

Ein erfolgreicher Commit erzeugt erst danach ein neues StationLayout.

---

## 22. Resize und Reflow

Bei Resize:

1. neue verfügbare Screenbreite bestimmen,
2. StationLayout aus denselben physikalischen Stationen neu berechnen,
3. Coordinate-Axis-Labels neu messen/packen, falls erforderlich,
4. Auto-Entity-Labels neu anordnen,
5. ManualOffsets relativ zu ihren neuen Ankern beibehalten.

Physikalische Koordinaten und Entity-Reihenfolge ändern sich nie durch Resize.

---

## 23. Determinismus

Alle Layoutverfahren benötigen stabile Tie-Breaker.

Nicht zulässig:

- Dictionary-Iteration als sichtbare Reihenfolge,
- zufällige Reihenfolge gleichrangiger Labels,
- von Hashcodes abhängige Auswahl.

Verwendet werden:

1. physikalische Station,
2. stabile Creation-/Document-Reihenfolge,
3. stabile Entity-ID als letzter Tie-Breaker.

Gleiche Dokumentdaten und gleiche Viewportgröße müssen dasselbe Layout erzeugen.

---

## 24. Spätere Streckenlast

Diese Spezifikation implementiert noch keine Streckenlast.

Sie legt jedoch die Architektur so fest, dass der nächste Meilenstein ohne erneuten Grundumbau anschließen kann.

Eine spätere UDL besitzt:

```text
q1
StartPosition
EndPosition
signed Intensity
```

Start und Ende werden normale Stationen.

Eine sehr kurze UDL kann über die neutrale `SpanRequirement` eine minimale sichtbare Breite verlangen.

Die sichtbare Anzahl der UDL-Pfeile ist rein grafisch. Sie darf nicht als Integration oder resultierende Kraft interpretiert werden.

---

## 25. Spätere segmentierte Balken / Wellen

Eine spätere Erweiterung kann einen Balken aus Abschnitten mit unterschiedlichen Querschnitten aufbauen, z. B.:

```text
0–100 mm      Ø80
100–500 mm    Ø100
500–L         Ø90
```

Querschnittsgrenzen können dann lediglich zusätzliche Stationen erzeugen.

Dafür wird jetzt nicht implementiert:

- `BeamSegment`,
- variables `A`,
- variables `I`,
- variables `E`,
- Solverlogik.

Die einzige heutige Vorbereitung ist die Entity-unabhängige StationLayout-Schnittstelle.

---

## 26. Tests

### 26.1 StationLayout

Abdecken:

- lineare Darstellung bei ausreichendem Platz,
- Aktivierung eines Mindestabstands,
- mehrere aktive Mindestabstände,
- physikalische Reihenfolge bleibt erhalten,
- exakt gleiche x → eine Station,
- fast gleiche x → unterschiedliche Stationen,
- unterschiedliche Left-/RightExtents,
- Endstation-Extents,
- `PhysicalToScreen`,
- `ScreenToPhysical`,
- Roundtrip innerhalb numerischer Genauigkeit,
- `IsDistorted`,
- `IsOverconstrained`,
- deterministischer Best-Effort-Fallback,
- keine NaN/Infinity.

### 26.2 AxisLabelPacker

Abdecken:

- ein Label pro Station,
- keine doppelten Labels bei gemeinsamer Station,
- gemessene unterschiedliche Labelbreiten,
- kompakte Lane-Wiederverwendung,
- `1200 / 1250 / 1300 / 1400` benötigt bei passenden Bounds genau zwei Lanes,
- dritte Lane nur, wenn zwei mathematisch nicht genügen,
- Endlabel-Clamping,
- dynamische Pane-Höhe,
- deterministische Tie-Breaker.

Culture-/Unit-Tests formatieren vor dem Packer und beweisen, dass lange und kurze Strings korrekt gemessen und danach gepackt werden.

### 26.3 Naming

Abdecken:

- A/B/C/.../Z/AA,
- F1/F2/F3,
- M1/M2/M3,
- Cancel verbraucht Auto-Kandidat nicht,
- erfolgreicher Commit verbraucht Kandidat auch nach Rename,
- Delete verwendet Auto-Namen nicht erneut,
- Rename verändert Counter nicht rückwärts,
- zukünftiger manuell belegter Auto-Kandidat wird übersprungen,
- projektweite case-insensitive Eindeutigkeit,
- Setup erhält Namen und Zählerzustand,
- Rename-only erzeugt keine Analysis.

### 26.4 Force-/Moment-Rendering-State

Abdecken:

- positive Force → Up,
- negative Force → Down,
- positive + negative an gleicher Station → Double,
- mehrere gleichgerichtete Forces → gemeinsamer Glyph,
- Nullkraft beeinflusst Richtungsglyph nicht,
- Kraftkontur hält 1 DIP Abstand zum oberen Balkenrand; Schaftlänge bleibt konstant,
- Kraft-Hitbereiche und automatische Labels berücksichtigen den Randabstand,
- Kraft- und Momentpfeilspitzen bleiben gefüllt und behalten die gemeinsame Strichstärke,
- positives Moment → ArrowHead 190°,
- negatives Moment → gespiegelter Bogen und ArrowHead 170°,
- beide Momentvorzeichen → gemeinsamer 340°-Bogen und beide ArrowHeads,
- Nullmoment beeinflusst Richtungsglyph nicht.

Die Tests sollen nach Möglichkeit Rendering-State/Geometry prüfen, nicht Pixel-Screenshots.

### 26.5 Interaktion

Abdecken:

- Placement verwendet stabilen Snapshot,
- Drag verwendet stabilen Snapshot,
- Commit erzeugt anschließend Reflow,
- Label-Drag verändert keine physikalische Position,
- Label-Drag erzeugt keine Analysis,
- ManualOffset bleibt bei Resize/Reflow erhalten,
- Symbol-Drag verändert Position wie bisher,
- shared glyph wählt nicht willkürlich ein Entity,
- Length Conflict bleibt ohne Analysis,
- rejected Focus Loss stellt committed Zustand vollständig wieder her.

---

## 27. Native GUI-Abnahme

Nach automatisierten Tests einmal gezielt nativ prüfen:

1. weit auseinanderliegende Stationen → praktisch lineare Darstellung,
2. zwei sehr nahe Stationen → sichtbare Entzerrung,
3. zwei Kräfte gleicher Richtung an derselben Station,
4. positive und negative Kraft an derselben Station,
5. positiver und negativer Moment,
6. beide Momentrichtungen an derselben Station,
7. Support-Bezeichnungen A/B/C,
8. Force-/Moment-Bezeichnungen,
9. Coordinate Axis mit einer Lane,
10. Coordinate Axis mit zwei Lanes,
11. Fall, der tatsächlich drei Lanes benötigt,
12. `1200/1250/1300/1400` kompakt mit zwei Lanes,
13. Force-/Moment-Label per Maus verschieben,
14. Symbol-Drag getrennt vom Label-Drag,
15. Länge über rechten Axis-Endwert bearbeiten,
16. rejected Length Conflict,
17. Resize und Reflow.

Nur konkrete gefundene Fehler iterativ korrigieren; keine unnötige Screenshot-Schleife.

---

## 28. Regression Gates

Nach Implementierung vollständig ausführen:

- Product Restore,
- Product Release Build,
- alle Produkttests,
- Validation Restore,
- Validation Release Build,
- alle Validation-Tests,
- Acceptance 18/18,
- SolverSourceSha256 unverändert,
- 0 Warnungen,
- 0 Fehler.

Core, Solver, Engineering und Analysis werden durch diesen UI-Meilenstein nicht geändert.

---

## 29. Architekturentscheidungen in Kurzform

Verbindlich sind insbesondere:

- physikalisches Modell und Screenlayout bleiben getrennt,
- schematische Entzerrung verändert niemals reale Werte,
- `StationLayout` ist pure und Entity-unabhängig,
- `StationTransform` ist stückweise linear und reversibel,
- technische Symbol-Bounds bestimmen Stationsmindestabstände; Texte nicht,
- Interactive Beam Pane und Coordinate Axis Pane teilen exakt denselben Transform,
- Coordinate-Axis-Labels werden minimal in dynamische Lanes gepackt,
- Object Labels sind getrennte, optional manuell verschiebbare Annotationen,
- Namen sind stabile Projektmetadaten und werden nicht nach Delete umnummeriert,
- Rename und Label-Movement lösen keine Analysis aus,
- Pointer-Gesten verwenden immutable Layout-Snapshots,
- gemeinsame Stationsglyphs dürfen keine zufällige Entity-Auswahl verursachen,
- extreme Dichte wird erkannt, aber Zoom/Pan wird nicht vorgezogen,
- zukünftige UDL-/Querschnittsgrenzen können die gleiche Stationsarchitektur nutzen.

Diese Regeln bilden die verbindliche grafische Grundlage für die nächsten SpanDraft-Desktop-Meilensteine.
