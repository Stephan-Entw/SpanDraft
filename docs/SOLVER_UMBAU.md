# SpanDraft – Solver-Umbau

Stand: 9. November 2026, fachlich freigegeben als Grundlage für den
Architekturentwurf der Ebene A. Maßgebliches Planungsdokument für den Branch
`features/solver-umbau`. Der Branch bleibt von `main` und von der Webseite
getrennt, bis die Übernahme ausdrücklich entschieden ist.

Dieses Dokument legt fachliche Anforderungen, Architekturgrenzen und Entscheidungen
fest. Es legt bewusst keine Klassenstrukturen oder Core-Typen fest.

## Grundsatz

Der Branch entwickelt den zukünftigen SpanDraft-Solver als zusammenhängende
Architektur. Die geplanten Features DEV-20, DEV-19, DEV-11, DEV-6 und DEV-12
werden beim Design von Anfang an berücksichtigt und dürfen im Solver bereits
implementiert werden. Jedes Feature behält seine eigene fachliche Abnahme und
wird erst als Produktfunktion freigegeben, wenn sein vollständiger Vertrag
erfüllt ist.

Die Planung hat drei Ebenen:

| Ebene | Gegenstand | Ergebnis |
|---|---|---|
| **A. Solver-Architektur** | Gemeinsamer Rechenkern | Produktionsreifer Solver, der den heutigen Funktionsumfang ersetzt |
| **B. Feature-Tracks** | DEV-20, DEV-19, DEV-11, DEV-6, DEV-12 | Fachliche Anforderungen, die der Rechenkern erfüllen muss |
| **C. Integration und Freigabe** | Ein Feature als Produktfunktion | Freigabe erst nach vollständigem Issue-Vertrag |

## Ausgangslage: Machbarkeitsnachweis

Der erste Commit dieses Branches enthält einen Prototyp. Er ist ein
Machbarkeitsnachweis, kein festgelegtes Architekturdesign. Seine Strukturen
(`SupportSpanSolver`, `SolverLoads`, Multipol-Gruppen, heutige Typgrenzen) dürfen
hinterfragt und ersetzt werden.

Der Prototyp zeigt, dass ein Rechenweg mit diesen Eigenschaften möglich ist:

- Unbekannte entstehen nur dort, wo die Mechanik sie verlangt: Drehwinkel an Lagern, Schnittkräfte an Gelenken und Abschnittswechseln, Federkräfte und Starrkörperbewegungen.
- Lasten wirken über geschlossene Formeln an ihrer Position. Reine Last- oder Ergebnisstationen erzeugen keine zusätzlichen Steifigkeitselemente oder Solver-Freiheitsgrade; mechanisch relevante Abstände dürfen dagegen in den Gleichungen auftreten.
- Querkraft und Moment folgen aus dem Gleichgewicht.
- Steife, eng benachbarte Federn bleiben durch eine äquivalente Umformung der Unbekannten genau (Newton-Form mit geteilten Differenzen).
- Instabile Systeme erkennt eine exakte Rangprüfung ohne Toleranz.

Gemessen gegen zwei unabhängige exakte Referenzen in Bruchrechnung (Werkzeuge
und Protokolle in `validation/investigations/solver-rework/`). Abweichungen sind auf
die jeweilige Feldskala bezogen:

| Problemklasse | Beobachtete größte Abweichung |
|---|---|
| Lasten ohne Knoten, 35 CAD-Fälle, 11.600 Zufallsmodelle | 7·10⁻⁷ (zwei Lager L/10¹¹ auseinander); CAD-Fälle 5·10⁻¹⁶ |
| Gelenke, 81 gezielte Fälle | 5·10⁻¹⁶ |
| Gelenke, Zufallsmodelle bis L/10⁹ | 3·10⁻⁸ |
| Federn, 270 Extremfälle bis 6 nm und k·L³/EI bis 10¹² | 5·10⁻¹⁰ |
| Federn, 6.000 Zufallsmodelle | 2·10⁻⁷ |
| EI-Wechsel und Linearlasten, gezielte Fälle bis 6 nm | 10⁻¹⁶ |
| Alle Features gemischt, 4.000 Zufallsmodelle | 9·10⁻⁶ in einem Modell, sonst 10⁻⁸ |

