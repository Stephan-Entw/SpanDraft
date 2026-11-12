# SpanDraft – Solver-Umbau

Stand: 12. November 2026. Der heutige Produktumfang ist auf den neuen Solver
migriert: Die Produktintegration (M3) und die Abnahme von Dokumentation,
Acceptance und CI (M4) sind abgeschlossen; die Nachweise stehen in
[VALIDATION.md](VALIDATION.md). Dieses Dokument beschreibt den Umbau auf
`features/solver-umbau`. Der Branch ist noch nicht nach `main` übernommen und
bleibt bis zur ausdrücklichen Übernahmeentscheidung von `main` und der Webseite
getrennt. Die Feature-Tracks (Ebene B) bleiben eigene, spätere Vorhaben.

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
- **Gleiche Position, verschiedene Objekte:** Die Migration erhält die bestehenden Koinzidenzen einschließlich unterschiedlicher starrer Lagertypen am selben Ort (A10). Die Feature-Tracks legen die Zulässigkeit und Semantik neuer Kombinationen fest (B, O6). Eine fachlich nicht definierte Kombination ergibt `InvalidModel`.

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
Balken, die übertragenen Größen sind Unbekannte. Der Architekturentwurf ersetzt es
durch eine lokale Zustandsformulierung, siehe [SOLVER_ARCHITEKTUR.md](SOLVER_ARCHITEKTUR.md).

### A5 Stabilität, Kondition und Fehler

