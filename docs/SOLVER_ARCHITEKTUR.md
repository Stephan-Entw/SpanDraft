# SpanDraft – Solver-Architektur (Ebene A)

Stand: 10. November 2026. Architektur für den Branch `features/solver-umbau`.
Die Zielarchitektur ist nach dem Closure für die C#-Implementierungsplanung
des heutigen Produktumfangs eingefroren (Abschnitt 15). Dieses Dokument legt
normativ fest, wie der Rechenkern die Anforderungen A1–A10 aus
[SOLVER_UMBAU.md](SOLVER_UMBAU.md) erfüllt. Produktfreigaben und Core-Typen
werden in den Feature-Tracks festgelegt. [SOLVER.md](SOLVER.md) beschreibt
den FE-Produktpfad auf `main`. Im Umbau-Branch verwendet der öffentliche
Einstieg bereits den Machbarkeitsprototyp `SupportSpanSolver` aus Abschnitt 2.

Alle Zahlen in diesem Dokument stammen aus reproduzierbaren Läufen in
[`validation/investigations/solver-rework/`](../validation/investigations/solver-rework/README.md);
Kennzeichnung: **[Protokollname]** verweist auf `results/<Name>.log`. Die Läufe der neuen
Formulierung (`state-*`) belegen den Entwurf mit einer Python-Referenz
auf Host-Binary64 und exakten Gegenproben wie `transfer_ref.py`.

[`FLOATING_POINT_CONTRACT.md`](../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md)
ist der normative numerische Vertrag für Primitives, Bereichsregeln und
Fehlernachweise (O2). Verweise darauf stehen als [GV x].
[`LITERATURE_AUDIT.md`](../validation/investigations/solver-rework/LITERATURE_AUDIT.md)
ist die nicht-normative Quellen-, Herkunfts- und Begründungsakte (Verweise
[Audit x]). Sie ordnet Entscheidungen nach ihrer Herkunft ein: direkte
Übernahme (L), Anpassung (A), SpanDraft-spezifische Entscheidung (S) oder
verworfene Alternative (V), mit X für rein experimentelle Evidenz.

## 1. Entscheidungen im Überblick

| Nr. | Thema | Entscheidung |
|---|---|---|
| E1 | Formulierung | **Lokale Zustandsformulierung** über mechanische Abschnitte (Mehrfachschießen). Das Schnittverfahren des Prototyps wird nicht übernommen. |
| E2 | Stationen und Unbekannte | Ereignisstationen (alle Positionen) bestimmen Felder und Ausgaben. Unbekannte sind nur die *freien* Komponenten des rechtsseitigen Zustands (w, θ, M, V) an *mechanischen* Stationen. |
| E3 | Vorgeschriebene Größen | w = 0, θ = 0, M = 0 rechts am Gelenk, M = V = 0 am Ende sind bekannte Werte und werden eliminiert, nicht als Gleichungszeilen gelöst. |
| E4 | Reaktionen | Werden nachträglich als Sprung bestimmt und mit einer eigenen adjungierten Lösung bewertet. Bei elastischen Halterungen gibt es zusätzlich das Federgesetz. Beide Wege unterscheiden sich in exakter Arithmetik um das Residual der Federzeile. Geliefert wird der Wert mit dem kleineren `VerifiedOutputBound`. Die Identität beider Wege ist ein interner Selbsttest (Abschnitt 4.2). |
| E5 | Gleiche Position | Station mit geordneten **Teilpunkten**, verbunden durch einen algebraischen Stationsübergang. Jede Anbindung gehört zu einem Teilpunkt. Die Koinzidenzen des heutigen Umfangs, einschließlich starrer Lager, bleiben erhalten (4.4). Neue Kombinationen brauchen eine fachliche Regel (O6). |
| E6 | Federgruppen | Keine geometrische Gruppierung und keine Newton-Multipole. Starke Evidenz (Abschnitt 5.4), noch kein Beweis. |
| E7 | Abstände | Jeder Abstand wird direkt aus zwei gespeicherten globalen Koordinaten gebildet. Abschnittseigenschaften werden nur über die Stationsfolge zugeordnet. |
| E8 | Skalierungskette | Entdimensionierung mit Zweierpotenzen (nur Geometrie und Steifigkeit) und Normierung der rechten Seite mit einer Zweierpotenz → Ruiz-Äquilibrierung mit Zweierpotenzen → LU mit Teilpivotisierung (Wahl begründet: Band (2,2), nur lokale Kopplung, gemessenes Pivotwachstum, 5.2) → adaptive Nachiteration mit Pflichtschritt → a-posteriori-Fehlerschranke mit Verifikation unter den Voraussetzungen des Gleitkomma-Vertrags. Householder-QR ist für diese graduierten Matrizen keine robustere Alternative. |
| E9 | Mechanismus | Exakter Rang auf dem kinematischen Modell der Teilkörper (Bareiss), unabhängig von der Lösungsmatrix. |
| E10 | Numerische Qualität | `RawBound` für den adjungierten Residualanteil, `VerifiedStateBound` nach verifizierter Kontraktion mit μ < 1, `VerifiedOutputBound` für den veröffentlichten SI-Wert. Variante C deckt den ganzen Balken mit gerichteten Stückgrenzen ab. Akzeptanz bei `VerifiedOutputBound` höchstens mathematisch τ = 10⁻⁸ mal Bezugsskala. Kondition und Rückwärtsfehler dienen der Diagnose. |
| E11 | Eingabesensitivität | Nur Diagnose; ändert weder Lösung noch Status. |
| E12 | Ergebnis | Internes Ergebnismodell (Stationen mit Seiten und Sprungarten, Stücke, Reaktionen einschließlich gemeinsamer starrer Zwangsreaktionen, Genauigkeitsbericht). `BeamSolution` wird zur Fassade des heutigen Vertrags. |
| E13 | Grenze Core ↔ Solver | Transiente mechanische Eingabe aus generischen Primitiven, je Rechnung aus dem Fachmodell gebaut. `SolverLoads` entfällt. |
| E14 | Alter FE-Solver | Nach der Migration eingefrorene Testreferenz außerhalb des Produktpfads. |
| E15 | Anregung | Unbelastet genau dann, wenn jedes gespeicherte Anregungsprimitiv null ist. Für geladene Modelle positive Anregungsnorm in Mantissen und ganzzahligen Binärexponenten (7.2). |
| E16 | Aufwand | Primärpfad O(n) in der Zahl mechanischer Stationen. Engpass ist der Qualitätsvertrag mit n + n_r adjungierten Lösungen: Band O(n²), dicht O(n³). Dichte Speicherung ist in M1 zulässig bis zu einer festzulegenden Modellgröße (Abschnitt 5.6). |

## 2. Befunde zum Prototyp