Die Werte sind Beobachtungen, keine Zusage (siehe A8).

- **Ablehnungen trotz Lösbarkeit:** Lösbare Modelle mit sehr unterschiedlich steifen Federn im Nanometerabstand werden selten abgelehnt (5 von 6.000 beziehungsweise 13 von 4.000 Zufallsmodellen).
- **Testlücken:** Die Produkttests finden 17 von 26 eingebauten Fehlern im neuen Code.
- **Plattform:** Gemessen wurde nur auf macOS.

## A. Solver-Architektur

### A1 Stationen und Unbekannte

- **Stationen** sind alle Positionen, an denen sich ein Ergebnis ändern kann: Lager, Gelenke, Federn, Abschnittswechsel, Punktlasten, Grenzen verteilter Lasten und Balkenenden. Sie bestimmen, wo Felder stückweise definiert sind, wo links und rechts unterschieden wird und wo Ergebnisse ausgegeben werden.
- **Unbekannte** sind nur die mechanisch nötigen Größen. Eine Station ist nicht automatisch ein Freiheitsgrad.
- **Keine künstliche Steifigkeit:** Die bloße Existenz einer Ergebnis- oder Ereignisstation darf keine künstliche Steifigkeit und keine zusätzlichen schlecht konditionierten Freiheitsgrade erzeugen. Insbesondere darf eine Lastposition nicht allein deshalb zu einem Balkenelement mit Steifigkeitstermen wie EI/a³ führen.
- **Zulässige inverse Abstände:** Inverse Abhängigkeiten von physikalischen Abständen sind zulässig, wenn sie aus dem mechanischen Modell oder einer mathematisch äquivalenten Transformation entstehen und numerisch stabil ausgewertet werden. Beispiele sind Reaktionen zweier naher Lager, geteilte Differenzen und linear veränderliche Lasten.

### A2 Positionen

- **Keine Mindestdistanz:** Es gibt kein Snapping und kein automatisches Zusammenführen verschiedener Koordinaten.
- **Gleich oder verschieden:** Zwei Koordinaten, die in der internen Koordinatendarstellung des Fachmodells numerisch verschieden sind, bleiben zwei verschiedene physikalische Positionen. Der Solver rundet, snappt oder vereinigt sie nicht zusätzlich. Gleich ist eine Position nur bei exakt gleicher interner Zahl. Was beim Einlesen auf dieselbe interne Zahl fällt, kann der Solver nicht mehr unterscheiden.
- **Gleiche Position, verschiedene Objekte:** Welche Kombinationen an exakt derselben Position zulässig sind und was sie bedeuten, legt jeder Feature-Track fachlich fest (siehe B). Nicht festgelegte Kombinationen werden als nicht eindeutig definiert abgelehnt und nicht implizit interpretiert.

### A3 Mechanisches Modell und Numerik

- **Kein Glätten:** Große Gegenreaktionen oder Querkräfte zwischen eng benachbarten Lagern oder Federn können physikalisch korrekt sein. Der Solver gibt sie unverändert aus.
- **Erlaubte Stabilisierung:** Zulässig sind nur Maßnahmen, die die numerische Darstellung verbessern und mathematisch äquivalent sind, etwa Basiswechsel der Unbekannten, Skalierung, lokale Auswertung oder auslöschungsfreie Formeln.
- **Verboten:** Numerisch eingeführte Penalty-Federn oder Ersatzsteifigkeiten zur Approximation eigentlich starrer Zwangsbedingungen sowie andere Maßnahmen, die das mechanische Modell verändern, etwa Regularisierung oder das Verschieben von Positionen. Federlager, die der Anwender modelliert, sind davon nicht betroffen; sie sind Teil des Modells.

### A4 Gemeinsame Mechanik

Ein Rechenkern trägt alle Elemente:

