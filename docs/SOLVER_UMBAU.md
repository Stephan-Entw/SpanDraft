# SpanDraft – Plan Solver-Umbau

Stand: 9. November 2026. Dieser Plan gilt für den Branch `stantipin/solver-umbau`.
Der Branch bleibt von `main` und von der Webseite getrennt, bis der Umbau
fachlich abgenommen und die Übernahme ausdrücklich entschieden ist.

## Ziel

Der Solver soll Positionen, die nur Nanometer auseinanderliegen (zum Beispiel aus
CAD), ohne Ablehnung und ohne stillen Fehler rechnen. Zugleich soll er die
geplanten Balkenfeatures tragen, ohne dass eines davon das Abstandsproblem
zurückbringt. Wo ein Modell nicht sicher lösbar ist, lehnt der Solver mit einer
eindeutigen Meldung ab, statt ein falsches Ergebnis zu liefern.

## Umfang

**Enthalten**, jeweils nur auf Solverseite:

| Phase | Inhalt | Issue |
|---|---|---|
| 1 | Neuer Rechenweg für den heutigen Funktionsumfang | – |
| 2 | Innere Gelenke | DEV-20 |
| 3 | Weg- und Drehfedern | DEV-19 |
| 4 | Mehrsegment-Balken (EI und EA je Abschnitt) | DEV-11 |
| 5 | Linear veränderliche Streckenlasten | DEV-6 |
| 6 | Axiale und schräge Einzelkräfte | DEV-12 |

**Nicht enthalten:**

- Bedienelemente, Editor, Zeichnung, Bericht, Hilfe und Projektdateiformat. Sie werden separat geplant.
- Temperaturbelastungen (DEV-27). Der Prototyp enthält eine eingeprägte Krümmung als Machbarkeitsprobe;
  sie wird in Phase 1 entfernt.
- Lagersenkungen, Schubspannung (DEV-17) und Vergleichsspannung (DEV-18).
- Änderungen an der technischen Dokumentation der Webseite. Sie folgen nach der Übernahme in `main`.

## Ausgangslage

Der Prototyp liegt im ersten Commit dieses Branches. Er ersetzt den Rechenweg,
lässt aber die öffentliche Schnittstelle (`BeamSolution`, `EvaluateAt`, `Extrema`)
unverändert. Die neuen Features erreicht er nur über interne Solver-Eingaben
(`SolverLoads`), weil Core dafür noch keine Typen hat.

Rechenweg in Kürze:

- Gleichungen nur an Lagern; unbekannt sind die Drehwinkel nicht eingespannter Lager.
- Lasten wirken über geschlossene Formeln an ihrer genauen Position.
- Querkraft und Moment folgen aus dem Gleichgewicht.
- Gelenke, Federn und Querschnittswechsel schneiden den Balken. Unbekannt sind die übertragenen Kräfte und Momente, nie Durchbiegungen.
- Steife, eng benachbarte Federn werden in Newton-Form gerechnet, mit geteilten Differenzen ohne Auslöschung.
- Instabile Systeme erkennt eine exakte Rangprüfung in Bruchrechnung.

Gemessen gegen zwei unabhängige exakte Referenzen in Bruchrechnung
(Werkzeuge und Protokolle in `validation/investigations/solver-rework/`):

| Bereich | Ergebnis |
|---|---|
| Lasten ohne Knoten | 35 CAD-Fälle und 11.600 Zufallsmodelle ohne Ablehnung, höchstens 7·10⁻⁷ |
| Gelenke | 81 gezielte Fälle höchstens 5·10⁻¹⁶, Zufallsmodelle bis L/10⁹ höchstens 3·10⁻⁸ |
| Federn | 270 Extremfälle höchstens 5·10⁻¹⁰, 6.000 Zufallsmodelle höchstens 2·10⁻⁷ |
| EI-Wechsel, Linearlasten | Gezielte Fälle bis 6 nm 10⁻¹⁶ |
| Alles gemischt | 4.000 Zufallsmodelle, eines mit 9·10⁻⁶, sonst höchstens 10⁻⁸ |
| Stabilität | In allen Zufallsläufen gleich entschieden wie die exakte Rechnung |
| Regression | Alle Produkttests, Validierungstests und Abnahme 18/18 bestanden |

Bekannte Lücken des Prototyps:

- **Ablehnungen trotz Lösbarkeit:** Lösbare Modelle mit sehr unterschiedlich steifen Federn im Nanometerabstand werden selten abgelehnt (5 von 6.000 bzw. 13 von 4.000 Zufallsmodellen).
- **Testlücken:** Die Produkttests finden 17 von 26 eingebauten Fehlern im neuen Code; die übrigen neun nur die exakten Vergleiche.
- **Plattform:** Gemessen nur auf macOS.
- **Altlasten:** Der alte FE-Pfad (`BeamAssembly`, `EulerBernoulliElement`) liegt noch im Code; der Solver-Hash in der CI ist nicht angepasst.

## Vorgehen je Phase

Jede Phase endet mit denselben Abnahmekriterien:

1. Unit-Tests mit von Hand gerechneten Sollwerten für jede neue Größe.
2. Vergleich gegen die exakte Referenz für gezielte Grenzfälle und Zufallsmodelle, ohne Abweichung über 10⁻⁹ (relativ zur Feldskala) und ohne Absturz.
3. Externe Referenz, wo möglich PyCBA oder eine analytische Lösung, als Abnahmefall in `validation/`.
4. Eingebaute Fehler im neuen Code: jeder wird von Tests oder Abnahme gefunden, nicht nur von den Untersuchungsskripten.
5. Produkttests, Validierungstests und Abnahme grün, unter macOS und Windows.
6. `docs/SOLVER.md`, `docs/VALIDATION.md` und betroffene Fachdokumente aktualisiert.
7. Fachliche Prüfung laut `docs/KONZEPT.md`.

### Phase 0: Sicherung (erledigt mit diesem Commit)

Prototyp, Prüfwerkzeuge, Ergebnisse und dieser Plan liegen auf dem Branch.

### Phase 1: Neuer Rechenweg für den heutigen Funktionsumfang

- Prototyp aufräumen: `SupportSpanSolver.cs` in verständliche Dateien teilen und die Temperaturkrümmung entfernen.
- Interne Eingaben für Features späterer Phasen hinter klare interne Schnittstellen legen.
- Alten FE-Pfad entfernen oder bewusst als Testorakel behalten (Entscheidung 1).
- Fehlercodes prüfen: `IllConditionedSystem` ist ohne Gelenke und Federn nicht mehr erreichbar.
- Exakte Referenz als Werkzeug in `validation/` übernehmen und die Abnahme um Fälle mit sehr nahen Positionen erweitern, darunter das CAD-Beispiel 100,54 mm / 100,540006 mm.
- Solver-Hash in `.github/workflows/ci.yml` anpassen.
- `SOLVER.md` und `VALIDATION.md` auf den neuen Rechenweg umschreiben.

### Phase 2: Gelenke (DEV-20)

- Entscheidungen 2 und 3 umsetzen (Moment genau auf dem Gelenk, Gelenk auf Einspannung).
- Drehwinkel am Gelenk links und rechts auch in den Knotenergebnissen bereitstellen.
- Fehlercodes für Mechanismus und Fast-Mechanismus mit klarer Ursache.
- Abnahmefälle mit PyCBA und analytisch (IndeterminateBeam kennt keine Gelenke).

### Phase 3: Federlager (DEV-19)

- Weg- und Drehfeder, Teileinspannung, Federn mit Axialhalt, Balken nur auf Federn.
- Gruppenbildung steifer Federn (Grenze 5 % der Teillänge, k·L³/EI ≥ 1) prüfen und festlegen.
- Ablehnungen trotz Lösbarkeit untersuchen und möglichst beseitigen.
- Fehlende Tests ergänzen: Newton-Gewichte dritter Ordnung, Drehfeder-Dipol, Starrkörperdrehung in Gruppenzeilen, Drehfeder an einem Querschnittswechsel.

### Phase 4: Mehrsegment-Balken (DEV-11)

- EI je Abschnitt (im Prototyp vorhanden), EA je Abschnitt für den Axialanteil (noch offen).
- Fehlender Test: mehrere Wechsel hintereinander.

### Phase 5: Linear veränderliche Lasten (DEV-6)

- Im Prototyp vorhanden, einschließlich Querkraftextremum im Feld bei Vorzeichenwechsel.
- Fehlende Tests: Formeln jenseits einer kurzen Dreieckslast, zugeschnittene Trapezlast, Feldpolynom fünften Grades.

### Phase 6: Axiale und schräge Einzelkräfte (DEV-12)

- Axialanteil im Solver vorhanden und entkoppelt (Theorie 1. Ordnung).
- Lasttyp, Normalkraftextrema in der Ergebnisschnittstelle, Abnahmefälle.

## Offene Entscheidungen

1. **Alter FE-Pfad:** Entfernen, oder als unabhängiges Testorakel für Modelle mit normalen Abständen behalten?
2. **Punktmoment genau auf einem Gelenk:** Ablehnen (Prototyp) oder Seite festlegen?
3. **Gelenk genau auf einer Einspannung:** Ablehnen (Prototyp)?
4. **Core-Modelltypen:** Gehören Gelenk-, Feder- und Lasttypen samt Prüfung in Core zu diesem Branch (ohne Bedienung und Dateiformat), oder bleiben die Features bis zur Bedienplanung solverintern? Die Abnahmefälle in `validation/` brauchen eine Eingabeform.
5. **Hinweis bei sehr nahen Positionen** (Vorschlag L/1000): Liefert der Solver dafür Diagnosedaten, die die Oberfläche später anzeigt?

## Folgen außerhalb des Solvers

- **Querkraft zwischen eng benachbarten Lagern:** Sie wird physikalisch riesig (6 nm Abstand: 31 GN). Diagramm und eine spätere Schubspannung (DEV-17) brauchen dafür eine eigene Darstellung.
- **Technische Dokumentation der Webseite:** Sie beschreibt das direkte Steifigkeitsverfahren und muss nach der Übernahme überarbeitet werden.