Der Prototyp (`SupportSpanSolver`) belegt die Machbarkeit (Messungen in
[SOLVER_UMBAU.md](SOLVER_UMBAU.md#ausgangslage-machbarkeitsnachweis)). Als Zielform taugt er nicht:

- **Zwei Verfahren in einem.** Drehwinkelverfahren in den starr gelagerten Teilen (`BendingField`),
  Kraftgrößenverfahren über Schnitte für Gelenke, Federn und EI-Wechsel (`HingedBending`).
  Jedes Element braucht eigene Sonderpfade (`TryReaction`, `RigidModes`, `CalculationSupports`).
- **Geometrische Heuristiken.** Für die Gruppierung gelten `GroupFraction = 0.05` und
  k·L³/EI ≥ 1, Gruppen haben höchstens 3 bzw. 2 Mitglieder. Freie Teile werden „in der breitesten
  Lücke“ verankert. `OwnerOf` ordnet eine Feder am Gelenk nach dem Nachbarabstand zu.
- **Teure, schwer prüfbare Funktionale.** Gauß-Integration der Krümmung je Unbekannter und je
  Station; für jede Unbekannte wird ein eigenes Einheitsfeld aufgebaut.
- **Pauschale Grenzen.** SVD-Verhältnis 10⁻¹² und Rückwärtsfehler 10⁻¹⁰. Das lehnt seltene lösbare
  Modelle ab (5 von 6.000 bzw. 13 von 4.000).
- **Eigenschaft über berechneten Mittelpunkt.** `RigidityOf(loads, a + (b − a)/2)` wählt bei einem
  1-ulp-Abschnitt das falsche EI, denn der Mittelpunkt rundet auf den Rand. Gefunden durch die
  Verletzungsanalyse in Abschnitt 6.4; in der neuen Formulierung durch E7 ausgeschlossen.
- **Nebenmodell.** `SolverLoads` trägt Features ohne Core-Typ, dazu die zu entfernende Temperaturkrümmung.

Übernommen werden die **Ideen**, nicht die Strukturen. Lasten wirken über geschlossene,
auslöschungsfreie Integrale (`Difference`/`Ramp`), Lastpositionen erzeugen keine Unbekannten, und V
und M folgen aus dem Gleichgewicht.

## 3. Zielarchitektur

### 3.1 Begriffe

| Begriff | Bedeutung |
|---|---|
| Ereignisstation | Jede Koordinate, an der sich ein Ergebnis ändern kann: Lager, Federn, Gelenke, Abschnittswechsel, Punktlasten, Grenzen verteilter Lasten, Balkenenden. Bestimmt Stücke, Seiten und Ausgabepunkte. |
| Mechanische Station | Ereignisstation mit mechanischer Wirkung auf die Gleichungen: Halterung (starr oder elastisch), Freigabe, Abschnittswechsel, Balkenende. Nur sie trägt Unbekannte. |
| Teilpunkt | Geordnete Unterteilung einer Station mit exakt gleicher Koordinate. Jeder Teilpunkt hat seine Anbindungen; zwischen zwei Teilpunkten gilt ein algebraischer Stationsübergang ohne Länge, Material oder Balkenmechanik. |
| Abschnitt | Bereich zwischen zwei aufeinanderfolgenden mechanischen Stationen mit konstantem EI und EA. |
| Zustand | Biegung (w, θ, M, V), Axial (u, N). |
| Stück | Bereich zwischen zwei aufeinanderfolgenden Ereignisstationen. Dort sind alle Felder geschlossene Polynome. |

### 3.2 Komponenten und Datenfluss

```text
Fachmodell (Core)
  │  Adapter: Umrechnung in SI-doubles, EI = E·I, EA = E·A, Zerlegung schräger Kräfte
  ▼
1 Mechanische Eingabe        transiente Primitive: Halterungen, Freigaben, Abschnitts-
  │                          eigenschaften, Lasten, Teilpunktzuordnung   ← Vertragsgrenze A8
  ▼
2 Eingabeprüfung             Endlichkeit, Vorzeichen, Bereiche; Koinzidenzregeln je Teilpunkt
  ▼
3 Stationsmodell             Ereignisstationen, mechanische Stationen, Teilpunkte, Abschnitte
  ▼
4 Mechanismusprüfung         exakter Rang der Teilkörper-Kinematik (Bareiss)   → UnstableModel
  ▼
5 Gleichungsaufbau           freie Zustandskomponenten, Übertragung + Stationsregeln,
  │                          Lastintegrale mit Datenschranken, Rundungszahlen je Eintrag [GV 5];
  │                          Gleichungszahl und Bereichsregeln [GV 7] → NumericalFailure;
  │                          ohne Anregung: exakte Nullen, weiter mit 8
  ▼
6 Lösung                     Entdimensionierung → Normierung der rechten Seite → Ruiz → LU →
  │                          Nachiteration; Bereichsregeln für Operanden und Zwischenwerte [GV 5–7]
  │                          Nullpivot → NotReliablySolvable (ZeroPivot)
  │                          0 < |Pivot| ≤ λ → NumericalFailure (NumericalRange)
  ▼
7 Qualitätsbewertung         adjungierte Lösungen (n + n_r) → RawBound → Verifikation (μ < 1,
  │                          6.3), sonst NotReliablySolvable (NotVerified) → VerifiedStateBound
  │                          → VerifiedOutputBound, SI-Bezugsgrößen und Selbsttests (6.1, 6.6)
  │                          → Stück-Gate über den ganzen Balken mit gerichteten Untergrenzen
  │                          der Bezugsgrößen → NotReliablySolvable (Gate) bei Überschreitung
  ▼
8 Feldrekonstruktion         Stücke zwischen Ereignisstationen, Links/Rechts-Werte, Reaktionen
  ▼
9 Extremwerte                analytische Kandidaten, Nullstellen, Bewertung
  ▼
10 Ergebnis / Fehler         internes Ergebnismodell, BeamSolution-Fassade, Fehlercode + Ursache
  ┆
11 Diagnosen (optional)      nach der Statusentscheidung, ohne Rückwirkung:
                             Eingabesensitivität, Rückwärtsfehler ω (Entwicklung/Validierung)
```

Biegung und belasteter Axialanteil nutzen in der Zielarchitektur dasselbe Stationsmodell und
durchlaufen 3–9 getrennt (Theorie 1. Ordnung, entkoppelt). Für die Migration gilt die axiale
Nullsemantik aus 4.3.

### 3.3 Invarianten

1. **Last ohne mechanische Station.** Hinzufügen oder Entfernen einer Last
   bei unveränderter Mechanik lässt die mechanischen Unbekannten (Auswahl
   und Reihenfolge), die Gleichungsmatrix, ihre Rundungszahlen und die
   matrixbezogene Zweierpotenz- und Ruiz-Skalierung bitgleich. Lastvektor,
   `F_exc`, RHS-Exponent und Anregungs-/RHS-Normierung dürfen sich ändern
   (5.1, 7.2, [GV 5]). Der historische Lauf
   [`state/invariance.py`](../validation/investigations/solver-rework/state/invariance.py)
   verglich beim Hinzufügen von Punktlasten und einer Streckenlast
   `A`, `S`, `K`, `rs`, `cs` und `free`. In 226 von 226 Modellen
   waren diese Größen gleich (**[state-invariance]**).
2. **Gleichungsstruktur.** Nach der Elimination stimmen die Zahl unabhängiger Gleichungen und die
   Zahl der Unbekannten überein: Jede vorgeschriebene Komponente entfernt genau eine Unbekannte, jede
   freigegebene Sprungbedingung genau eine Zeile. Die Kopplung bleibt lokal zwischen benachbarten
   Stationen. Die Systemgröße wächst linear mit der Zahl mechanischer Stationen. Bei Ordnung nach
   Stationen und Komponenten ist die Bandbreite **für jedes Modell höchstens 2 unten und 2 oben**.
   Das folgt aus der Struktur, nicht aus Messungen: Jeder Zeilenblock beginnt zwei Positionen vor
   seinem Spaltenblock, und die Übertragung ist eine obere Dreiecksmatrix in (w, θ, M, V) [Audit 3.1].
   Nach Teilpivotisierung bleiben die Multiplikatoren in 2 Zeilen unter der Diagonale, U hat obere
   Bandbreite ≤ 4. Gemessen auf 2.956 Modellen (bis 141 Stationen, n = 500) genau 2/2
   **[state-factor-summary]**. Kein Element führt
   globale oder versteckte Unbekannte ein, und keine Bedingung koppelt nicht benachbarte
   Stationen. Diese lokale Kopplung ist zugleich die Grundlage der Faktorisierungswahl (5.2): Die
   bekannten problematischen Mehrfachschieß-Systeme haben zusätzliche Randspalten aus nicht
   separierten Randbedingungen, SpanDraft hat keine.
3. **Positionen.** Zwei Koordinaten sind gleich genau dann, wenn die gespeicherten doubles gleich
   sind. Es gibt kein Runden, kein Snapping, kein Zusammenführen und keinen Mindestabstand (A2).
   Klassifikationen (welcher Abschnitt, welche Seite, welche Eigenschaft) benutzen nur exakte
   Vergleiche gespeicherter Koordinaten, nie berechnete Zwischenpunkte.
4. **Abstände.** Jeder Abstand in Übertragung, Lastintegral und Auswertung ist fl(x₂ − x₁) aus zwei
   gespeicherten Koordinaten, geteilt durch eine Zweierpotenz. Er wird nie aus bereits gerundeten
   lokalen Koordinaten gebildet.
5. **Modelltreue.** Keine Penalty-Steifigkeit, keine Regularisierung, keine Positionsänderung.
   Zweierpotenzskalierungen sind bei normalem darstellbarem Ergebnis exakt.
   Für Randfälle gelten die Bereichsregeln und Verlustschranken aus [GV 3, 5] (A3).
6. **Mechanismus.** Mechanismus genau dann, wenn der exakte Rang der Teilkörper-Kinematik
   unvollständig ist; ohne Toleranz (A5).
7. **Kein stilles Ergebnis.** Gelieferte Ausgaben haben eine verifizierte SI-Fehlerschranke
   höchstens τ mal der exakten Bezugsskala. Für geladene Modelle folgt sie aus Bereichsprüfung,
   Residualschranke, verifizierter Kontraktion und Stück-Gate [GV 2–7].
8. **Unerreichbarkeit.** Rechenlogik nicht freigegebener Features ist über Produktpfade nicht
   erreichbar (B).
9. **Diagnosen ohne Rückwirkung.** Diagnosen ändern weder Lösung noch Schranken noch Status.
10. **Anregung.** Ohne Anregung (7.2) ist das Ergebnis exakt null. Jedes Anregungsprimitiv ≠ 0 ist
    entdimensioniert normal; sonst wird abgelehnt.
11. **Selbsttests.** Interne Identitäten werden bei jeder Rechnung geprüft: Gleichungen =
    Unbekannte, alle Werte endlich, Identität von Sprung und Federgesetz (4.2). Eine Verletzung
    bedeutet einen Implementierungs- oder Zahlbereichsfehler, nie schlechte Kondition. Dann gibt es
    kein Ergebnis, sondern `NumericalFailure` (6.6).

## 4. Mechanische Formulierung

### 4.1 Zustand und Übertragung

Im Abschnitt j mit Länge a = fl(t_{j+1} − t_j), konstantem EI und lokaler Koordinate s = fl(x − t_j)
gilt (Vorzeichen nach [DOMAIN.md](DOMAIN.md), EI·w'' = M, M' = V, V' = q):

```text
V(s) = V_j + P_V(s)
M(s) = M_j + V_j·s + P_M(s)
θ(s) = θ_j + (M_j·s + V_j·s²/2 + P_θ(s)) / EI
w(s) = w_j + θ_j·s + (M_j·s²/2 + V_j·s³/6 + P_w(s)) / EI
```

(w_j, θ_j, M_j, V_j) ist der Zustand rechts der Station j. P_* sind die Integrale der Lasten im
offenen Abschnitt (t_j, x): Punktkraft, Punktmoment, linear veränderliche Streckenlast. Sie werden
mit den auslöschungsfreien Formen `Difference`/`Ramp` ausgewertet. Für x jenseits einer Last sind
dort alle Terme positiv in (x − Lastende) und Lastlänge. So behält eine wenige Nanometer lange
Last ihre Resultierende und ihr erstes Moment (DEV-6). Lasten genau an einer mechanischen Station
sind Sprünge dieser Station.

### 4.2 Stationsregeln

An jeder mechanischen Station (bzw. jedem Teilpunkt) gilt je Zustandskomponente genau eine
Regel. Der linksseitige Zustand ist die Übertragung des Nachbarabschnitts, der rechtsseitige die
Unbekannte.

| Element | w | θ | M | V |
|---|---|---|---|---|
| ohne Wirkung | stetig | stetig | Sprung −C | Sprung +P |
| starre Wegbehinderung | stetig, **w⁺ = 0 vorgeschrieben** | – | – | Sprung frei (Reaktion) |
| Wegfeder k | stetig | – | – | V⁺ − V⁻ + k·w⁺ = P |
| starre Drehbehinderung | – | stetig, **θ⁺ = 0 vorgeschrieben** | Sprung frei | – |
| Drehfeder k_θ | – | – | M⁺ − M⁻ − k_θ·θ⁺ = −C | – |
| Momentengelenk | – | Sprung frei | **M⁺ = 0 vorgeschrieben**, M stetig | – |
| Abschnittswechsel | stetig | stetig | stetig | stetig; EI, EA wechseln |
| Balkenende rechts | stetig | stetig | **M⁺ = 0** | **V⁺ = 0** |

Kombinationen setzen sich je Komponente zusammen, etwa Teileinspannung (starre Wegbehinderung +
Drehfeder) oder Weg- und Drehfeder. Am linken Ende ist der linksseitige Zustand (w, θ frei, M = V = 0).

Warum das genau ist: Der Zustand einer Station ist lokal. Ein kurzer Abschnitt koppelt seine
Endzustände über Koeffizienten a, a², a³ ≪ 1, also nahezu die Identität. Eine vorgeschriebene
Komponente ist exakt 0 und trägt keinen Rundungsrest in die Nachbarschaft. Dass beides nötig ist,
zeigt die Ablation (Abschnitt 5.5): Hält man die vorgeschriebenen Werte als Gleichungszeilen oder
setzt man sie auf den fortgepflanzten Ausdruck, verliert ein Lagertripel im Abstand L/10¹¹ alle
Stellen. Grund ist ein Rundungsrest von w ≈ 10⁻²⁰ am ersten Lager neben Clustertermen der
Größe 10⁻²⁵.

**Reaktionen** sind Sprünge: R = V⁺ − V⁻ − P bzw. C_R = M⁻ − M⁺ − C. Für den Lösungsanteil
ihrer Ausgabeschranke genügt *eine* adjungierte Lösung für das Sprungfunktional (Abschnitt 6.3).
Die Fehler beider Seiten sind korreliert. In früheren Messungen überschätzte die Summe der
Einzelindikatoren um bis zu 10¹² (Feder zwischen zwei Lagern im Abstand 4 nm, Querkräfte 8,7·10¹¹ N
auf beiden Seiten).

**Elastische Halterungen: zwei Wege, eine Identität.** Für eine Feder gibt es eine zweite
Auswertung, das Federgesetz R_law = −k·w⁺ bzw. C_law = −k_θ·θ⁺. Die Federzeile p des Systems lautet
c_jumpᵀx − c_lawᵀx = b_p. Daraus folgt für *jede* berechnete Lösung, in exakter Arithmetik:

```text
R_jump − R_law = −r_p                       (Residual der gespeicherten Federzeile, Krafteinheit)
z_jump − z_law = ±e_p / rs_p                (adjungierte Lösungen Sᵀz = C·c, rs_p Zeilenskalierung)
```

Die beiden Wege sind also nicht unabhängig. Ihre Differenz ist das Residual der Federzeile, und ihre
Fehler unterscheiden sich nur durch den Beitrag dieser einen Zeile. Folgerungen:

1. **Identität.** Sprung und Federgesetz beschreiben dieselbe exakte Reaktion. Die Differenz
   der berechneten Werte darf die Summe ihrer unabhängig verifizierten Schranken nicht
   übersteigen. Die gerichtete Vergleichsrechnung ist ein interner Selbsttest.
2. **Sprung.** Ein adjungiertes Funktional schließt den Lösungsanteil ein. Lastdaten,
   Auswertung und SI-Skalierung kommen hinzu.
3. **Federgesetz.** Die geschätzte Zustandskomponente wird mit der skalierten Steifigkeit
   multipliziert. Ihr verifizierter Zustands- und Auswertungsfehler wird fortgepflanzt.

4. **Geliefert wird der Wert mit der kleineren verifizierten Schranke, bei Gleichheit der Sprung.** Beide Werte sind
   exakte Auswertungen derselben Größe. Der Wert mit der kleineren verifizierten Schranke ist der weniger empfindliche
   gegenüber dem Residual der Federzeile. Er geht mit seiner eigenen Schranke in das Gate ein; eine erkennbare
   Inkonsistenz kann die Wahl nicht verdecken, denn diese ist bereits Gegenstand der
   Identitätsprüfung. Im früheren Vergleich war bei 57 Halterungen gelieferter Modelle der Wert
   mit dem größeren β genauer, höchstens um den Faktor 2,5. Beide Fehler lagen unter ihrem jeweiligen β.
5. **Historischer Vergleich fester Regeln:**
   - *Nur Sprung* lehnt 12 von 10.322 Modellen unnötig ab, mit tatsächlichen Fehlern ≤ 4·10⁻¹⁶
     (Federn zwischen nahen Lagern). In 144 gezielten Extremfällen sind es 42 von 144
     **[state-spring-extremes]**.
   - *Nur Federgesetz* traf auf beiden Mengen dieselben Entscheidungen wie das kleinere β. Dafür gibt
     es aber kein strukturelles Argument: Das Gesetz trägt den vollen Zeilenbeitrag d_p/rs_p, sobald
     der Sprung der unempfindliche Weg ist. Die Extremfälle reichen bis k·L³/EI = 10¹⁵ neben und
     zwischen nahen starren Lagern.
   - *Gewichtete Kombination* R* = (1 − α)·R_jump + α·R_law mit α = z_jump,p/(z_jump,p − z_law,p)
     entfernt den Beitrag der Zeile p ganz. Messbar gewinnt sie nichts: Der Fehler ist in 3.154 Fällen
     kleiner und in 3.831 größer, β im Median 4 % größer, die Entscheidungen sind gleich. Sie wird
     nicht übernommen.

Nach Kategorie (gelieferte Modelle, Fehler relativ zu max(|R|, S)): weiche Federn Sprung bis
2·10⁻¹⁰, Gesetz bis 8·10⁻¹⁶; steife Federn Sprung bis 10⁻⁹, Gesetz bis 8·10⁻¹³, gewählter Wert bis
8·10⁻¹³ (gewählt meist der Sprung); Federn
zwischen nahen Lagern Sprung bis 10⁻⁹, Gesetz bis 3·10⁻¹⁶; reine Federlagerung, Drehfedern und
kombinierte Federn ≤ 3·10⁻¹⁴. In abgelehnten Modellen (20 Halterungen) haben beide Wege dieselbe
Größenordnung des Fehlers.

**Sprungbilanz:** Mit R = R_law ist V⁺ − V⁻ − P = R nur bis auf D erfüllt. Die Summe der
verifizierten Ausgabeschranken beider Reaktionswerte schließt D ein. Relativ zu max(|R|, S_V)
ist D bei |V±| ≫ |R| nicht durch τ beschränkt. Die Tests prüfen die Sprungbilanz gegen die Summe
dieser Schranken (Abschnitt 11).

### 4.3 Axialanteil

Zur Migration gehört die axiale Nullsemantik des heutigen öffentlichen
Vertrags. Ohne äußere Axiallasten sind u und N auch bei Biegelast exakt
null. An axial gehaltenen Knoten ist `ReactionX` nullwertig, an freien
Freiheitsgraden fehlt die Reaktion. Die C#-Abnahme prüft diese Ausgaben
einschließlich beider Ereignisseiten. Die folgende Zustandsarchitektur
für belastete Modelle wird in DEV-12 vervollständigt.

Gleiche Struktur mit dem Zustand (u, N): EA·u' = N, N' = −n. Axiale Halterung schreibt u⁺ = 0 vor,
eine Axialfeder (falls DEV-19 sie einführt) lautet N⁺ − N⁻ − k_a·u⁺ = −P_x. EA-Wechsel sind
Abschnittswechsel. Die Momentenfreigabe überträgt N. Das System ist eigenständig und klein.
Gemessen: `state/axial.py` enthält die Formulierung (u, N) und eine unabhängige exakte Referenz
in Bruchrechnung (Einfachschießen mit u(0) und allen Halterungskräften als Unbekannten). Auf je 300
Zufallsmodellen bei L/10², L/10⁶ und L/10¹¹ (nahe Halterungen, Axialfedern, EA-Wechsel, kurze
Streckenlasten) liegt der größte Fehler bei 3·10⁻¹⁶ **[state-zero]**. Ohne axiale Halterung hat der
Kern einen Nullpivot, die Referenz meldet einen Mechanismus.

**Historische Qualitätsmessung für belastete Axialmodelle.** `state/axial_quality.py` ergänzte den
damaligen komponentenweisen Indikator für (u, N): Rundungszahlen je Eintrag, adjungierte Lösungen mit
genau einem Nachiterationsschritt (die damalige β-Regel wurde dort nicht eigens gemessen), Reaktionen
als Sprung mit eigener adjungierter Lösung. Dazu kommt das
Stück-Gate mit der geschlossenen Untergrenze für |N|. Auf denselben 900 Zufallsmodellen und drei
Grenzfällen (Halterungen im Abstand 10⁻¹¹ L, steife und weiche Feder, Streckenlast der Länge
10⁻¹¹ L): alle geliefert, Gate höchstens 1,8·10⁻¹⁴, tatsächlicher Fehler höchstens 3,8·10⁻¹⁶, keine
Unterschätzung. Gate/Fehler liegt im 1. Perzentil bei 6, im Median bei 34 **[state-axial-quality]**.
Für eine Axialfeder ist nur die Sprungauswertung untersucht; das Federgesetz als zweiter Weg folgt
derselben Identität wie in 4.2. Der Nachweis des belasteten Axialpfads einschließlich
der zweiten unabhängigen Referenz gehört zu DEV-12 (O4).

### 4.4 Gleiche Position und Teilpunkte

Eine Station mit exakt gleicher Koordinate besteht aus geordneten Teilpunkten T₁ … T_m. Jede
Anbindung (Halterung, Freigabe, Abschnittsgrenze, Punktkraft, Punktmoment) gehört genau einem
Teilpunkt. Zwischen T_i und T_{i+1} gilt der algebraische Übergang „Zustand rechts von T_i =
Zustand links von T_{i+1}“, ohne Länge und ohne Material. Jeder Teilpunkt mit mechanischer Wirkung
trägt seinen eigenen rechtsseitigen Zustand.

#### Starre Lager im heutigen Migrationsumfang

Maßgeblich sind die [Modellvalidierung](DOMAIN.md#modellvalidierung) und
der [öffentliche Lager- und Reaktionsvertrag](SOLVER.md#lager-stabilität-und-lösung).
Gleicher Lagertyp an exakt derselben Position bleibt ein ungültiges Duplikat
(`DuplicateSupport` in Core). Unterschiedliche starre Lagertypen an exakt
derselben Position sind zulässig. Für ihre gemeinsame mechanische Station
gilt auch bei der Abbildung auf Teilpunkte die Vereinigungsmenge der
gesperrten Freiheitsgrade. Jede Richtung zählt genau einmal für Elimination
und Mechanismusprüfung. `Pinned + Roller`
sperrt u und w, lässt aber allein die Starrkörperrotation frei. `Fixed`
mit weiteren starren Lagern bleibt mechanisch eine Einspannung.

Je gesperrter Richtung ist die gemeinsame Zwangsreaktion der Station
bestimmbar. Mehrere starre Lager mit derselben Sperre liefern keine
zusätzliche Gleichung, aus der sich einzelne Reaktionen bestimmen ließen.
Die Sprungbilanz und `BeamSolution` führen in dieser Richtung die gemeinsame
Reaktion ohne individuelle Aufteilung (Abschnitte 4.2 und 9).

#### Neue Feature-Koinzidenzen (O6)

Die offenen Semantiken aus B lassen sich als **Daten** ausdrücken:

| Fall (Track) | Ausdruck im Teilpunktmodell |
|---|---|
| Punktmoment genau auf Gelenk (DEV-20) | Moment links oder rechts des Gelenkteilpunkts, oder Ablehnung |
| Gelenk an Einspannung (DEV-20) | Einspannung links oder rechts des Gelenks, oder Ablehnung |
| Gelenk auf Lager ohne Drehbehinderung (DEV-20) | Lager und Gelenk am selben oder an getrennten Teilpunkten |
| Feder und starres Lager an gleicher Position (DEV-19) | Ablehnung oder fachlich definierte Anbindung am Teilpunkt |
| Abschnittswechsel an Lager, Gelenk, Einspannung (DEV-11) | Grenze vor oder nach dem Teilpunkt der Halterung |
| Schräge Kraft am Gelenk oder Lager (DEV-12) | Axial- und Querkomponente am gewählten Teilpunkt |

Die Koinzidenzregeln beschreiben zulässige Kombinationen und ihre Reihenfolge.
Eine nicht definierte Kombination ergibt `InvalidModel` (6.1, 6.6).
Jeder Feature-Track ergänzt die Regeln für seine neuen Kombinationen.
Die Messung für neue Kombinationen umfasst die Fälle, die `transfer_ref.py`
mit fester Reihenfolge abbildet: Gelenk auf Lager, Kraft auf Gelenk, Kraft und Moment auf Feder,
EI-Wechsel auf Gelenk und Feder; alle ≤ 4,5·10⁻¹⁵ **[state-targeted]**.

### 4.5 Erweiterungspunkte

Neue Elemente sind neue Stationsregeln oder Abschnittslasten, kein neuer Kern:
Lagersenkung (vorgeschriebener Wert ≠ 0), Querkraftgelenk (w-Sprung frei, V⁺ = 0), Temperatur-
krümmung DEV-27 (Abschnittslast im θ- und w-Integral), Axialfeder. Jede neue Regel muss
Invariante 2 erfüllen und braucht eine eigene Zeile in der exakten Referenz. Jede neue
Abschnittslast braucht zusätzlich ihre Rundungszahlen, eine Majorante des `VerifiedOutputBound`
je Stück und eine Schranke für die Änderung von |M|, |V| bzw. |N| im Stück (7.3), sonst deckt das
Gate sie nicht ab.

## 5. Numerische Strategie

### 5.1 Entdimensionierung und Äquilibrierung

Geometrie und Steifigkeit liefern `e_L = ILogB(L)` und
`e_EI = ILogB(max EI)`, später auch `e_EA = ILogB(max EA)`.
Die Referenzgrößen sind vor der Exponentenwahl positiv, endlich und normal.
Der Adapter prüft E·I bzw. später E·A gegen den normalen darstellbaren
Bereich. Normierte Referenzwerte liegen in [1, 2). Für w, θ, M, V
und die Last- und Federterme gelten die Einheiten der mechanischen
Formeln. Alle Einheitenwechsel kombinieren ganzzahlige Exponenten und
skalieren in einem Schritt [GV 3, 5].

Bei Anregung normiert ein weiterer Exponent die rechte Seite. Die
positive Norm liegt in den so normierten Einheiten in [1, 2).
Die Matrix bleibt lastunabhängig. Zu kleine oder zu große normierte
Anregungsprimitive führen zu `NumericalRange` (7.2). Die nachfolgende
Ruiz-Äquilibrierung speichert Zeilen- und Spaltenexponenten und kombiniert
sie vor der Matrixskalierung. Die resultierenden Einträge müssen
bereichsgültig sein. Ruiz verbessert die Kandidatenerzeugung und gibt
keine Konditionsgarantie.

### 5.2 Lösung und Nachiteration

- LU mit Teilpivotisierung (GEPP) auf dem äquilibrierten System. Bei Ordnung nach Stationen ist
  das System gebändert (Invariante 2). Die Primärlösung kostet mit Band-LU O(n), der
  Qualitätsvertrag dagegen O(n²); dicht ist für die erste Implementierung zulässig. n ≈ 3,5 je
  mechanischer Station; Kosten und Grenzen in Abschnitt 5.6.
- **Warum GEPP gewählt ist** [Audit 3.1]. Aus der BVP-Literatur ist bekannt, dass GEPP auf
  Mehrfachschieß-Systemen exponentielles Elementwachstum entwickeln kann (Wright 1993). Die dort
  untersuchten Systeme haben zusätzliche Randstruktur: Nicht separierte Randbedingungen koppeln
  beide Enden und erzeugen Spalten außerhalb des Bandes. Die SpanDraft-Formulierung koppelt nur
  lokal, ihre Bandstruktur ist strukturell auf (2,2) begrenzt. Für Bandmatrizen hängt das
  Pivotwachstum nur von der Bandbreite ab, nicht von n (Bohte 1975). Exponentialmoden, gegen die
  Entkopplung und adaptive Schießpunkte schützen (Mattheij), hat Euler-Bernoulli-Biegung nicht;
  die Übertragung ist polynomial. Zusätzlich gemessen auf 2.956 Modellen, darunter Stressfamilien
  bis 141 Stationen und eine adversarielle Suche, die gezielt das Wachstum maximiert:
  ρ = max|a⁽ᵏ⁾|/max|a| ≤ 1,97 **[state-factor-summary]**. Der Korrektheitsnachweis hängt von der
  Residualschranke der gespeicherten Lösung und der Verifikation der adjungierten Lösungen ab (6.3).
  Eine schlechtere Faktorisierung führt nur zu mehr Ablehnungen (gestörte LU in 6.3, [GV 9]). GEPP
  ist deshalb eine Effizienz- und Annahmefrage, keine Voraussetzung der Schranke.
- **Verworfen: Householder-QR als robustere Referenz.** Auf identisch skalierten Systemen liefert
  QR mit derselben Nachiteration in 29 von 2.829 verglichenen Modellen Vorwärtsfehler O(1), wo GEPP
  10⁻¹⁶ erreicht. Das umgekehrte Verhältnis kam nicht vor. Der damalige Indikator β erkannte jeden
  dieser QR-Fehler.
  Einordnung: QR ist normweise rückwärtsstabil. Die Matrizen sind aber trotz Äquilibrierung stark
  graduiert (bis 2·10⁻³⁴ innerhalb einer Spalte). Sie sind normweise extrem schlecht konditioniert
  (κ∞ = 10¹⁷ … 10³³), komponentenweise an der Lösung aber nur mäßig (11 … 77)
  **[state-cond-check]**. Ein normweise kleiner Rückwärtsfehler erlaubt dann einen Vorwärtsfehler
  O(1), und die Nachiteration in Arbeitsgenauigkeit konvergiert bei u·κ∞ ≫ 1 nicht. GEPP mit
  Nachiteration erreicht einen komponentenweise kleinen Rückwärtsfehler (Skeel 1980), und dafür
  zählt nur die mäßige komponentenweise Kondition. Eine strukturierte orthogonale Faktorisierung (Wright 1992) beruht auf denselben
  Transformationen. Ihr Vorteil, gekoppelte Randbedingungen, wird hier nicht gebraucht.
- **Voraussetzung, die Features erhalten müssen:** lokale Kopplung und polynomiale Übertragung.
  Eine Bedingung zwischen nicht benachbarten Stationen (etwa eine Koppelstange) oder
  Exponentialmoden (elastische Bettung, Theorie II. Ordnung) heben die Begründung auf und
  verlangen eine neue Prüfung der Faktorisierung (O17).
- **Nachiteration:** mindestens ein Schritt (Skeel), dann weiter, solange die Korrektur kleiner
  als die Hälfte der vorigen ist; höchstens 5 Schritte; Abbruch bei ‖Δy‖ ≤ 2u‖y‖ oder Stagnation.
  Das ist die normweise Abbruchlogik von LAWN 165 (ρ_thresh = 1/2) mit Pflichtschritt. Die
  Fehlerschätzung aus der Kontraktion ‖dx‖/(1 − ρ_max) wird *nicht* übernommen. Sie setzt ein
  Residual in erhöhter Genauigkeit voraus und lag mit double-Residual in 64 % der Modelle unter dem
  tatsächlichen Fehler **[state-refine-summary]**. Gemessen: 1–3 Schritte in
  99 %, höchstens 5 **[state-rand-*]**. Ohne Nachiteration steigen die Fehler um Größenordnungen
  **[state-ablation]**. Vergleich mit der BERR-Regel von LAPACK `xGERFS`, der LAWN-165-Logik und 5
  festen Schritten auf 2.811 Modellen: identische Entscheidungen. Die BERR-Regel spart 0,4 Schritte,
  kann aber ohne Schritt enden. Dann überwog in der damaligen Messung das Residual in d. β hatte
  kaum Reserve (Gate/Fehler bis 1,1 statt ≥ 7,3) **[state-refine-summary]**.
- **Residual:** in double genügt (Ablation: kompensiertes oder exaktes Residual ändert Fehler und
  Indikator nur unwesentlich, **[state-ablation]**). Kompensierte Residuen (TwoSum/TwoProd über
  `Math.FusedMultiplyAdd`) bleiben eine Option, falls die C#-Messung anderes zeigt.

### 5.3 Sehr nahe Koordinaten

Es gibt keinen Mindestabstand. Ein Abschnitt der Länge 1 ulp ist ein gewöhnlicher Abschnitt mit
a ≪ 1. Zahlbereichsgrenzen sind keine Mindestabstände: Unterläuft eine abgeleitete Größe (etwa a³
bei a/L_ref < 2⁻³⁴⁰) oder ein Zwischenwert ihrer Formel, wird mit `NumericalRange` abgelehnt, nicht
gerundet. Die vollständige Liste der Bereichsregeln steht im Gleitkomma-Vertrag [GV 7]. Dass die Differenz zweier
naher doubles nach Sterbenz exakt ist, ist eine nützliche Eigenschaft, aber keine Voraussetzung.
Der Vertrag bezieht sich auf die gespeicherten Koordinaten; über die Darstellung der ursprünglichen
Dezimaleingabe sagt die Exaktheit der Subtraktion nichts.

### 5.4 Steife und enge Federgruppen

Federn wirken über ihr Gesetz auf den lokalen Zustand. Nahe Federn sind deshalb benachbarte
Stationen mit nahezu identischer Übertragung. Es entstehen weder Differenzen großer Federkräfte
als Unbekannte noch Steifigkeitsterme EI/a³. Das Prinzip dahinter: Keine Rechengröße entsteht als
Summe stark verschieden großer Terme, deren kleiner Anteil die Information trägt. Das
Steifigkeitsverfahren verletzt das bei kurzen Elementen (12EI/a³ + k), das Kraftgrößenverfahren bei
fortgepflanzten Verschiebungen. Der Prototyp umgeht es mit Newton-Basen.

**Evidenz:** Gezielte Matrix mit 27 Konfigurationen × 6 Abständen (L/10 … L/10¹¹) × 6 Steifigkeiten
(k·L³/EI = 10⁻³ … 10¹²), darunter 8 Weg- und 8 Weg-/Drehfedern im Cluster, Cluster über Gelenke,
EI-Wechsel und kurze Lasten mit Vorzeichenwechsel. Ergebnis über 840 Fälle: größte Abweichung 2,3·10⁻¹⁴ im gemischten Maß, größter Indikator 2,8·10⁻¹², keine Ablehnung, keine Indikatorverletzung, höchstens 5 Nachiterationsschritte **[state-targeted]**. Zum Vergleich der Prototyp:
bis 5·10⁻¹⁰ **[s_detail2]**. Zufallsmodelle siehe Abschnitt 7.6.

Das ist starke Evidenz, aber kein Beweis. Die C#-Implementierung übernimmt die gezielte Matrix als
Abnahme (Abschnitt 11). Tauchen dort Federgruppen auf, die die Formulierung nicht trägt, wird der
Befund dokumentiert und neu entschieden. Eine geometrische Heuristik ist nicht vorgesehen.

### 5.5 Ablation

Die folgenden Messungen verwenden den Indikator des damaligen Prototyps.
Der aktuelle Korrektheitsnachweis steht im Gleitkomma-Vertrag.

Notwendigkeit der Bestandteile auf 763 Modellen (gezielte Auswahl + Zufall)
**[state-ablation]**:

Gemessen wird auf den 763 Modellen, die die Zielformulierung liefert (Indikator ≤ τ), jeweils der
Fehler im gemischten Maß:

| Variante | Median | p99 | Maximum | dann abgelehnt |
|---|---|---|---|---|
| Zielformulierung (Elimination, adaptive Nachiteration, Residual double) | 1·10⁻¹⁶ | 5·10⁻¹⁵ | 2,3·10⁻¹² | 0 |
| ohne Nachiteration | 2·10⁻¹⁶ | 1,6·10⁻⁶ | 5,4·10⁻⁴ | 15 |
| genau ein Nachiterationsschritt | 1·10⁻¹⁶ | 4·10⁻¹⁵ | 2,3·10⁻¹² | 0 |
| adaptiv, Residual kompensiert bzw. exakt | 9·10⁻¹⁷ | 3·10⁻¹⁵ | 3,5·10⁻¹² | 0 |
| ohne Entdimensionierung | 1·10⁻¹⁶ | 5·10⁻¹⁵ | 2,3·10⁻¹² | 0 |
| ohne Äquilibrierung | 1·10⁻¹⁶ | 6·10⁻¹⁵ | 2,9·10⁻⁸ | 1 |
| weder Entdimensionierung noch Äquilibrierung | 1·10⁻¹⁶ | 6·10⁻¹⁵ | 8,6·10⁻⁵ | 1 |
| vorgeschriebene Werte als lokale Zeilen | 1·10⁻¹⁶ | 5,9·10⁻² | 2,9 | 9 |
| vorgeschriebene Werte auf dem fortgepflanzten Ausdruck | 1·10⁻¹⁶ | 21 | 1·10⁶ | 64 |
| Reaktionen als Unbekannte (naives Mehrfachschießen) | 1·10⁻¹⁶ | 1,7 | 1,6·10⁹ | 101 |

Folgerungen:

- **Unverzichtbar:** Elimination vorgeschriebener Werte und mindestens eine Nachiteration.
- **Äquilibrierung:** schützt einzelne Fälle (Maximum 2,9·10⁻⁸ bzw. 8,6·10⁻⁵ ohne sie).
- **Entdimensionierung:** für die Genauigkeit neutral. Sie bleibt wegen Zahlbereich,
  Einheitenunabhängigkeit und bitgleicher Zweierpotenz-Invarianz.
- **Weitere Nachiterationsschritte:** auf dieser Menge ohne messbaren Gewinn. Die Regel
  „mindestens einer, weitere bei deutlicher Verbesserung“ kostet im Mittel 0,36 zusätzliche Schritte.
- **Kompensiertes oder exaktes Residual:** ohne Vorteil.

### 5.6 Aufwand

Es gibt n_m mechanische Stationen, n ≈ 3,5·n_m Unbekannte, n_r
Reaktionsfunktionale und k_j Lasten im mechanischen Abschnitt j. Bei
starren Lagern zählt die gemeinsame Zwangsreaktion je Station und
gesperrter Richtung. p = 2 und q = 4 sind die gemessenen Bandbreiten der
Multiplikatoren und von U nach Teilpivotisierung (Invariante 2).

| Schritt | Band | dicht | Bemerkung |
|---|---|---|---|
| Stationsmodell, Gleichungsaufbau | O(n_m + Σk_j) | gleich | Lasten je Abschnitt einsortieren; der Untersuchungscode sucht sie je Abschnitt neu (O(n_m·Σk_j)) |
| Faktorisierung | O(n·p·(p+q)) | 2n³/3 | |
| Primärlösung, ≤ 5 Nachiterationen | O(n) | O(n²) | |
| `RawBound` je Zustandskomponente aus n adjungierten Lösungen | **O(n²)** | **O(n³)** | Engpass |
| Reaktionen: n_r weitere adjungierte Lösungen | O(n·n_r) | O(n²·n_r) | |
| Verifikation (6.3): Residualschranke je adjungierter Lösung, 1 + 3 Durchgänge über M | O(n·p) je Lösung, je Durchgang O(n²) | O(n²) je Lösung, je Durchgang O(n²) | braucht die adjungierten Lösungen und ihre Residualschranken gespeichert (2n² Werte) oder je Durchgang neu gelöst; Größenordnung in [GV 12] |
| Federgesetz, Identitätsprüfung | O(1) je Feder | gleich | Federgesetz nutzt den `VerifiedStateBound`. Die Identitätsprüfung nutzt die verifizierten Ausgabeschranken beider Wege |
| Stück-Gate (geschlossene Untergrenze, 7.3) | O(Σ_j Stücke_j·k_j) | gleich | jede Auswertung läuft vom Abschnittsanfang |
| `EvaluateAt` | O(log n_m + k_j) | gleich | |
| Extremwerte | O(Σ_j Stücke_j·k_j) + Nullstellen Grad ≤ 4 | gleich | |
| Speicher | O(n·(p+q)) | 2n² Werte + Rundungszahlen | |

**Gemessen** auf Durchlaufträgern mit 2 bis 32 Feldern (6 bis 91 mechanische Stationen, n = 19 bis
319) **[state-cost]**: Im Operationsmodell einer Band-Implementierung steigt das Verhältnis
Qualitätsvertrag/Primärlösung von 13 auf 134, also linear in n. Dicht bleibt es bei etwa 10. Die
dichte Python-Rechnung zeigt für den Qualitätsvertrag den Exponenten 3,0. Hochgerechnet mit den
Konstanten des größten Modells (n ≈ 4·n_m):

| n_m | Band Qualität [flop] | dicht Qualität [flop] | dicht Speicher |
|---|---|---|---|
| 10 | 6·10⁴ | 4·10⁵ | < 0,1 MB |
| 30 | 6·10⁵ | 1·10⁷ | 0,2 MB |
| 100 | 6·10⁶ | 4·10⁸ | 2,6 MB |
| 300 | 6·10⁷ | 1·10¹⁰ | 23 MB |
| 1000 | 6·10⁸ | 4·10¹¹ | 256 MB |

Bei grob 10⁹ skalaren Operationen je Sekunde ist das dichte Verfahren bis etwa 30 mechanische
Stationen im Bereich von Millisekunden; ab etwa 100 Stationen erreicht es Zehntelsekunden. Mit Band
wird O(n²) erst bei etwa 1.000 Stationen spürbar. Das sind Schätzungen aus Operationszahlen, keine
C#-Messungen. Viele Lasten in einem Abschnitt kosten O(k²) für das Stück-Gate, weil jede Auswertung
alle Lasten des Abschnitts summiert. Gemessen ist der Exponent ≈ 2 (Python, 10 bis 640 Punktlasten);
das gehört zum Prinzip der auslöschungsfreien Auswertung und ist erst bei sehr vielen Lasten
relevant.

**Zu den Fragen des Aufwands:**

- *Braucht jede Zustandskomponente eine eigene adjungierte Lösung?* Für das Gate ja. Jedes Stück
  kombiniert die vier Komponenten des `VerifiedStateBound` am Abschnittsanfang mit den Gewichten
  |g| (7.3), und jede freie Unbekannte ist eine solche Zustandskomponente. Eigene adjungierte
  Lösungen je Feldpunkt wären schärfer, verlören aber die Monotonie, die das Stückende zur
  Majorante macht.
- *Gemeinsame Auswertung?* Im früheren Indikatormodell ist max_i β_i/S_i die ∞-Norm von
  D_S⁻¹·S⁻¹·D_d. Ein Hager/Higham-Schätzer käme mit O(n) je Schritt aus, schätzt aber das Maximum
  nur (Unterschätzung möglich), liefert keine Werte je Größe und deckt das Stück-Gate nicht ab.
  Eine Halbseparabel-Darstellung der Inversen
  ergäbe eine O(n)-Oberschranke |U_i|·|V_j|, kann aber so pessimistisch werden wie die Summe der
  Einzelindikatoren in 4.2. Beides wird nicht übernommen. Eine billige Verbesserung ohne
  Vertragsänderung: In Uᵀw = e_i sind die ersten i Komponenten null, die Vorwärtssubstitution beginnt
  erst bei i.
- *Transponierte Systeme:* Dieselbe Band-Faktorisierung (Multiplikatoren und Band von U) löst
  Sᵀz = c. Das Residual der Nachiteration nutzt das gespeicherte Band von S. Eine zweite Faktorisierung
  ist nicht nötig.
- *Dicht in M1:* sinnvoll, wenn die Faktorisierung hinter einer Schnittstelle liegt (Lösen,
  transponiert Lösen, Residual). Dann ersetzt eine Band-Speicherung sie, ohne Schranken oder Gate zu
  ändern. Die endgültige Modellgrößen- und Latenzgrenze folgt aus der C#-Messung (O9).
- *Tests vor `main`:* Zähltests (adjungierte Lösungen = n + n_r, Nachiterationsschritte ≤ Obergrenze),
  Bandbreitentest mit wachsender Stationszahl (Invariante 2), ein verfolgter Zeitmesswert bei
  n_m = 10, 30, 100 (Band zusätzlich 1.000) mit Budget statt enger Absolutgrenze, und ein Fall mit
  1.000 Lasten in einem Abschnitt.

## 6. Stabilität, Lösbarkeit und Fehlermodell (A5)

### 6.1 Prüfreihenfolge

1. Eingabeprüfung (Core-Validierung, Gültigkeit der Primitive, Koinzidenzregeln) → `InvalidModel`.
2. Exakte Mechanismusprüfung → `UnstableModel`.
3. Eingaben, normierte Primitive, Abstände und Koeffizientenzwischenwerte auf ihre Bereiche prüfen
   und das reduzierte System aufbauen [GV 5, 7]. Bei Verletzung gilt `NumericalRange`.
4. Sind alle gespeicherten Anregungsprimitive null, werden nach bestandenem Aufbau exakte
   Nullen ohne Faktorisierung ausgegeben (7.2).
5. Geladenes System äquilibrieren und lösen. Ein Nullpivot im exakt stabilen System ergibt
   `ZeroPivot`. Ein nicht-null Pivot an oder unter λ oder eine Bereichsverletzung bei der
   Äquilibrierung ergibt `NumericalRange`.
6. Adjungierte Lösungen verifizieren. Ohne zulässige Kontraktion gilt `NotVerified` (6.3).
7. Verifizierte Feld- und Reaktionsausgaben einschließlich SI-Referenzskalen einschließen.
   SI-Bezugsgrößen liegen in [2⁻⁹⁶⁹, 2¹⁰⁰⁰] und die Ausgabeobergrenzen höchstens bei 2¹⁰⁰⁰,
   andernfalls gilt `NumericalRange`.
8. Selbsttests gelten am jeweiligen Rechenschritt. Nicht endliche Kandidaten ergeben
   `NonFinite`. Verletzte Gleichungszahl oder Federreaktionsidentität ergibt
   `InternalInconsistency`.
9. Scheitert der gerichtete SI-Schwellenvergleich (7.3), gilt `Gate`.

Andere Ablehnungsgründe gibt es nicht. Insbesondere führen weder Kondition noch Rückwärtsfehler ω
noch Eingabesensitivität zu einer Ablehnung.

„Numerisch nicht sicher lösbar“ (im Datenfluss `NotReliablySolvable`) ist bis zur Migration der
bestehende Code `IllConditionedSystem` (Abschnitt 6.6).

### 6.2 Exakte Mechanismusprüfung

Geprüft wird nicht der Starrkörperraum des ungeteilten Balkens. Grundlage ist das kinematische
Modell der **Teilkörper**, die durch Gelenke und Freigaben entstehen. Je Teilkörper gibt es eine
Starrkörperbewegung w = a + b·x. Die Zwangsbedingungen sind:

- Wegbehinderung an x: a + b·x = 0,
- Drehbehinderung: b = 0,
- Momentengelenk an h: w links = w rechts (weitere Freigabearten bringen ihre eigene
  Übergangsbedingung).

Mechanismus heißt: Rang < 2·Teilkörper. Der Rang wird fraction-free (Bareiss) auf ganzzahlig
skalierten Zeilen aus den exakten rationalen Werten der doubles bestimmt. Das betrifft nur diese
kleine Matrix mit den Einträgen 0, ±1, x. EI, Steifigkeiten und Lasten gehen nicht ein. Die Größe ist
2·(Zahl der Freigaben + 1); Kosten und Zahlengröße sind für jede realistische Zahl von Freigaben
vernachlässigbar.

**Federn:** Eine Feder mit 0 < k < ∞ ist mechanisch keine starre Zwangsbedingung. Sie beseitigt einen
Starrkörpermodus nur durch positive elastische Energie. Für die Rangprüfung zählt ihre
Wirkrichtung, unabhängig vom Wert. Daraus folgt *nicht* w = 0 in der Mechanik. k = 0 bedeutet,
dass in dieser Richtung keine Feder wirkt; sie zählt dann auch nicht für den Rang. Negative oder nicht
endliche Steifigkeiten sind unzulässige Eingaben. Ob die Oberfläche k = 0 überhaupt anbietet,
entscheidet DEV-19. Starr ist eine eigene Halterungsart, kein k = ∞. Folge: Ein Balken auf sehr weichen
Federn ist exakt stabil, kann aber numerisch oder physikalisch nahezu ein Mechanismus sein.
Für die numerische Freigabe gilt die Prüfreihenfolge aus 6.1.

**Äquivalenz:** Bei EI > 0 und k > 0 ist das Gleichungssystem genau dann singulär, wenn die
Teilkörper-Kinematik einen Mechanismus hat. Begründung: Bei Last null leistet eine homogene Lösung
keine Arbeit, ihre Formänderungsenergie ist also null. Dann ist w'' = 0 je Teilkörper und w = 0 an
jeder Feder, und sie ist eine zulässige Starrkörperbewegung. Gemessen auf 12.500 Zufallsmodellen:
Mechanismusurteil und exakte Singularität stimmen in allen Fällen überein, und der Bareiss-Rang ist
gleich dem Bruchrang **[state-rand-*]**. Axial: ohne axiale Freigaben ist das ein Mechanismus genau
dann, wenn keine axiale Halterung existiert.

### 6.3 Numerische Qualität

Die Abschätzung unterscheidet drei Größen. `RawBound` ist der
adjungierte Residualanteil. `VerifiedStateBound` schließt nach
verifizierter Kontraktion den Zustandsfehler ein. `VerifiedOutputBound`
schließt für einen veröffentlichten SI-Wert außerdem Funktional,
Daten, lokale Auswertung und Rückskalierung ein. Der ausführbare
Gleitkomma-Vertrag leitet diese Kette und das gemeinsame
Residuallemma für graduellen Unterlauf, FTZ und DAZ her [GV 3–7].

Die Residualschranke d enthält den gemessenen Residualbetrag, seine
Rundungsschranke und die Matrix- und RHS-Datenfehler. Die verifizierten
adjungierten Residuen schließen die Abweichung M von der exakten
Inversenrelation ein. Akzeptiert wird nur ein endliches positives
Gewicht v mit nachgewiesenem Mv ≤ μv und μ < 1. Der
`VerifiedStateBound` ist dann die gerichtete Auswertung von
`RawBound + Mv/(1−μ)`. Ohne Verifikation wird kein `VerifiedOutputBound`
gebildet. Gewichtswahl, Neumann-Schritte und Nachiterationslimits
beeinflussen, welche Kandidaten verifizierbar sind.
Reaktionen verwenden ein eigenes Funktional oder bei einer Feder den
unabhängig eingeschlossenen Wert aus dem Federgesetz. Der
Korrektheitsnachweis hängt an den Schranken, auch bei gestörter
Faktorisierung.

Die C#-Abnahme prüft tatsächliche Fehler gegen die jeweiligen
verifizierten Schranken. Die Python-Referenz stellt Formeln, Primitives,
Status und exakte Gegenproben bereit. Bindend sind die erforderlichen
Zwischenwerte und die bewiesenen Schranken der Operationsfolge
[GV 2].

### 6.4 Verletzungsanalyse

Die folgenden Befunde betreffen frühere Prototypen und ihre damaligen
Indikatoren β. Der aktuelle Vertrag verwendet die drei getrennten
Schrankentypen aus 6.3.

Jede Stelle, an der der exakte Fehler β überstieg, wurde einzeln zerlegt: Datenanteil am exakten
Lösungsvektor, Lösungsanteil und Auswertungsanteil (`state/violations.py`, seit Runde 5 die Stufen A–F des Gleitkomma-Vertrags). Dabei fanden sich sechs
echte Mängel, die jetzt im Entwurf verankert sind:

1. **Abstände aus gerundeten lokalen Koordinaten.** Die Last bei 0,0132539 m liegt im Abschnitt
   [0,00384; 0,01345]. a − c wurde aus zwei bereits gerundeten Größen gebildet; der Datenfehler lag
   9,75-mal über dem Modell. Seit E7 bzw. Invariante 4 ist das Verhältnis ≤ 0,28.
2. **Abschnittseigenschaft über den Mittelpunkt.** Feder und EI-Wechsel liegen 1 ulp auseinander
   (1,1385679834798157 bzw. …816), der Mittelpunkt rundet auf den Wechsel. Danach Verhältnis ≤ 0,05.
   Derselbe Fehler steckt im Prototyp.
3. **Unterlauf.** Eine Punktlast von 10⁻³²⁰ N auf einem Einfeldträger ergibt entdimensioniert 0.
   Damit ist b ≡ 0, das Ergebnis exakt null, und ohne Unterlaufterm ist auch β = 0. Das Modell würde mit
   67 % tatsächlichem Fehler *geliefert*. Bei 10⁻³¹⁰ N (subnormal) ergeben sich 7,8·10⁻⁹ Fehler bei
   β = 0. Im Vertrag fängt beides zuerst die Bereichsregel aus 7.2 ab (Anregung entdimensioniert
   nicht normal → `NumericalFailure`). Der Unterlaufterm (Runde 3/4: η, seit Runde 5: λ) ist die
   zweite Sicherung für Unterläufe in Zwischenergebnissen: Auch ohne Bereichsregel lehnt er beide
   Fälle ab, normale Modelle ändern sich nicht **[state-zero]**.
4. **Pessimistische Reaktionsindikatoren** als Summe von Einzelindikatoren (bis 10¹²), siehe 4.2.
5. **Unterlauf der SI-Rückrechnung** (Runde 4, [Audit 3.4]). Ein Kleinstbalken (L = 10⁻⁶⁰ m) ist
   entdimensioniert völlig normal; erst die exakte Zweierpotenz-Rückrechnung in SI unterläuft
   (w ≈ 10⁻³¹⁸ m). β in SI unterläuft mit, der Rundungsfehler der Rückrechnung (≤ 2⁻¹⁰⁷⁵, mit FTZ
   < 2⁻¹⁰²²) steckt nicht in β. Ergebnis ohne Gegenmaßnahme: *geliefert* mit Fehler 9,7·10⁻⁸ bzw. 9,4·10⁻⁵ bei Gate 2,4·10⁻¹⁵
   **[state-underflow]**. Abhilfe: Bereichsregel auch für die SI-Bezugsgrößen (6.1 Schritt 7, 7.2).
   Danach wird keiner der 64 gezielten Unterlauffälle falsch geliefert. Seit Runde 5 ist die
   Rückskalierung allgemein Teil des Vertrags: S_q,SI ≥ 2⁻⁹⁶⁹ und λ-Anteil im
   `VerifiedOutputBound`, auch mit FTZ (mit FTZ hätte der Kleinstbalken 2,1·10⁻² Fehler)
   **[state-fpc-chain-underflow]**.
6. **Datenfehler abgeschnittener Linearlasten** (Runde 5, [GV 9]). Endet ein Abschnitt nahe dem
   Nulldurchgang einer Linearlast, hat die abgeschnittene Intensität q_a = v₁ + (v₂ − v₁)f einen
   absoluten Fehler u·|v|, also einen großen relativen. Das Rundungsmodell von Runde 1–4
   (`K_DIST = 40` auf |t₁| + |t₂|) hatte dafür keinen Term und unterschätzte den Fehler dieser
   Lastintegrale bis zum 9,4·10¹³-Fachen; beim Abstand 0 stimmte nicht einmal das Vorzeichen. Auf
   Ausgabewerten war es in den untersuchten Modellen nicht sichtbar (Fehler/Schranke ≤ 0,056),
   weil der Term absolut winzig ist. Abhilfe: eigener Datenterm der abgeschnittenen Intensität im
   Vertrag (Punkt 4 in [GV 5]); danach 0 Verletzungen.

Im damaligen Versuch mit adjungierter Nachiteration lag der Indikator in gelieferten Modellen
stets über oder auf dem tatsächlichen Fehler.
In abgelehnten Modellen (β zwischen 8·10⁻⁴ und 0,45) übersteigt der Fehler β dort, wo die erste
Ordnung nicht mehr gilt, um höchstens den Faktor 10⁰·⁵ ≈ 3 **[state-acceptance-summary]**. Mit der
Verifikation aus 6.3 ist das kein Restfall mehr: Entweder deckt die korrigierte Schranke den
Fehler, oder das Modell ist nicht verifiziert. Auf den 2.811 Modellen der Runde 4 gab es keine
Verletzung, auch nicht in abgelehnten Modellen **[state-refine-summary]**.

### 6.5 Eingabesensitivität (nur Diagnose)

Zu unterscheiden sind zwei Größen.

- **Numerischer Lösungsfehler:** berechnetes Ergebnis gegen die exakte Lösung der dem Solver
  übergebenen doubles. Teil des Vertrags; kann zur Ablehnung führen.
- **Eingabesensitivität:** Änderung der exakten Lösung bei kleiner Eingabeänderung. Ausschließlich
  Diagnose und Qualitätsinformation. Sie geht weder in den Solverstatus noch in die
  Akzeptanzentscheidung ein. Ablehnungsgründe sind allein die numerischen Kriterien aus 6.1.

Werkzeug ist die 1-ulp-Probe (`state/sens.py`): Jede Eingabeklasse wird um 1 ulp verschoben und
exakt neu gelöst. Sie braucht exakte Referenzlösungen und ist deshalb ein Entwicklungs- und
Validierungswerkzeug. Ein Produktsolver könnte höchstens eine Gleitkomma-Näherung davon als
zusätzliche Information ausgeben, nach der Statusentscheidung und ohne Rückwirkung.
Untersuchungstest **[state-decoupling]**: 158 Modelle (alle 16 abgelehnten bzw. numerisch
singulären der Zufallsläufe und 142 gelieferte) werden einmal ohne und einmal nach einer
vollständigen Sensitivitätsauswertung gelöst. Lösung, Indikatoren, Reaktionen und Status (geliefert
142, abgelehnt 11, numerisch singulär 5) sind in allen 158 Fällen bitgleich, und das Modell bleibt
unverändert.

Beobachtung, keine Begründung: Die abgelehnten Modelle der Zufallsläufe haben alle eine hohe
Positionssensitivität (≥ 0,6 der Skala) **[state-rand-*]**. Umgekehrt folgt aus hoher Sensitivität
keine Ablehnung: Die Konfiguration „gemischt“ hat eine Positionssensitivität bis 4·10⁹ und wird auf
10⁻¹⁶ genau gelöst und geliefert. Ob eine spätere Oberfläche Sensitivität als Hinweis zeigt
(„Ergebnis hängt stark von der exakten Lage nahe beieinanderliegender Lager ab“), ist eine offene
Produktentscheidung (O8).

### 6.6 Fehlercodes

`SolverErrorCode` bleibt für die Oberfläche stabil, zusätzlich gibt es eine strukturierte Ursache:

| Code | Ursache (Beispiele) |
|---|---|
| `InvalidModel` | Eingabe verletzt den Definitionsbereich: Validierungsfehler aus Core, nicht endliche Werte, EI/EA ≤ 0, k < 0 oder nicht endlich, Position außerhalb des Balkens; nicht definierte Koinzidenz (Kombination, Teilpunkt); k = 0 je nach DEV-19 |
| `UnstableModel` | exakter Mechanismus quer (betroffener Teilkörper) oder axial |
| `IllConditionedSystem` (Bedeutung: *numerisch nicht sicher lösbar*) | `ZeroPivot`: exakt stabil, F_exc > 0 und Nullpivot; `NotVerified`: adjungierte Lösungen nicht verifiziert (μ ≥ 1 für alle Gewichtskandidaten, 6.3), die Genauigkeit ist nicht nachgewiesen, das Ergebnis aber nicht als falsch erkannt; `Gate`: gerichteter SI-Schwellenvergleich fehlgeschlagen |
| `NumericalFailure` | `NumericalRange`: eine Bereichsregel aus [GV 7] verletzt (Eingabe ≠ 0 nicht normal, L < 2⁻⁹⁶⁸, Koeffizient oder Zwischenwert nicht normal, normiertes Anregungsprimitiv ≠ 0 nicht normal, Streckenlast zu kurz, Eintrag nach Ruiz außerhalb des zulässigen Bereichs, nicht-null Pivot an oder unter λ, SI-Bezugsgröße außerhalb [2⁻⁹⁶⁹, 2¹⁰⁰⁰]); `NonFinite`: Lösung, Schranke oder Gate nicht endlich; `InternalInconsistency`: Gleichungszahl ≠ Unbekannte, Federreaktions-Identität verletzt |

Die Zuordnung ist vollständig: Jede Ablehnung hat genau einen dieser Codes, und kein anderes
Kriterium lehnt ab (6.1). Kondition, ω und Eingabesensitivität dienen
ausschließlich der Diagnose. Die strukturierte Ursache ergänzt den
öffentlichen Code.

Die Umbenennung von `IllConditionedSystem` ist eine Option der Ergebnisschnittstelle. Der Code
bleibt bis zur Migration erhalten.

## 7. Numerischer Vertrag (A8)

### 7.1 Vertragsgrenze und Referenzlösung

Referenz ist die **exakte mathematische Lösung des mechanischen Modells zu den doubles, die dem
Rechenkern übergeben werden** (Schnitt 1 in Abschnitt 3.2). Nicht zum Solverfehler gehören:
UI-Parsing, Einheitenumrechnung, Querschnittswerte (A, I), Werkstoff (E), das Produkt E·I und die
Zerlegung schräger Kräfte über sin/cos (DEV-12). Diese Schritte haben eigene Verträge in Core bzw.
im Adapter. Die Untersuchung in `state/` zählt die Rundung von E·I konservativ trotzdem zum
Solver. Prüfbar ist die Referenz mit den exakten Bruchrechnungen (`transfer_ref.py` und die
Macaulay-Referenz `spring_ref.py`, untereinander bitgleich, `xcheck.py`).

### 7.2 Anregung und Bezugsgrößen

`unloaded` gilt genau dann, wenn alle gespeicherten Anregungsprimitive
exakt null sind. Bei stabilem Modell und gültigem Aufbau liefert dieser
Pfad ohne Faktorisierung exakte Nullen. Kompensierende Lasten bleiben
geladen. Für Biegung dient folgende positive Trapeznorm der Skalierung.

```text
F_exc = Σ|P| + Σ((|q_a| + |q_b|)/2)·l + Σ|C|/L
```

Die Norm wird aus Mantissen und Exponenten bestimmt. Sie ist bei
Vorzeichenwechsel im Allgemeinen verschieden vom Integral des Betrags
der Intensität. Geometrie und Steifigkeit werden vor der Normierung
geprüft. Neue Anregungsprimitive erhalten ihre Normbeiträge und
Bereichsregeln im jeweiligen Feature-Track [GV 5, 7].

Die exakten Bezugsskalen verwenden Stationsmaxima an den
Ereignisstationen einschließlich beider vorhandener Seiten.

```text
S_M = max(max|M|, F_exc·L)        S_V = max(S_M/L, F_exc)
S_θ = max(max|θ|, S_M·L/EI_min)   S_w = max(max|w|, S_M·L²/EI_min, S_θ·L)
```

Für w und θ gilt S_q als Bezugsskala. Für M, V und Reaktionen
gilt max(S_q, Betrag des exakten Werts) mit der entsprechenden Einheit.
Die gerichteten unteren Skalen verwenden Stationswerte abzüglich der
verifizierten Schranke, eine untere Anregungsnorm und eine obere
Schranke für EI_min. Für die SI-Range-Prüfung werden die Skalen auch
nach oben eingeschlossen [GV 7].

### 7.3 Kontinuierliches Qualitätsgate und Zusagen

Variante C verwendet die verifizierte Fehlermajorante je offenem
Stück und prüft die Ereignisseiten separat. Für M und V kann eine
gerichtete Untergrenze des exakten Betrags im Stück die Bezugsskala
verbessern. Die Untergrenze folgt aus der maximalen Änderung von
M und V bei eingeschlossenen exakten Streckenlasten. Die Betragsgrenzen
für die SI-Ausgabe umfassen auch das Innere von w- und θ-Stücken.
Die Begründung und die Richtung aller Rundungen stehen in [GV 3, 7].
Exakte Bernstein-Einschließung bleibt die unabhängige Gegenprobe.

Freigabe erfordert `VerifiedOutputBound ≤ τ · ReferenceScale` mit dem
exakten Produktwert `τ = 1/100_000_000`. Die Referenz prüft eine nach
oben eingeschlossene SI-Schranke gegen die nach unten gerundete
Schwelle aus [GV 7]. Das Gate deckt damit Feldwerte, Stationswerte,
Reaktionen und Werte an ausgegebenen Extremwertkandidaten ab.

O5 ist mit Variante B geschlossen. `PolynomialRoots` bleibt
unabhängig validiert. Der `VerifiedOutputBound` für den Kandidatenwert
gilt am ausgegebenen Ort und auf der ausgegebenen Seite.
Kandidatenvollständigkeit und Positionsfehler werden getrennt gegen
exakte Wurzelisolation validiert [GV 11].

### 7.4 Gültigkeitsbereich

Der Vertrag gilt für alle Modelle, die:

- Eingabeprüfung und Mechanismusprüfung bestehen,
- alle Bereichsregeln des Gleitkomma-Vertrags erfüllen ([GV 5–7] für
  Eingaben, Zwischenwerte, normierte Anregung, Skalierung und Pivots),
- deren adjungierte Lösungen verifiziert sind (μ < 1, 6.3) und die den gerichteten
  SI-Schwellenvergleich bestehen (7.3),
- deren SI-Bezugsgrößen in [2⁻⁹⁶⁹, 2¹⁰⁰⁰] liegen (7.2),

und er gilt unter den Plattformvoraussetzungen aus [GV 2], bei graduellem oder abruptem Unterlauf.

Eine Zusage zum *Anteil* gelieferter Modelle gibt es nicht. Sie hängt von der Kondition des
Modells ab und ist nur statistisch beschrieben (7.6).

### 7.5 Prüfbarkeit

- A–F sind harte Konformitätsprüfungen mit exakten Sollwerten. Sie
  reichen von lokalen Formeln, Daten und Residuen über Kontraktion
  und Zustandsfehler bis zu Ausgaben, Stückgrenzen und SI-Gate.
- Range- und Statusfälle prüfen Ablehnung und Fehlerursache. Exakt
  unbelastete Fälle prüfen exakte Nullen. Die Migration prüft auch
  exakte axiale Nullausgaben bei nicht-null Biegelast.
- Auf jeder unterstützten Plattform gelten dieselben fachlichen und
  numerischen Kriterien. Bitvergleiche einzelner Primitives können
  die Diagnose unterstützen [GV 2, 9, 13].

### 7.6 Historische Messungen vor dem Closure-Pass

Die Kennzahlen dieses Abschnitts beziehen sich auf die jeweils benannten
früheren Prototypen und ihre Indikatoren. Die aktuellen Nachweise stehen
in [GV 9].

Zufallsmodelle mit den Generatoren der Prototyp-Untersuchung (Federn: gleiche Seeds wie
`s_fuzz.py`, also dieselben Modelle; gemischt: Zufallsfolge von `f_fuzz.py`, Krümmung verworfen).
Der Abstand bezeichnet, wie nah Lager, Federn, Gelenke, Lasten und EI-Wechsel aneinander gerückt
werden. Fehler im gemischten Maß, τ = 10⁻⁸ **[state-rand-s-*, state-rand-f-*]**:

| Klasse | Modelle | Mechanismen | gelöst | Median | p99 | abgelehnt (Indikator > τ) | numerisch singulär |
|---|---|---|---|---|---|---|---|
| Federn L/10² | 1.500 | 350 | 1.150 | 1·10⁻¹⁶ | 3·10⁻¹⁵ | 0 | 0 |
| Federn L/10⁴ | 1.500 | 340 | 1.160 | 1·10⁻¹⁶ | 3·10⁻¹⁵ | 0 | 0 |
| Federn L/10⁶ | 1.500 | 391 | 1.109 | 1·10⁻¹⁶ | 2·10⁻¹⁵ | 0 | 0 |
| Federn L/10⁹ | 1.500 | 364 | 1.136 | 1·10⁻¹⁶ | 5·10⁻¹⁵ | 6 | 0 |
| Federn L/10¹¹ | 1.500 | 361 | 1.139 | 1·10⁻¹⁶ | 3·10⁻¹⁵ | 1 | 0 |
| gemischt L/10² | 1.000 | 243 | 757 | 1·10⁻¹⁶ | 2·10⁻¹⁵ | 0 | 0 |
| gemischt L/10⁴ | 1.000 | 222 | 778 | 1·10⁻¹⁶ | 3·10⁻¹⁵ | 0 | 0 |
| gemischt L/10⁶ | 1.000 | 233 | 767 | 1·10⁻¹⁶ | 8·10⁻¹⁵ | 1 | 0 |
| gemischt L/10⁹ | 1.000 | 235 | 765 | 1·10⁻¹⁶ | 1·10⁻¹⁵ | 2 | 0 |
| gemischt L/10¹¹ | 1.000 | 250 | 745 | 1·10⁻¹⁶ | 2·10⁻¹⁵ | 1 | 5 |

- **Mechanismen:** In allen 12.500 Modellen gleich der exakten Singularität.
- **Nachiteration:** in rund 70 % ein Schritt, in 27 % zwei, höchstens fünf.
- **Ablehnungen:** 11 plus 5 numerisch singuläre, also 0,17 % der 9.511 exakt stabilen Modelle.
  Ablehnungsgrund ist in allen Fällen β > τ bzw. der Nullpivot. Als Diagnose (6.5, nicht als
  Begründung) haben alle eine 1-ulp-Positionssensitivität ≥ 0,6. Die numerisch singulären Fälle sind
  Balken nur auf zwei Federn im Abstand ~10⁻¹¹ L mit stark verschiedener Steifigkeit.
- **Pessimistische Ablehnungen:** Drei der abgelehnten Modelle haben einen tatsächlichen Fehler
  unter τ (2·10⁻⁹ … 4·10⁻⁹), werden aber wegen β abgelehnt (O3).
- **Stück-Gate und adjungierte Nachiteration** ändern an diesen Zahlen nichts. Die Akzeptanzstudie
  mit beidem ergibt dieselben 10.335 gelieferten Modelle (inklusive der 840 gezielten) und 16
  Ablehnungen **[state-acceptance-summary]**. Mit dem damaligen Gate (Variante C, β-Regel der
  adjungierten Nachiteration) ist es ebenso: 10.335 geliefert, 11 abgelehnt, 5 numerisch singulär
  **[state-gate-summary]**.
- **Akzeptanzfaktor s** (Gate mit adjungierter Nachiteration, alle 10.351 exakt stabilen Modelle):

  | s | geliefert | abgelehnt | größter tatsächlicher Fehler geliefert | geliefert mit Fehler > τ | abgelehnt mit Fehler ≤ τ |
  |---|---|---|---|---|---|
  | 1 | 10.335 | 16 | 2,0·10⁻¹¹ | 0 | 3 |
  | 2 | 10.335 | 16 | 2,0·10⁻¹¹ | 0 | 3 |
  | 4 | 10.335 | 16 | 2,0·10⁻¹¹ | 0 | 3 |
  | 10 | 10.333 | 18 | 5,9·10⁻¹² | 0 | 5 |

  Kein s liefert ein Ergebnis mit Fehler > τ. Ein größeres s erzeugt nur unnötige Ablehnungen.
  Zwischen dem größten gelieferten Fehler und τ liegen 500×, und β überschätzt im 1. Perzentil um
  10¹. Ein Sicherheitsfaktor ist durch die Referenzmenge daher nicht begründet. **Entscheidung
  s = 1.** Die frühere Einschränkung (nur 2 Modelle mit Gate in (10⁻⁹, 10⁻⁸]) ist durch gezielte
  Grenzfälle ersetzt (Runde 4, `state/near_tau.py`). Jedes Zufallsmodell der Gate-Läufe mit Gate
  ≥ 10⁻¹¹ wird mit stetig verändertem Abstandsparameter neu erzeugt (derselbe Zufallsstrom), und
  per Bisektion werden Gates bei 10⁻¹⁰, 10⁻⁹, 3·10⁻⁹, 10⁻⁸ und 3·10⁻⁸ getroffen. Ergebnis
  **[state-refine-summary]**: 75 Modelle, 41 mit Gate in (10⁻¹⁰, 10⁻⁸]. 52 geliefert, größter
  Fehler 2,4·10⁻¹⁰, Gate/Fehler ≥ 11,8. Alle 23 abgelehnten haben einen Fehler ≤ τ; das ist
  Pessimismus von β (O3), kein falsches Ergebnis. Mit allen Nachiterationsregeln und mit
  Verifikation sind die Entscheidungen gleich, auch bei um 10⁻⁴ gestörten LU-Faktoren.
  Nahe τ ist s = 1 damit auf 75 gezielten Fällen belegt, die Reserve beträgt mindestens den Faktor 11,8.
- **Vergleich mit dem Prototyp auf denselben 6.000 Federmodellen** (L/10² … L/10⁹): Er lehnte 14 ab,
  die neue Formulierung 6. Sein größter Fehler lag bei 2·10⁻⁷ **[s_rand2]**. Der größte
  tatsächliche Fehler gelieferter Modelle der neuen Formulierung liegt auf diesen Klassen bei
  1,1·10⁻¹¹ **[state-gate-summary]**.
- **Round-5-Regressionssatz:** Der Closure-Pass prüft die gezielten Fälle nahe τ erneut.
  Die Zufallsregression umfasst jedes fünfte Modell aus den Zufallssätzen der Runde 4.
  Aktuelle Entscheidungen und Schrankenvergleiche stehen in **[state-fpc-summary]**.
- **Einheitenwechsel** (m ↔ mm, N ↔ kN): Auf 112 gelieferten Modellen liegt der Fehler gegen die
  exakte Lösung der jeweils eigenen Eingabe bei ≤ 3,9·10⁻¹⁵, keine zusätzliche Ablehnung. Eine reine
  Zweierpotenz-Umskalierung ist in 307 von 307 Modellen bitgleich **[state-invariance]**.

## 8. Felder, Links/Rechts und Extremwerte (A6)

- **Stücke** zwischen allen Ereignisstationen. Je Stück wird der Zustand am Stückanfang *direkt* vom
  Anfang seines mechanischen Abschnitts berechnet (Lastintegrale mit globalen Abständen), nie von
  Stück zu Stück fortgepflanzt. Die Felder sind geschlossene Polynome in der lokalen
  Stückkoordinate: w bis Grad 5, θ 4, M 3, V 2 bei linear veränderlicher Last; u bis Grad 2 bzw. 3,
  N bis Grad 1 bzw. 2.
- **Auswertung** an x immer direkt vom Anfang des mechanischen Abschnitts. Die Stückpolynome dienen
  nur der Nullstellensuche.
- **Seiten:** An jeder Station gibt es Werte links und rechts; bei Teilpunkten links vom ersten und
  rechts vom letzten. Am Gelenk liefert die Schnittstelle θ links und θ rechts. Sprünge von V, M, θ
  und N sind als **Sprungarten** je Station ausgewiesen: Punktlast, Reaktion, Moment, Freigabe. Die
  Darstellung muss sie nicht aus Reaktionen und Lasten rekonstruieren, wie es
  `ResultDiagramProjection.CanJump` heute tut.
- **Extremwerte:** Kandidaten sind die Stückenden (beide Seiten) und die inneren Nullstellen der
  Ableitung: θ für w, V für M, q für V (DEV-6, Querkraftextremum bei linearer Last), n für N. Die
  Nullstellen bestimmt das vorhandene `PolynomialRoots` (Grad ≤ 4). Danach wird am Kandidaten wie
  ein Feldwert ausgewertet und bewertet. Für N-Extrema ist die Struktur vorhanden; ob sie in die
  Schnittstelle kommen, entscheidet DEV-12.
- **Extremwertreferenz:** exakte Isolation der Wurzeln der Ableitung je Stück (quadratfreie
  Zerlegung nach Yun, Sturm-Folgen über Q, dyadische Intervalle, Vielfachheiten), gegengeprüft mit
  sympy (`state/piece_oracle.py`). Auf 1.833 Modellen kein übersehener Kandidat, gelieferte
  Extremwerte ≤ 3,1·10⁻¹⁴ **[state-piece-*]**. Wichtig für die Implementierung: Der Kandidatenwert
  wird an der *gerundeten* Stelle über `EvaluateAt` ausgewertet, nicht am Stückpolynom bei t.

## 9. Ergebnisschnittstelle (A10)

Das **interne Ergebnismodell** wird ohne FE-Begriffe gebildet:

| Teil | Inhalt |
|---|---|
| Stationsergebnis | Position, Teilpunkte, Zustand links und rechts (w, θ, M, V, u, N), Sprungarten |
| Reaktion | Kraft bzw. Moment mit Station und Richtung. Bei starren Lagern eine gemeinsame Reaktion je wirksamer Zwangsrichtung (4.4) |
| Stück | Grenzen, Feldpolynome |
| Extremwerte | w, V, M (N nach DEV-12) mit Ort und Seite |
| Genauigkeitsbericht | Gate und größter normierter `VerifiedOutputBound` je Größe relativ zu τ, Verifikation (μ), „ohne Anregung“, Nachiterationsschritte. Diagnosen (Sensitivität, ω) getrennt davon und nur in Entwicklung/Validierung |

**`BeamSolution` als Fassade** erhält den heutigen Vertrag:

- `Nodes` sind die Ereignisstationen; das sind dieselben Positionen wie die heutigen FE-Knoten.
  `NodeIndex` ist der Stationsindex. u, w und θ sind die rechtsseitigen Werte.
  `ReactionX`, `ReactionY` und `ReactionMoment` enthalten je Station die
  gemeinsame Reaktion der jeweiligen gesperrten Richtung. Für freie
  Freiheitsgrade bleibt die Reaktion abwesend. Eine Reaktion mit Wert null
  an einem gesperrten Freiheitsgrad bleibt vorhanden.
- `EvaluateAt(position, side)` und `Extrema` bleiben unverändert in Signatur und Semantik,
  einschließlich der Gleichstandsregeln aus [SOLVER.md](SOLVER.md#globale-extremwerte).
- Das interne Modell ergänzt Werte links und rechts je Station (θ am
  Gelenk), Sprungarten und den Genauigkeitsbericht. Ob `Nodes` später
  umbenannt wird, entscheidet die Integration.

Nutzer heute: `ResultDiagramProjection`, `EditorViewModel.ResultProbe`, `BeamAnalysis`,
`BeamAnalysisResult`, `BeamEngineeringAnalysis`. Keiner benötigt FE-Interna.

## 10. Grenze Core ↔ Solver (A9)

- Ein **Adapter im Solverprojekt** baut je Rechnung aus dem `BeamModel` die mechanische Eingabe.
  Sie besteht aus generischen Primitiven: Halterung (Position, Richtungen w/θ/u, starr oder k > 0),
  Freigabe (Position, Art), Abschnittseigenschaft (Bereich, EI, EA), Querlast (Punktkraft,
  Punktmoment, linear verteilt mit zwei Randwerten), Axiallast (Punkt, verteilt) und die
  Teilpunktzuordnung. Die Eingabe ist unveränderlich und wird nicht gespeichert. Es entsteht kein
  verstecktes Nebenmodell.
- Freigegebene Features werden in Core modelliert. Ihr Core-Typ wird im Track festgelegt, der
  Adapter bildet ihn auf Primitive ab. Den Kern kennt nur Primitive.
- **Bis zur Freigabe** erreichen Tests die Primitive über einen internen Einstieg
  (`InternalsVisibleTo`); der Produktpfad baut sie nur aus Core. `SolverLoads` entfällt.
- Core bleibt frei von Numerik und prüft weiterhin identische Lagerduplikate.
  Die mechanischen Koinzidenzregeln des Solvers erhalten die heutige Semantik
  aus 4.4. Die Tracks liefern die fachlichen Regeln für neue Kombinationen.

## 11. Validierungsstrategie (A7)

**Übernahme in dauerhafte Abnahmefälle** (exakte Sollwerte, eingecheckt, CI ohne Python):

| Quelle | Fälle |
|---|---|
| Gezielte Matrix (`state/cases.py`) | jede Konfiguration bei L/10³, L/10⁷, L/10¹¹ und k·L³/EI = 1, 10⁶, 10¹² |
| `s_fuzz.py t` / `s_detail2` | Federtabelle (Paar, Nebenlager, nur Federn, elastische Einspannung, am Gelenk, drei, Drehpaar) |
| Zufall | je Klasse die schwierigsten gelieferten Modelle und alle abgelehnten (erwarteter Code; Sensitivität als Diagnose) |
| Anregung | die sechs Nullfälle aus `state/zero.py`, kompensierte Lasten, P und −P am selben Ort, Unterlauf 10⁻³¹⁰ und 10⁻³²⁰ N (erwartet `NumericalFailure`, Ursache `NumericalRange`) |
| Belasteter Axialpfad (DEV-12) | Zufallsmodelle aus `state/zero.py` gegen die exakte Axialreferenz `state/axial.py`, mit Gate (`state/axial_quality.py`) |
| Federn extrem | die 144 Fälle aus `state/spring_extremes.py` (k·L³/EI bis 10¹⁵, Abstände bis L/10¹¹) |
| Numerisch singulär | Balken nur auf zwei Federn im Abstand 5·10⁻¹² m (erwartet `IllConditionedSystem`), ergänzend die `ZeroPivot`-Fälle aus **[state-fpc-regress-f-1e-11]** |
| Grenzfälle nahe τ | die 75 Modelle aus `state/near_tau.py` (Gate 10⁻¹⁰ … 3·10⁻⁸), erwartete Entscheidung je Fall |
| Extremwerte | die 37 konstruierten Fälle aus `state/piece_study.py` (Wurzel am Rand, doppelte und fast doppelte Wurzeln, Extremum neben Ereignisstation, kurze Stücke, Skalen), Sollwerte aus exakter Wurzelisolation |
| Unterlauf | die 64 Fälle aus `state/underflow_study.py`, darunter der Kleinstbalken mit unterlaufender SI-Rückrechnung (erwartet `NumericalRange`) |
| Faktorisierung | Stressfamilien und die Modelle, an denen QR scheitert (`state/factor_study.py`, `state/cond_check.py`) |
| Gleitkomma-Vertrag | Stufen A–F aus `state/fpc_stage_a.py` und `state/fpc_chain.py` mit Sollwerten aus exakter Arithmetik. Formelfamilien mit strukturierten Grenzwerten und 685 Kettenläufe **[state-fpc-summary]**, darunter Fälle nahe τ, gestörte LU, Nulldurchgang, kurze schwere Lasten, EI-Wechsel 1 ulp neben einer Feder und das 6-nm-Paar. Die gezielten Unterlauffälle decken alle vier Unterlaufmodi ab. A5 dokumentiert den historischen Vergleich mit `state.py` [GV 9]. |
| Bestehend | V01–V15, 6-nm-Fall (`SixNanometerRenderingTests`), 35 CAD-Fälle aus `compare.py`, Prototyp-Tests (Gelenk, Federn, EI, Linearlast) |

Im heutigen Umfang werden Reaktionen je Station und Richtung geprüft.
Hinzu kommen w, θ, M und V an Stationen (beide Seiten) und im Feld sowie
die Extremwerte. Erzeugt werden die Sollwerte von beiden exakten Referenzen, mit `xcheck.py`
bitgleich gegeneinander geprüft. Für den Axialanteil gibt es bisher eine exakte Referenz
(`state/axial.py`). Die zweite unabhängige Referenz gehört zu DEV-12 (O4).

**Neue Architekturtests:**

- Phantomstation: Eine zusätzliche Last lässt mechanische Unbekannte, Matrix, matrixbezogene
  Rundungszahlen und Matrixskalierung bitgleich (Invariante 1).
- Strukturzählung: Gleichungen = Unbekannte, Bandbreite höchstens (2,2), Multiplikatoren höchstens 2
  unter der Diagonale (Invariante 2).
- Verifikation: mit gestörten LU-Faktoren liegen Fehler innerhalb der verifizierten Schranken (6.3, 7.5).
- Zweierpotenz-Skalierung bitgleich; allgemeiner Einheitenwechsel innerhalb des Vertrags (zwei
  getrennte Tests).
- Spiegelung x → L − x, Superposition, Reihenfolge der Eingabe: innerhalb des Vertrags.
- Sprungbilanzen an jeder Station: V⁺ − V⁻ = P + R, M⁺ − M⁻ = −(C + C_R), θ-Sprung nur am Gelenk;
  geprüft gegen die Summe der beteiligten verifizierten Schranken (4.2).
- Vertrag: Bei gelieferten Sollwertfällen liegt der tatsächliche Fehler innerhalb des `VerifiedOutputBound`,
  der höchstens τ mal der Bezugsskala beträgt (hart). Das Verhältnis von Ausgabeschranke zu tatsächlichem
  Fehler wird diagnostisch überwacht (7.5).
- Gate-Abdeckung: Ausgabeschranke ≤ Stückmajorante und LB_q ≤ |Q_exact(x)| an dichten Punkten; Monotonie im offenen
  Stück (7.3, 7.5).
- Nullanregung: exakte Nullen ohne Lösung, auch beim numerisch singulären Modell; Unterlauf der
  Anregung → `NumericalFailure`.
- Diagnosen ohne Rückwirkung: Lösung und Status mit und ohne Sensitivität bitgleich.
- Federreaktionen: Identitätsprüfung (4.2) mit eingebauten Fehlern; Wahl nach kleinerer verifizierter Schranke;
  weiche Feder zwischen zwei nahen Lagern und sehr steife Federn als Sollwertfälle.
- Fehlercodes: je Ursache aus 6.6 ein Fall; Fälle mit mehreren Ursachen prüfen die Reihenfolge aus
  6.1; ein Fall mit ω ≈ 1 und kleinem Gate wird geliefert.
- Adjungierte Nachiteration: mindestens ein Schritt, Schrittzahl ≤ 5, Abbruch nach der Änderung des adjungierten Rohterms.
- Aufwand: Zahl der adjungierten Lösungen = n + n_r, Bandbreite unabhängig von der Stationszahl,
  C#-Messung bei n_m = 10, 30, 100 und bei 1.000 Lasten in einem Abschnitt (5.6).
- Mechanismus: Bareiss-Urteil gleich exakter Singularität (Sollwerte).
- Unerreichbarkeit nicht freigegebener Primitive über `EulerBernoulliBeamSolver.Solve`.
- Zuordnung: 1-ulp-Abschnitt mit Eigenschaftswechsel; Last 1 ulp vor einer Station.

**Eingebaute Fehler:** Die Fehlerklassen aus `mutate2.py`/`mutate3.py` werden auf die neuen Regeln
übertragen (je Stationsregel, Übertragungskoeffizient, Lastintegral, Elimination, Nachiteration,
Indikator, Extrema). Jeder nicht äquivalente Mutant muss von dauerhaften Tests erkannt werden.

**Plattform.** Jede unterstützte Plattform muss auf den festen
Abnahmefällen dieselben fachlichen Entscheidungen und Fehlerklassen
erfüllen. Jeder gelieferte Wert muss innerhalb seiner verifizierten
Schranke und der mathematischen Toleranz liegen. Die Abnahme prüft
benötigte Primitives und Zwischenwerte im Release-Build auf x64 und
ARM64. Bitgleichheit ist Diagnose [GV 2, 13].


## 12. Migration (A10)

| Phase | Inhalt | Ende |
|---|---|---|
| M1 | Neuer Kern im Solverprojekt neben dem Machbarkeitsprototyp, interner Einstieg, Adapter für den heutigen Funktionsumfang | Heutiger Funktionsumfang ist über den neuen Kern abgedeckt |
| M2 | Sollwertfälle einschließlich axialer Nullausgabe und starrer Lagerkoinzidenz, Architekturtests, Mutationslauf; alter FE-Solver als Vergleich auf V01–V15 und den CAD-Fällen | Abnahme grün auf macOS und Windows |
| M3 | `EulerBernoulliBeamSolver.Solve` auf den neuen Kern; `BeamSolution`-Fassade; `SupportSpanSolver`, `SolverLoads`, `ReducedSystemSolver`, `DofMap`, `BeamAssembly`, `EulerBernoulliElement` verlassen den Produktpfad | ein Produktionssolver |
| M4 | [KONZEPT.md](KONZEPT.md), [SOLVER.md](SOLVER.md), [VALIDATION.md](VALIDATION.md), CI-Solver-Hash anpassen | Voraussetzungen für `main` erfüllt |
| M5 | Feature-Tracks nacheinander (neue Koinzidenzregeln, Core-Typ, Adapter, Freigabe) | je Track |

**Migrationsabnahme für starre Lager an gleicher Position.** Die bestehenden
Produkttests definieren die Referenzfälle für Abschnitt 4.4.

- `BeamModelTests.IdenticalSupportsAtTheSamePositionAreReported` prüft
  `DuplicateSupport`. `BeamModelTests.DifferentSupportTypesAtTheSamePositionAreNotIdentical`
  prüft die Zulässigkeit unterschiedlicher Lagertypen.
- `SolverStructureTests.CoincidentDifferentSupportsMergeRestraintsWithoutDuplicatingNodes`
  prüft den gemeinsamen Knoten und die Vereinigungsmenge der Sperren.
  `Fixed + Pinned + Roller` liefert dieselben Knotenergebnisse einschließlich
  der gemeinsamen Reaktionen wie `Fixed` allein.
- `SolverFailureTests.CoincidentPinnedAndRollerDoNotRestrainRigidRotation`
  prüft `UnstableModel` für die verbleibende Starrkörperrotation.

**Alter FE-Solver:** Er wird als eingefrorene Kopie in ein Test- oder Validierungsprojekt verschoben
und gehört nicht mehr zum Produkt. Er ist ein unabhängiges, *nicht exaktes* Verfahren für den
heutigen Funktionsumfang mit dokumentiertem Gültigkeitsbereich: gut getrennte Stationen,
Abweichung bei engen Stationen bekannt **[compare-rand]**. Er dient als Differenzialtest im Sinne
von A7 „Fremdsolver“, wird nicht weiterentwickelt und bekommt keine neuen Features. Der Prototyp wird
nach M3 entfernt; seine Testfälle bleiben als Sollwertfälle erhalten.

## 13. Offene Entscheidungen und Risiken

| Nr. | Punkt | Art |
|---|---|---|
| O1 | Exakter Produktwert τ = 1/100_000_000, gerichteter SI-Vergleich [GV 7] | Geschlossen |
| O2 | C#-Konformität mit dem Gleitkomma-Vertrag und seinen Fehlerklassen, nachzuweisen mit A–F und exakten Referenzen [GV 9, 13] | C#-Abnahme |
| O3 | Pessimistische Schranken können korrekte Fälle ablehnen | Akzeptiertes Risiko |
| O4 | Belasteter Axialvertrag, zweite unabhängige Referenz und Axialfedergesetz. Die heutige axiale Nullausgabe gehört zur Migration | DEV-12 |
| O5 | Variante B. PolynomialRoots bleibt unabhängig validiert, exakte Yun/Sturm-Isolation bleibt Oracle [GV 11] | Geschlossen |
| O6 | Fachliche Semantik neuer Feature-Koinzidenzen je Track. Starre Lager am selben Ort gehören zum Migrationsvertrag (4.4) | Feature-Track |
| O7 | N-Extrema in der Schnittstelle (DEV-12) | Track |
| O8 | Eingabesensitivität anzeigen ja/nein | Produkt |
| O9 | Modellgrößen- und Latenzgrenze nach C#-Messung festlegen. [GV 12] liefert eine Größenordnungsschätzung | Produkt nach C#-Messung |
| O10 | Keine Federgruppierung. Umsetzung an den Federextremen bestätigen | C#-Abnahme |
| O11 | Umbenennung von `IllConditionedSystem` / `Nodes` | Integration |
| O12 | Jede Plattform erfüllt fachliche Entscheidungen, Schranken, τ und Fehlerklassifikation unabhängig. Bitvergleiche dienen der Diagnose | C#-Abnahme |
| O13 | Geschlossen (Runde 4): 75 gezielte Grenzfälle, 41 mit Gate in (10⁻¹⁰, 10⁻⁸], kein gelieferter Fehler > τ, Gate/Fehler ≥ 11,8 (7.6). Die Fälle werden in die Abnahme übernommen | – |
| O14 | Anregungsskala für spätere Anregungsarten (Lagersenkung, Temperatur) je Track konkret festlegen. Für jeden neuen Lasttyp die Stückmajorante des `VerifiedOutputBound` und die Schranke für die Änderung von \|Q\| im Stück festlegen (7.3) | Track |
| O15 | Geschlossen (Runde 4). Bei überwiegendem Residual war das damalige β scharf. Bei 10 % gestörter Faktorisierung lag der Fehler in jedem verifizierten Modell unter β. Seit Runde 5 gilt der Nachweis mit den getrennten Schranken aus 6.3. | – |
| O16 | Geschlossen (Runde 4, präzisiert Runde 5): Verifikation der adjungierten Lösungen nach Rump (2010, Satz 10.2), μ < 1 nach oben gerundet, Gewichte mit Neumann-Schritten, sonst Ablehnung mit Ursache `NotVerified` (6.3, [GV 6, 10]) | – |
| O17 | Nichtlokale Kopplungen oder exponentielle Zustandsübertragung lösen eine Neubewertung im jeweiligen Track aus | Feature-Track |
| O18 | Gradueller und abrupter Unterlauf sind im Vertrag hergeleitet [GV 3, 6, 7] | Geschlossen |
| O19 | Gerichtete Primitives, Bereichsgrenzen und benötigte Zwischenwerte im Release-Build auf x64 und ARM64 prüfen [GV 2] | C#-Abnahme |
| O20 | Gerichtete Rundung der Schranken (L5) und Software-Flush sind neue Implementierungspflichten; ihre Kosten sind in [GV 12] nicht gesondert gemessen | Implementierung |

## 14. Abgleich mit A1–A10 und den Feature-Tracks

| Anforderung | Erfüllung im Entwurf | Nachweis / offen |
|---|---|---|
| A1 Stationen und Unbekannte | E2, E3, Invariante 1, 2 | Phantomstationstest; gezielte Matrix |
| A2 Positionen | Invariante 3, 4; E5 Teilpunkte mit Koinzidenztabelle | Verletzungsanalyse 6.4; starre Lagerkoinzidenz 4.4 und 12; neue Kombinationen O6 |
| A3 Modell und Numerik | Invariante 5; nur exakte Skalierungen | Ablation |
| A4 Gemeinsame Mechanik | Eine Formulierung, Tabelle 4.2, Axial 4.3, Erweiterungen 4.5 | Axialreferenz O4 |
| A5 Stabilität, Kondition, Fehler | 6.1–6.6; vier Fehlercodes getrennt; vollständige Code-Zuordnung mit strukturierter Ursache (6.6, `NotVerified` neu); Schranke unter dem Gleitkomma-Vertrag und Verifikation statt Pauschalgrenze; ω, Kondition und Sensitivität nur Diagnose; Selbsttests → `NumericalFailure`; Bereichs- und Subnormalvertrag unabhängig von FTZ/DAZ, auch für die SI-Rückrechnung; Literaturabgleich jeder Regel ([Audit]) | Ablehnungen 7.6; O2 (C#-Konformität), O3, O17, O19 |
| A6 Felder und Extremwerte | Abschnitt 8, beide θ am Gelenk, Sprungarten; Extremwerte gegen exakte Wurzelisolation geprüft | Vollständigkeit validiert, nicht zertifiziert (O5, Variante B beschlossen); N-Extrema O7 |
| A7 Validierung | Abschnitt 11, zwei exakte Referenzen, Mutationen, exakte Oracles für Stück-Gate und Extremwerte | Belastetes Axialmodell O4 |
| A8 Numerischer Vertrag | Abschnitt 7 und Gleitkomma-Vertrag: Anregungsskala und Nullfall, Bereichsregeln einschließlich SI-Rückskalierung, Gate über den ganzen Balken mit Majorante und geschlossener Untergrenze (unabhängig gegen exakte Bernstein-Einschließung geprüft), s = 1 auch nahe τ belegt. `VerifiedOutputBound` gilt auch am ausgegebenen Extremwertkandidaten. Positionsfehler und Vollständigkeit werden getrennt validiert (7.3). Zur Migration gehört die axiale Nullsemantik (4.3). | O1 und O5 geschlossen, C#-Abnahme O2 |
| A9 Grenze Fachmodell | Abschnitt 10 | – |
| A10 Migration | Abschnitte 9, 12 | Plattformkonformität O12 |
| DEV-20 Gelenke | Momentengelenk als Stationsregel; θ links/rechts; exakte Mechanismusprüfung über Teilkörper | Koinzidenz O6 |
| DEV-19 Federlager | Weg-, Dreh-, Kombi-Federn als Gesetz auf dem lokalen Zustand; keine Gruppierung; Reaktion aus Sprung oder Federgesetz nach kleinerem `VerifiedOutputBound`, Identität beider Wege als Selbsttest (4.2); k = 0 entscheidet der Track | O10; axiale Feder als Regel möglich, Federgesetz axial O4; Feder an starrem Lager O6 |
| DEV-11 Mehrsegment | Abschnittswechsel als mechanische Station mit EI und EA; Zuordnung über Stationsfolge | Wechsel an Halterung O6 |
| DEV-6 Linearlasten | Lastintegrale `Difference`/`Ramp` mit globalen Abständen; Querkraftextremum im Feld | Extrema O5; ein oder zwei Lasttypen ist Core-Frage |
| DEV-12 Axial/schräg | Entkoppeltes (u, N)-System mit eigenem Qualitätsnachweis und Gate (4.3); Zerlegung im Adapter außerhalb der Vertragsgrenze | Axialreferenz O4; N-Extrema O7 |

## 15. Closure und eingefrorener Stand

Die [Closure-Kriterien C1–C13](#closure-kriterien-c1c13) sind für den
Architekturentwurf erfüllt. Ebene A ist für die C#-Implementierungsplanung
des heutigen öffentlichen Funktionsumfangs einschließlich starrer
Lagerkoinzidenz und axialer Nullsemantik eingefroren. Die C#-Umsetzung und
ihre fachliche und numerische Abnahme je unterstützter Plattform stehen aus.
Der belastete Axialvertrag und neue Feature-Koinzidenzen bleiben in ihren
Feature-Tracks.

### Closure-Kriterien C1–C13

Der [Gleitkomma-Vertrag][closure-gv] definiert die Beweiskette A–F. Das
[Closure-Protokoll][closure-summary] indiziert die Formel-, Ketten- und
Regressionsläufe. Die [Boundary-Suite][closure-boundary] prüft Grenzfälle
und Mutanten. Die [Oracle-Gegenproben][closure-oracles] vergleichen
unabhängige exakte Referenzen und die Wurzelisolation. Die
[axiale Produktbaseline][closure-axial] belegt die Nullsemantik am
bestehenden öffentlichen Pfad.

| Kriterium | Maßstab | Nachweis |
|---|---|---|
| C1 | Keine bestätigte Schrankenverletzung in der Kette A–F. | [Closure-Protokoll][closure-summary], [Oracle-Gegenproben][closure-oracles] |
| C2 | Kein gelieferter Fall hat einen tatsächlichen normierten Fehler über dem mathematischen τ = 10⁻⁸. | Kettenläufe einschließlich Fällen nahe τ und Regression im [Closure-Protokoll][closure-summary] |
| C3 | Sichere Anregungsnorm. Exakter Nullanregungstest getrennt von `F_exc`. Kein stiller Unter- oder Überlauf. | [GV 5, 7][closure-gv], `excitation` und `full_cases` in der [Boundary-Suite][closure-boundary] |
| C4 | Exponentenbasierte Zweierpotenzskalierung mit expliziten Bereichsregeln. | [GV 3, 5, 7][closure-gv], `exponent` und `functional_scaling` in der [Boundary-Suite][closure-boundary] |
| C5 | Gerichtete Schranken sind auch an ±λ konservativ. Gespeicherte Schranken sind frei von Subnormalen. | [GV 3][closure-gv], `direction` in der [Boundary-Suite][closure-boundary] |
| C6 | `GammaUpper` ist konservativ nach außen gerundet und prüft seinen Gültigkeitsbereich. | [GV 3][closure-gv], `gamma` in der [Boundary-Suite][closure-boundary] |
| C7 | Der Residualpfad ist hergeleitet und für graduellen Unterlauf, FTZ, DAZ und deren Kombination geprüft. | [GV 6, 9][closure-gv], `residual` in der [Boundary-Suite][closure-boundary] und Unterlaufläufe im [Closure-Protokoll][closure-summary] |
| C8 | `RawBound`, `VerifiedStateBound` und `VerifiedOutputBound` sind eindeutig getrennt. | [GV 1, 4, 6][closure-gv], getrennte Prüfungen in den Stufen B–F im [Closure-Protokoll][closure-summary] |
| C9 | Numerische Konstanten sind als Korrektheitsgrenze, Range-Policy oder Strategie klassifiziert. | [GV 8][closure-gv] |
| C10 | Die deterministische Boundary-Suite erkennt die geforderten Primitive-Mutanten (`outward`, `lambda`, `exponent`, `contribution`, `gamma`, `unloaded`) und Mechanikmutanten (B3, L1, L2). | [Boundary-Suite][closure-boundary] |
| C11 | Architektur, Gleitkomma-Vertrag, ausführbare Referenz und Closure-Evidenz sind widerspruchsfrei. | Abgleich mit [GV 4–9][closure-gv], [`state/fpc.py`](../validation/investigations/solver-rework/state/fpc.py), [Closure-Protokoll][closure-summary] und [Oracle-Gegenproben][closure-oracles] |
| C12 | Die Plattformzusage verlangt fachliche und numerische Konformität. Plattformübergreifende Bitvergleiche dienen der Diagnose. | [GV 2, 13][closure-gv], C#-Abnahme in Abschnitt 11 |
| C13 | Für den heutigen Migrationsumfang ist kein Architektur- oder Floating-Point-Blocker bekannt. Offen bleiben die ausdrücklich verschobenen Feature-Tracks, Produktentscheidungen und C#-Abnahmen. | Migrationsumfang in Abschnitten 4.3, 4.4 und 12, [axiale Produktbaseline][closure-axial], offene Punkte in Abschnitt 13 |

[closure-gv]: ../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md
[closure-summary]: ../validation/investigations/solver-rework/results/state-fpc-summary.log
[closure-boundary]: ../validation/investigations/solver-rework/results/state-fpc-boundary.log
[closure-oracles]: ../validation/investigations/solver-rework/results/state-fpc-oracles.log
[closure-axial]: ../validation/investigations/solver-rework/results/state-fpc-axial-baseline.log

### Wiedereröffnung

Die folgenden Ereignisse können den Entwurf wieder öffnen.

1. Ein konkreter gelieferter Wert verletzt seine zugesagte Schranke oder τ.
2. Eine mathematische Voraussetzung der Beweiskette erweist sich als falsch.
3. Eine notwendige Primitive ist in C# auf einer unterstützten Plattform
   nicht zuverlässig realisierbar.
4. Ein späteres Feature verletzt eine dokumentierte Architekturvoraussetzung,
   etwa lokale Kopplung oder polynomiale Zustandsübertragung.