Die [Prüfreihenfolge](SOLVER_ARCHITEKTUR.md#61-prüfreihenfolge) und die
[Zuordnung der Ursachen](SOLVER_ARCHITEKTUR.md#66-fehlercodes) legen die
Ablehnung fest. Der [Gleitkomma-Vertrag](../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md#7-bereiche-und-kontinuierliches-gate)
definiert die numerischen Bereichsregeln, die Verifikation und das Gate.

| Code | Ablehnungsgrund |
|---|---|
| `InvalidModel` | Die Eingabe verletzt den fachlichen Definitionsbereich. |
| `UnstableModel` | Die exakte kinematische Rangprüfung erkennt einen Mechanismus. |
| `IllConditionedSystem` | Das exakt stabile, belastete Modell ist numerisch nicht sicher lösbar. Ursachen sind `ZeroPivot`, fehlende Verifikation (`NotVerified`) oder ein gescheiterter gerichteter SI-Schwellenvergleich (`Gate`). |
| `NumericalFailure` | Eine Bereichsregel ist verletzt (`NumericalRange`), Lösung, Schranke oder Gate sind nicht endlich (`NonFinite`) oder ein interner Selbsttest scheitert (`InternalInconsistency`). |

Für die Annahme gelten die konkreten Prüfungen der Architektur. Es gibt
keine pauschale geometrische oder Konditionsgrenze. Kondition,
Rückwärtsfehler und Eingabesensitivität dienen ausschließlich der Diagnose.
Jede Ablehnung erhält den vorgesehenen Code und, wo definiert, die
strukturierte Ursache. Außerhalb des numerischen Vertrags wird kein
Ergebnis ausgegeben (A8).

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
- **Geprüfte Größen:** Reaktionen je Station und Richtung im heutigen Umfang sowie V, M, w und θ an Stationen und im Feld. Das gilt ausdrücklich bei sehr nahen Lagern, Federn und Lasten und geht über globale Gleichgewichtsprüfungen hinaus.
- **Untersuchungswerkzeuge** bleiben Entwicklungswerkzeuge für neue Grenzfälle und Sollwerte.

### A8 Numerischer Vertrag

Der [Architekturentwurf](SOLVER_ARCHITEKTUR.md#7-numerischer-vertrag-a8)
und der [Gleitkomma-Vertrag](../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md)
definieren Bezugsskalen, Bereichsregeln und verifizierte Fehlerschranken.
Der mathematische Produktwert ist τ = 10⁻⁸. Die Freigabe setzt voraus,
dass jede vollständige SI-Fehlerschranke höchstens τ mal der zugehörigen
Bezugsskala ist. Die C#-Abnahme prüft dies auf jeder unterstützten Plattform.

### A9 Grenze zum Fachmodell

- **Übergangslösung:** Interne Nebenmodelle wie `SolverLoads` sind als Forschungs- und Übergangslösung zulässig, aber keine Zielarchitektur.
- **Ziel:** Freigegebene Produktfunktionen werden im Fachmodell abgebildet. Der Solver bekommt seine mechanische Eingabe aus dem Fachmodell und hält kein dauerhaftes verstecktes Nebenmodell.
- **Keine Festlegung jetzt:** Die endgültigen Core-Typen der Features werden erst im jeweiligen Feature-Track festgelegt.

### A10 Migration vom FE-Pfad

- **Öffentlicher Vertrag:** Er bleibt für den heutigen Funktionsumfang erhalten, also Knotenergebnisse, `EvaluateAt` und Extremwerte, soweit A6 keine Erweiterung verlangt.
- **Kein Parallelbetrieb:** Nach erfolgreicher Migration bleibt der alte FE-Solver nicht als zweiter Produktionssolver bestehen.
- **Als Werkzeug:** Der eingefrorene FE-Pfad bleibt ein unabhängiges Vergleichswerkzeug außerhalb des Produktpfads.
- **Starre Lager an gleicher Position:** Die Migration erhält die Vereinigungsmenge der Sperren, die gemeinsame Reaktion je Station und Richtung und die bestehende Duplikatprüfung. Die Semantik und die Referenzfälle stehen in [Architektur §4.4](SOLVER_ARCHITEKTUR.md#starre-lager-im-heutigen-migrationsumfang) und [§12](SOLVER_ARCHITEKTUR.md#12-migration-a10).
- **Axiale Nullsemantik:** Ohne äußere Axiallasten bleiben u, N und vorhandene axiale Reaktionen exakt null. Reaktionen an freien Freiheitsgraden bleiben abwesend.
- **Nach der Migration:** Solver-Hash in der CI, `docs/KONZEPT.md`, `docs/SOLVER.md`, `docs/VALIDATION.md` und die Abnahme werden angepasst. `docs/KONZEPT.md` beschreibt den produktiven Zustandskernel. Die technische Dokumentation der Webseite folgt nach der Übernahme in `main`.

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
- **Prototyp:** Vorhanden. Die Gruppierung (5 % der Teillänge, k·L³/EI ≥ 1) ist eine Heuristik; der Architekturentwurf sieht keine Gruppierung mehr vor (Bestätigung durch die Abnahme offen, [SOLVER_ARCHITEKTUR.md](SOLVER_ARCHITEKTUR.md#54-steife-und-enge-federgruppen)).

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
- **Belasteter Axialvertrag:** Zweite unabhängige Referenz, vollständige Floating-Point-Kette und Axialfedergesetz gehören zu DEV-12. Die heutige axiale Nullsemantik gehört zur Migration (A10).

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
| 1 | Alter FE-Solver | Nach der Migration eingefrorenes unabhängiges Vergleichswerkzeug außerhalb des Produktpfads. |
| 2 | Sehr nahe Positionen | Keine Mindestdistanz, kein Zusammenführen. Verschiedene Koordinaten sind verschiedene Positionen. |
| 3 | Sehr nahe Lager und Federn | Große Reaktionen und Querkräfte bleiben unverändert. Stabilisierung nur für die numerische Darstellung. |
| 4 | Punktmoment genau auf Gelenk | Nicht implizit interpretieren. Bis zu einer fachlichen Semantik nicht eindeutig definiert. |
| 5 | Gelenk genau an Einspannung | Nicht stillschweigend interpretieren. Erst Semantik festlegen. |
| 6 | Temperaturkrümmung | Nicht Teil des Umbaus, bleibt DEV-27. Der Code im Prototyp wird entfernt. |
| 7 | `SolverLoads` und ähnliche Strukturen | Übergangslösung, nicht Zielarchitektur. |
| 8 | Nähe-Warnung wie L/1000 | Nicht übernehmen. Diagnosen beruhen auf numerischer Qualität oder Modellgrenzen. |
| 9 | Validierung | Geeignete Untersuchungsfälle werden dauerhafte Abnahme- und CI-Fälle. |
| 10 | Genauigkeitsziel | Exakter Produktwert τ = 10⁻⁸ mit verifizierten Ausgabeschranken und Bezugsskalen (A8). |

## Offene Punkte

1. **Neue Feature-Koinzidenzen:** Semantik je Feature-Track (A2, B, O6).
2. **Plattformen:** Unabhängige fachliche und numerische Abnahme je unterstützter Plattform.
3. **Produktentscheidungen der Feature-Tracks:** Siehe [Architekturentwurf](SOLVER_ARCHITEKTUR.md).

## Voraussetzung für die Übernahme in `main`

Für die Übernahme in `main` gelten folgende Bedingungen. Auf dem Branch sind
sie für den heutigen Produktumfang erfüllt; die Übernahme selbst ist eine
eigene, ausdrückliche Entscheidung.

- **Vertrag erfüllt:** Die Implementierung erfüllt den numerischen Vertrag (A8), die Ablehnungskriterien (A5) und die Semantik des heutigen Produktumfangs (A10).
- **Validierung:** Die dauerhafte Validierung ist erfolgreich, einschließlich Windows-CI.
- **Produktionspfad:** Der neue Kern ersetzt den FE-Pfad und hat den Machbarkeitsprototyp des Umbau-Branches abgelöst.
- **Dokumentation:** `docs/KONZEPT.md`, `docs/SOLVER.md`, `docs/VALIDATION.md` und der CI-Hash entsprechen dem neuen Stand.

## Folgen außerhalb des Solvers

- **Diagramme:** Bei sehr nahen Lagern kann die Querkraft zwischen ihnen physikalisch sehr groß werden. In einem untersuchten Fall mit 6 nm Lagerabstand entstanden beispielsweise etwa 31 GN Querkraft zwischen den Lagern. Diagramme brauchen dafür eine geeignete Darstellung, ohne den Wert zu verändern.
- **Spätere Bewertungen:** Schubspannung (DEV-17) und Vergleichsspannung (DEV-18) brauchen dieselbe Unterscheidung.

## Nächster Schritt

Nächster Schritt ist die Übernahmeentscheidung für `main`. Danach folgen die
technische Dokumentation der Webseite (A10) und die Feature-Tracks der Ebene B,
jeweils mit eigener Abnahme nach Ebene C.