| Element | Wirkung |
|---|---|
| Starre Lager | Weg- und Drehbehinderung |
| Innere Gelenke | Momentenfreigabe |
| Federn | Translatorisch, rotatorisch und kombiniert |
| Abschnittswechsel | Wechsel von EI und EA |
| Lasten | Punktkraft, Punktmoment, konstante und linear veränderliche Streckenlast |
| Axialanteile | Axiale Kräfte und Lasten, Theorie 1. Ordnung, von der Biegung entkoppelt |

Neue Elemente sollen sich einfügen, ohne den Kern umzubauen. Der Prototyp setzt
das als Schnittverfahren um: Gelenke, Federn und Abschnittswechsel schneiden den
Balken, die übertragenen Größen sind Unbekannte. Ob das die Zielform ist, wird
im Architekturentwurf entschieden.

### A5 Stabilität, Kondition und Fehler

Drei Fälle werden getrennt:

| Fall | Erkennung | Folge |
|---|---|---|
| **Mechanismus** | Exakt, ohne Toleranz | Ablehnung |
| **Numerisch nicht sicher lösbar** | Aus der tatsächlichen numerischen Qualität (z. B. Kondition, Rückwärtsfehler, Fehlerschätzung), nicht aus geometrischer Nähe | Ablehnung, kein Ergebnis |
| **Unzulässige oder nicht definierte Eingabe** | Bei der Eingabeprüfung | Ablehnung |

- **Meldungen** nennen die Ursache so, dass die Oberfläche später eine verständliche Erklärung geben kann. Fehlercodes werden dafür einheitlich festgelegt.
- **Grundsatz:** Kein stilles Ergebnis außerhalb des numerischen Vertrags (A8).
- **Offen:** Die heutige Konditionsgrenze lehnt einzelne lösbare Modelle ab. Das Kriterium soll so verbessert werden, dass es weniger lösbare Modelle ablehnt und trotzdem kein falsches Ergebnis durchlässt.

### A6 Felder und Extremwerte

- **Stückweise analytisch:** w, θ, V, M, u und N sind zwischen Stationen geschlossene Polynome und werden nicht abgetastet.
- **Links und rechts:** An Sprüngen werden beide Seiten unterschieden: V an Punktlasten und Lagern, M an Punktmomenten, θ an Gelenken. Am Gelenk muss die Ergebnisschnittstelle beide Drehwinkel liefern.
- **Extremwerte** werden aus der analytischen Feldbeschreibung bestimmt und nicht durch Abtastung gesucht, auch im Feldinneren, einschließlich des Querkraftextremums bei linear veränderlicher Last. Die erforderlichen Nullstellen werden mit kontrollierter numerischer Genauigkeit bestimmt.
- **Normalkraft:** Für ihre Extrema wird über die Ergebnisschnittstelle mit DEV-12 entschieden.

### A7 Validierungsstrategie

Bausteine:

- **Exakte Referenzen in Bruchrechnung:** Mindestens zwei unabhängige Verfahren, die in exakter Bruchrechnung identische Ergebnisse liefern.
- **Fremdsolver und Analytik:** Externe Programme (PyCBA, IndeterminateBeam, soweit fachlich abgedeckt) und analytische Lösungen. Ihre eigenen Grenzen müssen dokumentiert sein, etwa der Stellenverlust von PyCBA bei sehr weichen oder sehr steifen Federn.
- **Gezielte Grenzfälle und Zufallsmodelle:** Dazu gehören sehr nahe Positionen, extreme Steifigkeitsverhältnisse, fast bewegliche Systeme und die Prüfung der Stabilitätsentscheidung.
- **Eingebaute Fehler:** Jeder nicht äquivalente, fachlich relevante eingebaute Fehler im betrachteten Solvercode muss von dauerhaften Tests oder der Abnahme erkannt werden, nicht nur von Untersuchungsskripten. Überlebende Mutanten werden einzeln geprüft und entweder durch zusätzliche Abdeckung beseitigt oder nachvollziehbar als äquivalent beziehungsweise nicht relevant begründet.

Überführung in die dauerhafte Validierung:

- **Sollwerte statt Skripte:** Geeignete Fälle werden mit exakt berechneten Sollwerten als eingecheckte Abnahmefälle übernommen. Normale CI und Abnahme laufen weiterhin ohne Python, wie heute in `validation/README.md` festgelegt.
- **Geprüfte Größen:** Einzelne Reaktionen, V, M, w und θ an Stationen und im Feld. Das gilt ausdrücklich bei sehr nahen Lagern, Federn und Lasten und geht über globale Gleichgewichtsprüfungen hinaus.
- **Untersuchungswerkzeuge** bleiben Entwicklungswerkzeuge für neue Grenzfälle und Sollwerte.

### A8 Numerischer Vertrag (offen)

Eine pauschale Grenze wie 10⁻⁹ wird nicht übernommen. Die Messungen zeigen je
nach Problemklasse verschiedene Fehlerniveaus, von 10⁻¹⁶ bis 10⁻⁵. Der Vertrag
ist sauber zu definieren:

- **Bezugsgrößen:** Absolut, relativ zur Größe selbst oder relativ zur Feldskala, je Ergebnisgröße.
- **Gültigkeitsbereich:** Problemklassen und Eingabebereiche, für die eine Zusage gilt.
- **Kondition:** Wie die Zusage von der Kondition des Modells abhängt.
- **Ablehnung:** Was gilt, wenn eine Zusage nicht erreichbar ist.
- **Prüfbarkeit:** Wie der Vertrag in Abnahme und CI geprüft wird.

### A9 Grenze zum Fachmodell

- **Übergangslösung:** Interne Nebenmodelle wie `SolverLoads` sind als Forschungs- und Übergangslösung zulässig, aber keine Zielarchitektur.
- **Ziel:** Freigegebene Produktfunktionen werden im Fachmodell abgebildet. Der Solver bekommt seine mechanische Eingabe aus dem Fachmodell und hält kein dauerhaftes verstecktes Nebenmodell.
- **Keine Festlegung jetzt:** Die endgültigen Core-Typen der Features werden erst im jeweiligen Feature-Track festgelegt.

### A10 Migration vom FE-Pfad

- **Öffentlicher Vertrag:** Er bleibt für den heutigen Funktionsumfang erhalten, also Knotenergebnisse, `EvaluateAt` und Extremwerte, soweit A6 keine Erweiterung verlangt.
- **Kein Parallelbetrieb:** Nach erfolgreicher Migration bleibt der alte FE-Solver nicht als zweiter Produktionssolver bestehen.
- **Als Werkzeug:** Ist er als unabhängiges Vergleichswerkzeug nützlich, kann seine Logik außerhalb des Produktpfads erhalten bleiben.
- **Nach der Migration:** Solver-Hash in der CI, `docs/KONZEPT.md`, `docs/SOLVER.md`, `docs/VALIDATION.md` und die Abnahme werden angepasst. `docs/KONZEPT.md` beschreibt heute noch das direkte Steifigkeitsverfahren. Die technische Dokumentation der Webseite folgt nach der Übernahme in `main`.

## B. Feature-Tracks

Die Rechenlogik der Features darf im gemeinsamen Solver vorhanden sein, bevor das
Feature freigegeben ist. Bis zur Freigabe ist sie über Produktpfade nicht erreichbar.

### DEV-20 Innere Gelenke

- **Anforderung:** Momentenfreigabe im geraden Balken, Kräfte werden übertragen. Kein geknickter 2D-Rahmen.
- **Ergebnisse:** Beide Drehwinkel am Gelenk.
- **Mechanismen:** Sie werden exakt erkannt.
- **Nicht eindeutig definiert, vorerst Ablehnung:**
  - Punktmoment genau auf einem Gelenk
  - Gelenk genau an einer Einspannung
  - Gelenk genau an einer Drehbehinderung durch eine Feder
- **Zu klären:** Gelenk genau auf einem Lager ohne Drehbehinderung (Prototyp: erlaubt, trennt den Balken). Die fachliche Bestätigung fehlt noch.
- **Prototyp:** Vorhanden.

### DEV-19 Federlager

- **Anforderung:** Translatorische und rotatorische Federsteifigkeit sowie deren zulässige Kombinationen. Bleibt im geraden 1D-Balkenmodell.
- **Kombinationen:** Starre Verschiebung mit Drehfeder (Teileinspannung), Wegfeder mit Drehfeder, Balken nur auf Federn.
- **Steife, eng benachbarte Federn:** Sie werden ohne Modelländerung genau gerechnet (A3).
- **Zu klären:**
  - Hält ein Federlager axial? Gibt es axiale Federn?
  - Darf eine Feder an derselben Position wie ein starres Lager liegen (Prototyp: Ablehnung)?
  - Wie wird mit sehr großen und sehr kleinen Steifigkeiten umgegangen, ohne einen Ersatz für starre Lager einzuführen?
- **Prototyp:** Vorhanden. Die Gruppierung (5 % der Teillänge, k·L³/EI ≥ 1) ist eine Heuristik und wird im Architekturentwurf geprüft.

### DEV-11 Mehrsegment-Balken

- **Anforderung:** Aufeinanderfolgende Abschnitte mit unterschiedlichem Querschnitt und/oder Werkstoff auf gemeinsamer gerader Achse, also EI und EA je Abschnitt. Kein Rahmenmodell.
- **Zu klären:**
  - Abschnittswechsel an einem Lager, Gelenk oder einer Einspannung
  - Eigengewicht je Abschnitt
  - Spannungsbewertung je Abschnitt (Engineering, nicht Solver)
- **Prototyp:** EI je Abschnitt vorhanden, EA je Abschnitt fehlt.

### DEV-6 Linear veränderliche Streckenlasten

- **Anforderung:** Ein Lasttyp mit zwei Randwerten q₁ und q₂ für konstante, Dreiecks- und Trapezlasten sowie Lasten mit Vorzeichenwechsel.
- **Kurze Lasten:** Auch bei sehr kleiner Ausdehnung werden Resultierende und erstes Lastmoment numerisch stabil erhalten. Daraus darf ein Schwerpunkt nur abgeleitet werden, wenn die Resultierende hinreichend von null verschieden ist. Bei Vorzeichenwechsel kann die Resultierende null sein, das Lastmoment aber nicht, etwa bei einer antisymmetrischen Dreieckslast.
- **Extremwerte:** Das Querkraftextremum im Feld wird gefunden.
- **Zu klären:** Verhältnis zum heutigen Gleichstreckenlasttyp, also ein Typ oder zwei.
- **Prototyp:** Vorhanden.

### DEV-12 Axiale und schräge Einzelkräfte

- **Anforderung:** Axiale Kräfte sowie schräge Punktkräfte mit Betrag und Winkel. Sie werden in axiale und transversale Komponenten zerlegt, der Angriffspunkt liegt auf der Balkenachse. Exzentrische Krafteinleitung ist ein anderes Thema.
- **Theorie 1. Ordnung:** Keine Kopplung von Normalkraft und Biegung.
- **Zu klären:**
  - Normalkraftextrema in der Ergebnisschnittstelle
  - Regeln für axiale Lagerung
- **Prototyp:** Axialanteil vorhanden, nur intern getestet.

## C. Integration und Freigabe

Ein Feature ist erst Produktfunktion, wenn sein Issue vollständig abgeschlossen ist.
Je nach Feature gehören dazu:

| Bestandteil | Beispiel |
|---|---|
| Fachmodell/Core | Typ und Eingabeprüfung |
| Solver | Rechenlogik nach Ebene A |
| Persistenz | Projektformat und Versionierung |
| UI | Eingabe und Darstellung |
| Tests und unabhängige Validierung | Abnahmefälle mit Sollwerten |
| Dokumentation | Entwicklerdoku, Hilfe, technische Doku der Webseite |
| Report | Ausgabe im Bericht, falls nötig |

Mehrere halb fertige Produktfeatures werden nicht gleichzeitig freigegeben.
Bedienung, Persistenz und Report werden in den Feature-Issues separat geplant.

## Entscheidungen

| Nr. | Thema | Entscheidung |
|---|---|---|
| 1 | Alter FE-Solver | Nach der Migration kein zweiter Produktionssolver. Als Vergleichswerkzeug außerhalb des Produktpfads zulässig. |
| 2 | Sehr nahe Positionen | Keine Mindestdistanz, kein Zusammenführen. Verschiedene Koordinaten sind verschiedene Positionen. |
| 3 | Sehr nahe Lager und Federn | Große Reaktionen und Querkräfte bleiben unverändert. Stabilisierung nur für die numerische Darstellung. |
| 4 | Punktmoment genau auf Gelenk | Nicht implizit interpretieren. Bis zu einer fachlichen Semantik nicht eindeutig definiert. |
| 5 | Gelenk genau an Einspannung | Nicht stillschweigend interpretieren. Erst Semantik festlegen. |
| 6 | Temperaturkrümmung | Nicht Teil des Umbaus, bleibt DEV-27. Der Code im Prototyp wird entfernt. |
| 7 | `SolverLoads` und ähnliche Strukturen | Übergangslösung, nicht Zielarchitektur. |
| 8 | Nähe-Warnung wie L/1000 | Nicht übernehmen. Diagnosen beruhen auf numerischer Qualität oder Modellgrenzen. |
| 9 | Validierung | Geeignete Untersuchungsfälle werden dauerhafte Abnahme- und CI-Fälle. |
| 10 | Genauigkeitsziel | Keine pauschale Toleranz. Numerischer Vertrag ist offen (A8). |

## Offene Punkte

1. **Numerischer Vertrag:** Siehe A8.
2. **Ablehnungskriterium:** Weniger Ablehnungen lösbarer Modelle, ohne falsche Ergebnisse durchzulassen (A5).
3. **Kombinationen an gleicher Position:** Semantik je Feature-Track (A2, B).
4. **Architekturentwurf:** Ob das Schnittverfahren die Zielform ist und wie Eingabe, Kern und Ergebnis getrennt werden (A4, A9).
5. **Alter FE-Solver als Vergleichswerkzeug:** Ja oder nein, und wo er liegt.
6. **Plattform:** Prüfung unter Windows.

## Voraussetzung für die Übernahme in `main`

Der Architekturentwurf darf begonnen und implementiert werden, bevor alle offenen
Punkte abschließend geklärt sind. Vor der Übernahme in `main` müssen jedoch
mindestens erfüllt sein:

- **Festgelegt:** Der numerische Vertrag (A8), das Ablehnungskriterium (A5) und die für den aktuellen Produktumfang relevanten Semantiken.
- **Validierung:** Die dauerhafte Validierung ist erfolgreich, einschließlich Windows-CI.
- **Produktionspfad:** Der bisherige Pfad ist eindeutig ersetzt.
- **Dokumentation:** `docs/KONZEPT.md`, `docs/SOLVER.md`, `docs/VALIDATION.md` und der CI-Hash entsprechen dem neuen Stand.

## Folgen außerhalb des Solvers

- **Diagramme:** Bei sehr nahen Lagern kann die Querkraft zwischen ihnen physikalisch sehr groß werden. In einem untersuchten Fall mit 6 nm Lagerabstand entstanden beispielsweise etwa 31 GN Querkraft zwischen den Lagern. Diagramme brauchen dafür eine geeignete Darstellung, ohne den Wert zu verändern.
- **Spätere Bewertungen:** Schubspannung (DEV-17) und Vergleichsspannung (DEV-18) brauchen dieselbe Unterscheidung.

## Nächster Schritt

Architekturentwurf für Ebene A. Er berücksichtigt die Feature-Tracks aus B, ohne
deren offene Semantiken vorab zu entscheiden. Ein Produktionsrefactoring folgt
erst nach dem Entwurf.
