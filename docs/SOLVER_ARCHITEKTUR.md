# SpanDraft – Solver-Architektur (Ebene A)

Stand: 10. November 2026. Architekturentwurf für den Branch `features/solver-umbau`, **nicht
freigegeben**; die Freigabe erfolgt nach einem gesonderten Review. Die fachliche
Grundlage ist [SOLVER_UMBAU.md](SOLVER_UMBAU.md); dieses Dokument legt fest, *wie* der Rechenkern die
Anforderungen A1–A10 erfüllt. Es gibt keine Produktfunktion frei und legt keine Core-Typen der
Feature-Tracks fest. Der heutige Produktionssolver ist in [SOLVER.md](SOLVER.md) beschrieben.

Alle Zahlen in diesem Dokument stammen aus reproduzierbaren Läufen in
[`validation/investigations/solver-rework/`](../validation/investigations/solver-rework/README.md);
Kennzeichnung: **[Protokollname]** verweist auf `results/<Name>.log`. Die Läufe der neuen
Formulierung (`state-*`) sind eine Gleitkomma-Implementierung in Python (IEEE double wie .NET), gemessen
gegen die exakte Bruchrechnungsreferenz `transfer_ref.py`. Es sind Belege für die Architektur,
keine Abnahme des späteren C#-Codes.

Die numerischen Entscheidungen sind mit der etablierten Theorie zu Mehrfachschießen,
Randwertproblemen und linearer Algebra abgeglichen:
[`LITERATURE_AUDIT.md`](../validation/investigations/solver-rework/LITERATURE_AUDIT.md) enthält die
Quellen mit DOI, ihre Voraussetzungen, die Übertragbarkeit und für jede Regel den Status
(übernommen, angepasst, bewusste Abweichung, offen). Verweise darauf stehen als [Audit 3.x].

Welche Gleitkommaoperationen der Kern für jede Formel ausführt, welche Rundungs- und Bereichsfehler
daraus folgen und unter welchen Voraussetzungen β eine Schranke ist, legt der Gleitkomma-Vertrag
[`FLOATING_POINT_CONTRACT.md`](../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md)
fest (O2, Runde 5). Verweise darauf stehen als [GV x].

## 1. Entscheidungen im Überblick

| Nr. | Thema | Entscheidung |
|---|---|---|
| E1 | Formulierung | **Lokale Zustandsformulierung** über mechanische Abschnitte (Mehrfachschießen). Das Schnittverfahren des Prototyps wird nicht übernommen. |
| E2 | Stationen und Unbekannte | Ereignisstationen (alle Positionen) bestimmen Felder und Ausgaben. Unbekannte sind nur die *freien* Komponenten des rechtsseitigen Zustands (w, θ, M, V) an *mechanischen* Stationen. |
| E3 | Vorgeschriebene Größen | w = 0, θ = 0, M = 0 rechts am Gelenk, M = V = 0 am Ende sind bekannte Werte und werden eliminiert, nicht als Gleichungszeilen gelöst. |
| E4 | Reaktionen | Werden nachträglich als Sprung bestimmt und mit einer eigenen adjungierten Lösung bewertet; sie sind keine Unbekannten. Bei elastischen Halterungen gibt es zusätzlich das Federgesetz. Beide Wege unterscheiden sich exakt um das Residual der Federzeile; geliefert wird der Wert mit dem kleineren Indikator, und die Identität beider Wege ist ein interner Selbsttest (Abschnitt 4.2). |
| E5 | Gleiche Position | Station mit geordneten **Teilpunkten**, verbunden durch einen algebraischen Stationsübergang. Jede Anbindung, auch Punktkraft und Punktmoment, gehört zu einem Teilpunkt. Nicht definierte Kombinationen werden abgelehnt. |
| E6 | Federgruppen | Keine geometrische Gruppierung und keine Newton-Multipole. Starke Evidenz (Abschnitt 5.4), noch kein Beweis. |
| E7 | Abstände | Jeder Abstand wird direkt aus zwei gespeicherten globalen Koordinaten gebildet. Abschnittseigenschaften werden nur über die Stationsfolge zugeordnet. |
| E8 | Skalierungskette | Entdimensionierung mit Zweierpotenzen (nur Geometrie und Steifigkeit) und Normierung der rechten Seite mit einer Zweierpotenz → Ruiz-Äquilibrierung mit Zweierpotenzen → LU mit Teilpivotisierung (Wahl begründet: Band (2,2), nur lokale Kopplung, gemessenes Pivotwachstum, 5.2) → adaptive Nachiteration mit Pflichtschritt → a-posteriori-Fehlerschranke mit Verifikation unter den Voraussetzungen des Gleitkomma-Vertrags. Householder-QR ist für diese graduierten Matrizen keine robustere Alternative. |
| E9 | Mechanismus | Exakter Rang auf dem kinematischen Modell der Teilkörper (Bareiss), unabhängig von der Lösungsmatrix. |
| E10 | Numerische Qualität | **Komponentenweise** A-posteriori-Fehlerschranke β aus adjungierten Lösungen mit Unterlaufterm (komponentenweise Form der `xGERFS`-Schranke). β ist eine obere Fehlerschranke unter zwei Voraussetzungen: Das Rundungsmodell des Gleitkomma-Vertrags gilt für die ausgeführte Operationsfolge (Aufbau, Lastintegrale, Lösung, Auswertung), und die adjungierten Lösungen sind verifiziert (Rump 2010, Satz 10.2; μ < 1, Gewichte mit Neumann-Schritten). Ist die Verifikation nicht erfolgreich, wird abgelehnt. Adjungierte Nachiteration: mindestens ein Schritt, weitere nur, solange sich β um mehr als 1/16 ändert, höchstens 5. Ein Stück-Gate mit geschlossener Untergrenze für \|M\| und \|V\| deckt den ganzen Balken ab, ohne Nullstellensuche. Geliefert wird bei Gate ≤ τ (Akzeptanzfaktor s = 1). Weder Konditions- noch Rückwärtsfehlergrenze; ω ist keine Entscheidungsgröße. |
| E11 | Eingabesensitivität | Nur Diagnose; ändert weder Lösung noch Status. |
| E12 | Ergebnis | Internes Ergebnismodell (Stationen mit Seiten und Sprungarten, Stücke, Reaktionen je Halterung, Genauigkeitsbericht). `BeamSolution` wird zur Fassade des heutigen Vertrags. |
| E13 | Grenze Core ↔ Solver | Transiente mechanische Eingabe aus generischen Primitiven, je Rechnung aus dem Fachmodell gebaut. `SolverLoads` entfällt. |
| E14 | Alter FE-Solver | Nach der Migration eingefrorene Testreferenz außerhalb des Produktpfads. |
| E15 | Anregung | Definiert über die Solvereingabe (Summe der Beträge aller Anregungsprimitive), nicht über Resultierende. Ohne Anregung: exakt null, kein relatives Maß. |
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
  │                          Nachiteration; gespeicherte Vektoren ohne Subnormale
  │                          Nullpivot → NotReliablySolvable; subnormaler Pivot → NumericalFailure
  ▼
7 Qualitätsbewertung         adjungierte Lösungen (n + n_r) mit Residualschranke → β je Größe
  │                          → Selbsttests (endlich, Federreaktions-Identität) → NumericalFailure,
  │                          falls verletzt → SI-Bezugsgrößen im Bereich [2⁻⁹⁶⁹, 2¹⁰⁰⁰], sonst
  │                          NumericalFailure (NumericalRange) → Verifikation der adjungierten
  │                          Lösungen (μ < 1, 6.3), sonst NotReliablySolvable (NotVerified) →
  │                          Stück-Gate über den ganzen Balken mit rigorosen Untergrenzen der
  │                          Bezugsgrößen → NotReliablySolvable, falls > τ
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

Biegung und Axialanteil durchlaufen 3–9 getrennt (Theorie 1. Ordnung, entkoppelt), mit
demselben Stationsmodell.

### 3.3 Invarianten

1. **Keine künstliche Steifigkeit.** Eine Ereignisstation ohne mechanische Wirkung ändert weder
   Unbekannte noch Gleichungsmatrix. Hinzufügen oder Entfernen einer Last lässt Matrix,
   Rundungszahlen, Skalierung und Unbekannte bitgleich (A1; gemessen in 226 von 226 Modellen
   **[state-invariance]**).
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
   Skalierungen sind Zweierpotenzen und damit exakt (A3).
6. **Mechanismus.** Mechanismus genau dann, wenn der exakte Rang der Teilkörper-Kinematik
   unvollständig ist; ohne Toleranz (A5).
7. **Kein stilles Ergebnis.** Ein Ergebnis wird nur geliefert, wenn alle Bereichsregeln erfüllt
   sind, die adjungierten Lösungen verifiziert sind und das Gate über den ganzen Balken ≤ τ ist.
   Danach hat jedes `EvaluateAt(x)` ein normiertes β ≤ Gate (A5, A8, Abschnitt 7.3). Das ist eine
   Eigenschaft der Konstruktion. Dass damit auch der tatsächliche Fehler ≤ τ ist, folgt aus einer
   Beweiskette: (1) Das Rundungsmodell liefert für die festgelegte Operationsfolge eine gültige
   komponentenweise Schranke |r_exakt| ≤ d. (2) Die Verifikation schließt die exakte Lösung in
   ŷ ± E ein. (3) Daraus folgt die Schranke für jedes Funktional und jeden Feldwert. Die Kette ist
   unter den Plattformvoraussetzungen V1–V6 hergeleitet [GV 2–4] und auf jeder Stufe gegen exakte
   Arithmetik geprüft [GV 9]. Sie ist ein Papierbeweis, kein maschinell geprüfter.
8. **Unerreichbarkeit.** Rechenlogik nicht freigegebener Features ist über Produktpfade nicht
   erreichbar (B).
9. **Diagnosen ohne Rückwirkung.** Diagnosen ändern weder Lösung noch Indikator noch Status.
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

**Reaktionen** sind Sprünge: R = V⁺ − V⁻ − P bzw. C_R = M⁻ − M⁺ − C. Ihr Fehlerindikator kommt aus
*einer* adjungierten Lösung für das Funktional V⁺ − V⁻ (Abschnitt 6.3). Die Fehler beider Seiten
sind korreliert, und die Summe der Einzelindikatoren überschätzt um bis zu 10¹² (Feder zwischen zwei
Lagern im Abstand 4 nm, Querkräfte 8,7·10¹¹ N auf beiden Seiten).

**Elastische Halterungen: zwei Wege, eine Identität.** Für eine Feder gibt es eine zweite
Auswertung, das Federgesetz R_law = −k·w⁺ bzw. C_law = −k_θ·θ⁺. Die Federzeile p des Systems lautet
c_jumpᵀx − c_lawᵀx = b_p. Daraus folgt für *jede* berechnete Lösung, in exakter Arithmetik:

```text
R_jump − R_law = −r_p                       (Residual der gespeicherten Federzeile, Krafteinheit)
z_jump − z_law = ±e_p / rs_p                (adjungierte Lösungen Sᵀz = C·c, rs_p Zeilenskalierung)
```

Die beiden Wege sind also nicht unabhängig. Ihre Differenz ist das Residual der Federzeile, und ihre
Fehler unterscheiden sich nur durch den Beitrag dieser einen Zeile. Folgerungen:

1. **Die Relation |R_jump − R_law| ≤ β_jump + β_law ist herleitbar, nicht nur empirisch.** Mit
   d_p ≥ |r̂_p| + δr_p gilt |R_jump − R_law| ≤ (|r̂_p| + δr_p)/rs_p + Auswertungsrundung
   ≤ |z_jump − z_law|ᵀd ≤ β_jump + β_law. Für die berechneten adjungierten Lösungen genügt dabei,
   dass die p-Komponente von z_jump − z_law stimmt; gemessen ist rs_p·|z_jump − z_law|_p = 1,0000 in
   allen Halterungen. Das gilt für jedes Rundungsmodell, denn die Datenrundung geht nicht ein. Die
   Relation prüft daher die Implementierung, **nicht** die Güte von β.
   Gemessen auf 10.322 Modellen mit 25.504 elastischen Halterungen (16.847 Weg-, 8.657 Drehfedern,
   6.153 davon kombiniert): Das Verhältnis erreicht höchstens 1,000. Ebenso bleibt es bei 1,000, wenn
   man d auf |r̂| + δr kürzt, also das Rundungsmodell ganz weglässt **[state-spring-rule-summary]**.
2. **Produktregel: Identitätsprüfung als Selbsttest.** Geprüft wird die scharfe Form ohne
   adjungierte Lösung: |D − |r̂_p|/rs_p| ≤ δr_p/rs_p + γ(m)·(Σ|Terme des Sprungs| + |k·w⁺|), mit
   D = |R_jump − R_law|. Sie hängt nur von IEEE-Rundung ab und war nie verletzt (höchstens 10 % der
   Schranke). Ihre Verletzung bedeutet, dass Gleichungsaufbau und Reaktionsauswertung nicht dieselben
   Koeffizienten benutzen. Das ist ein Implementierungsfehler und nie schlechte Kondition; Folge ist
   `NumericalFailure` mit Ursache `InternalInconsistency` (6.1, Schritt 6). Sie ist weder Diagnose
   noch `IllConditionedSystem`. Die adjungierte Identität z_jump − z_law = ±e_p/rs_p gilt abseits von
   p nur bis auf die Rundung der beiden Lösungen: In 39 von 25.484 Halterungen gelieferter Modelle
   liegt die Abweichung über 10⁻⁶·|z_jump − z_law|_p, höchstens beim 83-Fachen. Sie eignet sich
   deshalb nicht als Prüfung.
3. **Selbsttest, kein Ersatz für Tests.** Eingebaute Fehler: k im Gesetz um 2⁻²⁰ verfälscht → 91,9 %
   erkannt; fehlende Stationslast im Sprung → 100 %; fehlendes Abschnittslastintegral → 97,6 %. Nicht
   erkannt werden Fehler unterhalb der Rundungsschranke der Zeile. Die Prüfung kostet O(1) je Feder,
   weil beide Werte ohnehin berechnet werden.
4. **Geliefert wird der Wert mit dem kleineren β; bei Gleichheit der Sprung.** Beide Werte sind
   exakte Auswertungen derselben Größe. Der Wert mit dem kleineren β ist der weniger empfindliche
   gegenüber dem Residual der Federzeile. Er geht mit *seinem* β in das Gate ein; eine erkennbare
   Inkonsistenz kann die Wahl nicht verdecken, denn diese ist bereits Gegenstand der
   Identitätsprüfung. Kleineres β ist kein Orakel für den tatsächlichen Fehler: In 57 Halterungen
   gelieferter Modelle ist der andere Wert genauer, höchstens um den Faktor 2,5, beide unter ihrem β.
5. **Feste Regeln im Vergleich:**
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

**Sprungbilanz:** Mit R = R_law ist V⁺ − V⁻ − P = R nur bis auf D erfüllt. D liegt innerhalb der
Indikatoren, ist aber relativ zu max(|R|, S_V) nicht durch τ beschränkt, wenn |V±| ≫ |R| ist. Die
Sprungbilanz ist deshalb keine eigene Zusage; Tests prüfen sie gegen β_jump + β_law (Abschnitt 11).

### 4.3 Axialanteil

Gleiche Struktur mit dem Zustand (u, N): EA·u' = N, N' = −n. Axiale Halterung schreibt u⁺ = 0 vor,
eine Axialfeder (falls DEV-19 sie einführt) lautet N⁺ − N⁻ − k_a·u⁺ = −P_x. EA-Wechsel sind
Abschnittswechsel. Die Momentenfreigabe überträgt N. Das System ist eigenständig und klein.
Gemessen: `state/axial.py` enthält die Formulierung (u, N) und eine unabhängige exakte Referenz
in Bruchrechnung (Einfachschießen mit u(0) und allen Halterungskräften als Unbekannten). Auf je 300
Zufallsmodellen bei L/10², L/10⁶ und L/10¹¹ (nahe Halterungen, Axialfedern, EA-Wechsel, kurze
Streckenlasten) liegt der größte Fehler bei 3·10⁻¹⁶ **[state-zero]**. Ohne axiale Halterung hat der
Kern einen Nullpivot, die Referenz meldet einen Mechanismus.

**Qualitätsvertrag axial.** Bis zu dieser Runde hatte der Untersuchungscode für (u, N) keinen
Indikator, obwohl der Vertrag (7.2, 7.3) u, N und Axialreaktionen einschließt. `state/axial_quality.py`
ergänzt denselben komponentenweisen Indikator: Rundungszahlen je Eintrag, adjungierte Lösungen mit
genau einem Nachiterationsschritt (die β-Regel aus 6.3 ist dort nicht eigens gemessen), Reaktionen
als Sprung mit eigener adjungierter Lösung. Dazu kommt das
Stück-Gate mit der geschlossenen Untergrenze für |N|. Auf denselben 900 Zufallsmodellen und drei
Grenzfällen (Halterungen im Abstand 10⁻¹¹ L, steife und weiche Feder, Streckenlast der Länge
10⁻¹¹ L): alle geliefert, Gate höchstens 1,8·10⁻¹⁴, tatsächlicher Fehler höchstens 3,8·10⁻¹⁶, keine
Unterschätzung. Gate/Fehler liegt im 1. Perzentil bei 6, im Median bei 34 **[state-axial-quality]**.
Für eine Axialfeder ist nur die Sprungauswertung untersucht; das Federgesetz als zweiter Weg folgt
derselben Identität wie in 4.2, ist aber nicht gemessen. Offen bleibt eine zweite unabhängige
exakte Axialreferenz für A7 (O4).

### 4.4 Gleiche Position und Teilpunkte

Eine Station mit exakt gleicher Koordinate besteht aus geordneten Teilpunkten T₁ … T_m. Jede
Anbindung (Halterung, Freigabe, Abschnittsgrenze, Punktkraft, Punktmoment) gehört genau einem
Teilpunkt. Zwischen T_i und T_{i+1} gilt der algebraische Übergang „Zustand rechts von T_i =
Zustand links von T_{i+1}“, ohne Länge und ohne Material. Jeder Teilpunkt mit mechanischer Wirkung
trägt seinen eigenen rechtsseitigen Zustand. Damit lassen sich alle offenen Semantiken aus B als
**Daten** ausdrücken:

| Fall (Track) | Ausdruck im Teilpunktmodell |
|---|---|
| Punktmoment genau auf Gelenk (DEV-20) | Moment links oder rechts des Gelenkteilpunkts, oder Ablehnung |
| Gelenk an Einspannung (DEV-20) | Einspannung links oder rechts des Gelenks, oder Ablehnung |
| Gelenk auf Lager ohne Drehbehinderung (DEV-20) | Lager und Gelenk am selben oder an getrennten Teilpunkten |
| Feder und starres Lager an gleicher Position (DEV-19) | Ablehnung, oder beide am selben Teilpunkt mit definierter Reaktionsaufteilung |
| Abschnittswechsel an Lager, Gelenk, Einspannung (DEV-11) | Grenze vor oder nach dem Teilpunkt der Halterung |
| Schräge Kraft am Gelenk oder Lager (DEV-12) | Axial- und Querkomponente am gewählten Teilpunkt |

Die Koinzidenzregeln sind eine Tabelle zulässiger Kombinationen samt Reihenfolge. Fehlt ein
Eintrag, wird mit eigenem Fehlercode abgelehnt (A2). Der Feature-Track füllt die Tabelle, der Kern
bleibt unverändert. **Gemessen** sind bisher nur die Kombinationen, die `transfer_ref.py` mit
fester Reihenfolge abbildet: Gelenk auf Lager, Kraft auf Gelenk, Kraft und Moment auf Feder,
EI-Wechsel auf Gelenk und Feder; alle ≤ 4,5·10⁻¹⁵ **[state-targeted]**.

### 4.5 Erweiterungspunkte

Neue Elemente sind neue Stationsregeln oder Abschnittslasten, kein neuer Kern:
Lagersenkung (vorgeschriebener Wert ≠ 0), Querkraftgelenk (w-Sprung frei, V⁺ = 0), Temperatur-
krümmung DEV-27 (Abschnittslast im θ- und w-Integral), Axialfeder. Jede neue Regel muss
Invariante 2 erfüllen und braucht eine eigene Zeile in der exakten Referenz. Jede neue
Abschnittslast braucht zusätzlich ihre Rundungszahlen, eine β-Majorante je Stück und eine Schranke
für die Änderung von |M|, |V| bzw. |N| im Stück (7.3), sonst deckt das Gate sie nicht ab.

## 5. Numerische Strategie

### 5.1 Entdimensionierung und Äquilibrierung

Die Skalierung hängt nur von Geometrie und Steifigkeit ab, nicht von den Lasten. Sie funktioniert
also auch beim unbelasteten System.

```text
L_ref = 2^round(log2 L)      EI_ref = 2^round(log2 max EI)      EA_ref = 2^round(log2 max EA)
w/L_ref   θ   M·L_ref/EI_ref   V·L_ref²/EI_ref        u/L_ref   N/EA_ref
k·L_ref³/EI_ref   k_θ·L_ref/EI_ref   k_a·L_ref/EA_ref   (Lasten entsprechend)
```

**Normierung der rechten Seite (Runde 5).** Zusätzlich wird die rechte Seite mit einer
Zweierpotenz 2^−e_F skaliert, sodass die Anregung F_exc (7.2) entdimensioniert in [1, 2) liegt. Die
Matrix bleibt unverändert und lastunabhängig; ohne Anregung entfällt der Schritt (7.2). Weil das
Problem linear ist, skaliert die Lösung exakt mit. Zweck: Alle Bezugsgrößen sind dann in
normierten Einheiten von der Größenordnung 1. Damit kostet der plattformunabhängige Unterlaufterm
λ = 2⁻¹⁰²² je Operation nichts Messbares (6.3, [GV 5, 8]), und große oder kleine Lasten können
nicht über- oder unterlaufen.

Weil alle Faktoren Zweierpotenzen sind, ist die Entdimensionierung exakt. Eine reine
Zweierpotenz-Umskalierung des Modells liefert deshalb bitgleich umskalierte Ergebnisse
(**[state-invariance]**). Danach folgt die Ruiz-Äquilibrierung von Zeilen und Spalten mit auf
Zweierpotenzen gerundeten Faktoren, ebenfalls exakt. Das entspricht der Ruiz-Iteration (Ruiz
2001) mit Faktoren als Basispotenzen wie in LAPACK `xGEEQUB` („Barring over- and underflow,
scaling by these factors introduces no additional rounding errors“). Sie ist für die Genauigkeit
meist neutral, schützt aber einzelne Fälle vor Stellenverlust in der Pivotwahl (Abschnitt 5.5).
Eine Konditionsgarantie gibt sie nicht: Die Gradierung *innerhalb* einer Spalte bleibt, normweise
bis κ∞ = 10³³ bei gutartiger komponentenweiser Kondition [Audit 3.1]. Unterlauf durch die Skalierung
trat in gezielten Fällen mit Abschnitten bis 2⁻³³⁵ L nicht auf **[state-underflow]**.

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
  ρ = max|a⁽ᵏ⁾|/max|a| ≤ 1,97 **[state-factor-summary]**. Wichtig für den Vertrag: Die Richtigkeit
  von β hängt nicht von der Stabilität der Faktorisierung ab. β misst das Residual der gespeicherten
  Lösung, und die Verifikation erfasst ungenaue adjungierte Lösungen. Eine schlechtere
  Faktorisierung führt nur zu mehr Ablehnungen (gestörte LU in 6.3, [GV 9.2]). GEPP ist deshalb
  eine Effizienz- und Annahmefrage, keine Voraussetzung der Schranke.
- **Verworfen: Householder-QR als robustere Referenz.** Auf identisch skalierten Systemen liefert
  QR mit derselben Nachiteration in 29 von 2.829 verglichenen Modellen Vorwärtsfehler O(1), wo GEPP
  10⁻¹⁶ erreicht. Das umgekehrte Verhältnis kam nicht vor. β erkannte jeden dieser QR-Fehler.
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
  tatsächlichen Fehler **[state-refine-summary]**; das Fehlermaß ist β. Gemessen: 1–3 Schritte in
  99 %, höchstens 5 **[state-rand-*]**. Ohne Nachiteration steigen die Fehler um Größenordnungen
  **[state-ablation]**. Vergleich mit der BERR-Regel von LAPACK `xGERFS`, der LAWN-165-Logik und 5
  festen Schritten auf 2.811 Modellen: identische Entscheidungen. Die BERR-Regel spart 0,4 Schritte,
  kann aber ohne Schritt enden. Dann überwiegt das Residual in d, und β hat keine Reserve mehr
  (Gate/Fehler bis 1,1 statt ≥ 7,3) **[state-refine-summary]**.
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

Bezeichnungen: n_m mechanische Stationen, n ≈ 3,5·n_m Unbekannte, n_r Halterungen (starr und
elastisch), k_j Lasten im mechanischen Abschnitt j. p = 2 und q = 4 sind die gemessenen Bandbreiten
der Multiplikatoren und von U nach Teilpivotisierung (Invariante 2).

| Schritt | Band | dicht | Bemerkung |
|---|---|---|---|
| Stationsmodell, Gleichungsaufbau | O(n_m + Σk_j) | gleich | Lasten je Abschnitt einsortieren; der Untersuchungscode sucht sie je Abschnitt neu (O(n_m·Σk_j)) |
| Faktorisierung | O(n·p·(p+q)) | 2n³/3 | |
| Primärlösung, ≤ 5 Nachiterationen | O(n) | O(n²) | |
| β aller Zustände: n adjungierte Lösungen | **O(n²)** | **O(n³)** | Engpass |
| Reaktionen: n_r weitere adjungierte Lösungen | O(n·n_r) | O(n²·n_r) | |
| Verifikation (6.3): Residualschranke je adjungierter Lösung, 1 + 3 Durchgänge über M | O(n·p) je Lösung, je Durchgang O(n²) | O(n²) je Lösung, je Durchgang O(n²) | braucht die adjungierten Lösungen und ihre Residualschranken gespeichert (2n² Werte) oder je Durchgang neu gelöst; Größenordnung in [GV 12] |
| Federgesetz, Identitätsprüfung | O(1) je Feder | gleich | nutzt das β des Zustands |
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
  kombiniert die β aller vier Zustände am Abschnittsanfang mit den Gewichten |g| (7.3), und jede
  freie Unbekannte ist ein solcher Zustand. Eigene adjungierte Lösungen je Feldpunkt wären schärfer,
  verlören aber die Monotonie, die das Stückende zur Majorante macht.
- *Gemeinsame Auswertung?* max_i β_i/S_i ist die ∞-Norm von D_S⁻¹·S⁻¹·D_d. Ein Hager/Higham-Schätzer
  käme mit O(n) je Schritt aus, schätzt aber das Maximum nur (Unterschätzung möglich), liefert keine
  Werte je Größe und deckt das Stück-Gate nicht ab. Eine Halbseparabel-Darstellung der Inversen
  ergäbe eine O(n)-Oberschranke |U_i|·|V_j|, kann aber so pessimistisch werden wie die Summe der
  Einzelindikatoren in 4.2. Beides wird nicht übernommen. Eine billige Verbesserung ohne
  Vertragsänderung: In Uᵀw = e_i sind die ersten i Komponenten null, die Vorwärtssubstitution beginnt
  erst bei i.
- *Transponierte Systeme:* Dieselbe Band-Faktorisierung (Multiplikatoren und Band von U) löst
  Sᵀz = c. Das Residual der Nachiteration nutzt das gespeicherte Band von S. Eine zweite Faktorisierung
  ist nicht nötig.
- *Dicht in M1:* sinnvoll, wenn die Faktorisierung hinter einer Schnittstelle liegt (Lösen,
  transponiert Lösen, Residual). Dann ersetzt eine Band-Speicherung sie, ohne Indikator oder Gate zu
  ändern. Bis dahin gilt eine dokumentierte Modellgrößengrenze (O9).
- *Tests vor `main`:* Zähltests (adjungierte Lösungen = n + n_r, Nachiterationsschritte ≤ Obergrenze),
  Bandbreitentest mit wachsender Stationszahl (Invariante 2), ein verfolgter Zeitmesswert bei
  n_m = 10, 30, 100 (Band zusätzlich 1.000) mit Budget statt enger Absolutgrenze, und ein Fall mit
  1.000 Lasten in einem Abschnitt.

## 6. Stabilität, Lösbarkeit und Fehlermodell (A5)

### 6.1 Prüfreihenfolge

1. Eingabeprüfung (Core-Validierung, Gültigkeit der Primitive, Koinzidenzregeln) → `InvalidModel`.
2. Exakte Mechanismusprüfung → `UnstableModel`.
3. Aufbau mit Bereichsprüfung [GV 7]: Eingabewerte ≠ 0 normal, L ≥ 2⁻⁹⁶⁸, Stationsabstände und
   jeder Koeffizient mit allen Zwischenwerten seiner Formel normal, normierte Anregungsprimitive
   ≠ 0 normal, skalierte Streckenlastlängen ≥ 2⁻⁹⁶⁹, Einträge nach Ruiz normal (5.3, 7.2) →
   `NumericalFailure` (Ursache `NumericalRange`).
4. Ohne Anregung (F_exc = 0 je Teilproblem, 7.2): Ergebnis exakt null, *ohne* Lösung. Das gilt
   für jedes exakt stabile Modell, auch ein numerisch singuläres, denn die exakte Lösung ist dann
   unabhängig von der Kondition null.
5. Lösung: Ein Nullpivot bei exakt stabilem Modell bedeutet, das System ist numerisch singulär →
   *numerisch nicht sicher lösbar* (Ursache `ZeroPivot`). Ein Pivot 0 < |p| ≤ 2⁻¹⁰²² ist ein
   Bereichsfall → `NumericalFailure` (`NumericalRange`).
6. Selbsttests (Invariante 11): ein Wert nicht endlich (Ursache `NonFinite`), Gleichungszahl (schon
   beim Aufbau geprüft) oder Federreaktions-Identität verletzt (Ursache `InternalInconsistency`) →
   `NumericalFailure`. Sie stehen vor dem Gate, weil ein verletzter Selbsttest auch β entwertet.
7. Bereichsregel der Ergebnisse: Jede SI-Bezugsgröße S_q (7.2) liegt in [2⁻⁹⁶⁹, 2¹⁰⁰⁰], und keine
   Obergrenze von |Q| je Stück läuft in SI über, sonst `NumericalFailure` (Ursache
   `NumericalRange`) [GV 7]. Gegenbeispiel ohne diese Regel in 6.4, Befund 5.
8. Verifikation der adjungierten Lösungen: μ ≥ 1 für alle Gewichtskandidaten (6.3) → *numerisch
   nicht sicher lösbar*, Ursache `NotVerified`. β ist dann nicht nachweislich eine Schranke; das
   Ergebnis ist damit nicht als falsch erkannt, seine Genauigkeit nur nicht nachgewiesen.
9. Gate > τ (7.3) → *numerisch nicht sicher lösbar*, Ursache `Gate`.

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
Federn ist exakt stabil, kann aber numerisch oder physikalisch nahezu ein Mechanismus sein. Das
entscheiden dann die Schritte 4, 5, 8 und 9 aus 6.1.

**Äquivalenz:** Bei EI > 0 und k > 0 ist das Gleichungssystem genau dann singulär, wenn die
Teilkörper-Kinematik einen Mechanismus hat. Begründung: Bei Last null leistet eine homogene Lösung
keine Arbeit, ihre Formänderungsenergie ist also null. Dann ist w'' = 0 je Teilkörper und w = 0 an
jeder Feder, und sie ist eine zulässige Starrkörperbewegung. Gemessen auf 12.500 Zufallsmodellen:
Mechanismusurteil und exakte Singularität stimmen in allen Fällen überein, und der Bareiss-Rang ist
gleich dem Bruchrang **[state-rand-*]**. Axial: ohne axiale Freigaben ist das ein Mechanismus genau
dann, wenn keine axiale Halterung existiert.

### 6.3 Numerische Qualität: τ, β, s

Drei Größen werden strikt getrennt:

| Symbol | Bedeutung |
|---|---|
| τ | geforderte Genauigkeit des **tatsächlichen** Ergebnisses im Maß aus 7.2 (Produktwert, 7.3) |
| β | zur Laufzeit berechnete **A-posteriori-Fehlerschranke** einer Ergebnisgröße. Eine obere Schranke des Fehlers gegenüber der exakten Lösung der übergebenen doubles ist sie nur, wenn (1) das Rundungsmodell des Gleitkomma-Vertrags für Aufbau, Lastintegrale, Lösung und Auswertung gilt (Plattformvoraussetzungen V1–V6, [GV 2]) und (2) die adjungierten Lösungen verifiziert sind (μ < 1, unten) |
| s | Akzeptanzfaktor: geliefert wird, wenn s · Gate ≤ τ (Gate: größtes normiertes β über den ganzen Balken, 7.3). Entscheidung: **s = 1** (7.6) |

Sei A*x* = b* das System der exakten Daten und Â, b̂ das gespeicherte. Für die berechnete Lösung
x̂ gilt exakt

```text
x̂ − x* = A*⁻¹ r*,   r* = b* − A* x̂,
|r*| ≤ d := |r̂| + δr + Σ_j γ̄(k_ij + 1)|Â_ij||x̂_j| + δb_i
```

- r̂ ist das berechnete Residual und δr = γ̄(m+1)(|b̂_i| + Σ|Â_ij||x̂_j|) + 2mλ seine Rundungsschranke
  (m Nichtnullen der Zeile, Punkt 14 in [GV 5]).
- k_ij ist die Zahl der Rundungen bei der Bildung des Eintrags A_ij nach der ausgeführten Formel.
  δb_i ist die Schranke des Datenfehlers der rechten Seite: Lastintegrale, abgeschnittene
  Intensitäten und Stationslasten, mit eigenem λ-Anteil (Punkte 6–9 in [GV 5]).
- γ(k) = k·u/(1 − k·u), γ̄(k) = (k + 1)u ≥ γ(k) exakt darstellbar.
- λ = 2⁻¹⁰²² ist der absolute Fehler einer Operation, deren Ergebnis subnormal wird oder durch
  FTZ zu 0 wird. Er ersetzt η = 2⁻¹⁰⁷⁴ aus Runde 3/4, das nur bei graduellem Unterlauf galt [GV 8].
  Ohne diesen Term ist die Schranke bei Unterlauf null, obwohl das Ergebnis falsch ist (Befund 3
  in 6.4). Jede Komponente hat d_i ≥ 2mλ > 0.

Die Rundungszahlen folgen aus der verbindlichen Operationsfolge jeder Formel [GV 5]. Das Programm
`state/fpc_counts.py` leitet sie mechanisch mit den Regeln aus [GV 3] ab. Danach wird jede Formel
mit ihrer Schranke gegen exakte rationale Werte geprüft, auf strukturierten Grenzfällen und mit
emuliertem FTZ/DAZ (`state/fpc_stage_a.py`). Werden Schranken verletzt, wird die Formel korrigiert,
nicht die Zahl. So ist in Runde 5 der fehlende Datenterm der abgeschnittenen Linearlast gefunden
worden (Befund 6 in 6.4).

**Komponentenweiser Indikator** für eine Ergebnisgröße y = cᵀx: Löse Aᵀz = c mit der vorhandenen
Faktorisierung und Nachiteration (unten), dann β_y = |z|ᵀd. A⁻¹ wird nicht gebildet. Benötigt werden
n adjungierte Lösungen für die Zustände und n_r für die Reaktionen; die Kosten stehen in 5.6.
Feldwerte innerhalb eines Stücks und Extremwerte werden über |g|ᵀβ_Zustand plus Auswertungsrundung
abgeschätzt (7.3). Reaktionen entstehen als Differenz korrelierter Zustände und erhalten deshalb eine
eigene adjungierte Lösung. Die Summe der Einzelindikatoren kann dort um viele Größenordnungen
überschätzen (Abschnitt 4.2).

Das ist die komponentenweise Form der Fehlerschranke von LAPACK `xGERFS` (Arioli, Demmel, Duff
1989): Dort steht \|A⁻¹\|(\|r\| + NZ·ε(\|A\|\|x\| + \|b\|) + SAFE1), normweise über einen Schätzer
ausgewertet. SpanDraft bildet die Zeilen von \|A⁻¹\| exakt über adjungierte Lösungen und ersetzt
NZ·ε durch die hergeleiteten Rundungszahlen der Einträge [Audit 2].

**Adjungierte Nachiteration: mindestens ein Schritt; weitere nur, solange sich β der Größe um mehr
als 1/16 ändert; höchstens 5.** Begründung und Messung **[state-gate-summary]**, 10.346 exakt stabile
Modelle:

| Regel | geliefert / abgelehnt | Schritte je adjungierter Lösung | Gate gegenüber genau einem Schritt |
|---|---|---|---|
| keine Nachiteration | 10.335 / 11 | 0 | bis 86 % kleiner (nur abgelehnte Modelle); 4 Unterschätzungen in gelieferten **[state-acceptance-summary]** |
| genau ein Schritt | 10.335 / 11 | 1 | – |
| wie Primärlösung (‖Δz‖ ≤ 2u‖z‖ oder Stagnation) | 10.335 / 11 | 1,293 | bei 3 abgelehnten Modellen um mehr als 1 % anders, sonst gleich |
| β-Änderung ≤ 1/16 | 10.335 / 11 | 1,003 | bei 3 abgelehnten Modellen um mehr als 1 % anders, sonst gleich |

- Ein Schritt ist nötig. Ohne ihn liegt β an 4 Werten gelieferter Modelle unter dem tatsächlichen
  Fehler (bis zum 1,66-Fachen, bei Werten ≤ 2,4·10⁻³⁵ der Skala). Das entspricht der bekannten
  Eigenschaft, dass ein Nachiterationsschritt in Arbeitsgenauigkeit die LU-Lösung komponentenweise
  rückwärtsstabil macht (Skeel).
- Weitere Schritte ändern in keinem gelieferten Modell das Gate um mehr als 1 %. In den
  schwierigsten abgelehnten Modellen erhöhen sie β, etwa bei f|1e-11|165 von 3,6·10⁻³ auf 4,1·10⁻³;
  der Fehler dort ist 4,2·10⁻³. Eine starre Regel „genau ein Schritt“ ist dort zu optimistisch.
- Das Abbruchkriterium der Primärlösung kostet 29 % zusätzliche Schritte, weil es volle
  Konvergenz von z nachweist, die β nicht braucht. Das β-Kriterium misst, was verwendet wird, und
  kostet im Mittel 0,3 %. 1/16 ist keine angepasste Konstante: β hat typisch zwei Größenordnungen
  Reserve, eine Änderung unter 6 % ist dafür bedeutungslos.
- Bei künstlich um 10 % gestörten LU-Faktoren reduziert die Primärregel (bis 5 Schritte) die Fälle
  „Fehler knapp über Gate“ von 9 auf 1, die β-Regel auf 7 **[state-perturb-check]**. Dort liegt β
  ohnehin innerhalb von 1 % am Fehler (siehe ω unten); kein gestörter Fall lieferte einen Fehler über τ.
- **Vergleich mit etablierten Regeln** (Runde 4, 2.811 Modelle, [Audit 3.2, 3.3]): die BERR-Regel von
  LAPACK `xGERFS`, die Kontraktions- und Stagnationslogik von LAWN 165 und 5 feste Schritte treffen
  dieselben Entscheidungen. Mit der Verifikation unten bestimmt die Regel nur noch Kosten und
  Verifizierbarkeit. Die β-Regel kostet 1,002 Schritte je adjungierter Lösung, die BERR-Regel 0,973,
  LAWN 165 1,114. Bei um 10 % gestörten LU-Faktoren bleiben mit der β-Regel 23, mit der BERR-Regel 69
  und ohne Nachiteration 241 von 624 Modellen unverifiziert **[state-refine-summary]**. Die β-Regel
  bleibt deshalb als bewusste Abweichung mit gemessenem Vorteil. Die Konstante 1/16 selbst ist
  nicht variiert worden.

**Entscheidung: komponentenweise, nicht normweise.** Ein normweiser Vertrag (Higham/Hager-Schätzer
für ‖A⁻¹‖) würde kleine, fachlich wichtige Größen nicht schützen. Beispiel: die Querkraft außerhalb
eines Lagerpaars, wenn zwischen den Lagern 10¹⁴-mal größere Werte stehen. Die Messungen zeigen,
dass die komponentenweise Zusage erreichbar ist (Abschnitt 7.6).

**Verifikation der adjungierten Lösungen (O15, O16; vollständig in [GV 6]).** |z|ᵀd ist nur dann
eine Schranke, wenn z genau genug ist. Zwei Ungenauigkeiten bleiben: die berechnete ẑᵢ statt der exakten adjungierten
Lösung und das gespeicherte Â statt A*. Beides lässt sich exakt erfassen, nicht nur in erster
Ordnung. Mit dem exakten Residual sᵢ = eᵢ − Âᵀẑᵢ und |Â − A*| ≤ Γ|Â| gilt für den Fehler e

```text
|e_i| ≤ β_i + (M|e|)_i,     M = |S_res| + |Ẑ|ᵀ Γ |Â| ≥ 0      (Zeile i von |S_res|: Schranke von |s_i|)
μ = max_i (M v)_i / v_i < 1   ⇒   |e_i| ≤ β_i + (M v)_i / (1 − μ)
```

Das ist Satz 10.2 aus Rump (2010, Acta Numerica) in der mit v gewichteten ∞-Norm, mit den
berechneten adjungierten Lösungen als genäherter Inverse R. Exakt gilt e = R r* + C e mit
C = I − R S*, und M ≥ |C| eintragsweise [GV 6]. Gewichte: zuerst v = β + 2⁻²⁰⁰·D (D: Nenner des
Gates), dann drei Neumann-Schritte v ← β + Mv + 2⁻²⁰⁰·D. Jedes v > 0 ist zulässig; die Schritte
entscheiden nur, ob μ < 1 *nachweisbar* ist. In Runde 4 fehlten sie, deshalb waren zwei Modelle
nicht verifizierbar (unten). |sᵢ| wird in double gebildet, mit der rigorosen Schranke
γ̄(m+1)(|c| + |S|ᵀ|z|) + 2mλ. Dot2 (Ogita, Rump, Oishi 2005) bringt für μ nur einen festen Faktor
und ist keine Voraussetzung mehr [GV 6]. Es entsteht keine Rekursion: Die n + n_r adjungierten
Lösungen sind ohnehin berechnet; M braucht ihre Residuen und je Gewichtskandidat einen Durchgang über
sie. Für Reaktionen gilt dasselbe mit der Zeile ihrer adjungierten Lösung. Ist μ ≥ 1 für alle
Kandidaten, ist β nicht nachgewiesen, und das Modell wird abgelehnt (6.1 Schritt 8).

**Rigorose Auswertung.** Eine gewöhnlich gerundete Schätzung μ < 1 genügt nicht. d, β, Mv, μ, E
und das Gate werden mit nach oben gerundeten Operationen gebildet, 1 − μ nach unten. .NET hat keine
Rundungsmodus-Steuerung. Der Kern emuliert die gerichtete Rundung je Operation mit
`Math.BitIncrement`/`Math.BitDecrement`, bei Ergebnissen unter 2⁻¹⁰²² mit dem Wert 2⁻¹⁰²² (L5 in
[GV 3]). Intervallarithmetik ist nicht nötig. Diese Emulation ist Voraussetzung der Architektur.

Die Trust-Größe von `xGERFSX` wird nicht übernommen. Sie schätzt die Kondition der Lösung x in einer
Norm und setzt Residuen in erhöhter Genauigkeit voraus (LAWN 165, Abschnitt 7). SpanDraft braucht
dagegen die Genauigkeit der adjungierten Lösung je Funktional bei double-Residual, auch für exakt
verschwindende Komponenten.

Gemessen **[state-refine-summary]** auf 2.811 Modellen:

- ungestört μ im Median 4·10⁻¹⁵; die Korrektur ändert das Gate höchstens um 3,6 %;
- 2 Modelle (Lager bzw. starre Feder im Abstand 3·10⁻¹¹ m) waren mit den Gewichten von Runde 4
  nicht verifizierbar, obwohl ihr Fehler 1,9·10⁻¹⁶ bzw. 0 ist. Runde 5 hat die Ursache geklärt
  [GV 10]. Eine Komponente mit β auf Unterlaufniveau ist über M_ij ≈ 2·10⁻¹⁵ an eine um 10²² größer
  gewichtete gekoppelt. M selbst hat aber den Spektralradius 4,5·10⁻¹⁵ bzw. 1,0·10⁻¹⁴, die
  Information reicht also. Das Scheitern war eine vermeidbare Schwäche der Gewichtswahl. Mit den
  Neumann-Schritten sind beide rigoros verifiziert, mit Gate 6,2·10⁻¹⁵ bzw. 6,7·10⁻¹⁶, und alle
  Kettenstufen sind ohne Verletzung **[state-fpc-unverified, state-fpc-chain-unverified]**;
- bei 10⁻⁴ bzw. 10⁻¹ gestörten LU-Faktoren lag der tatsächliche Fehler in *jedem* verifizierten Modell unter β (Messung). Bei
  10⁻¹ lag das unkorrigierte β in 311 von 624 Modellen an mindestens einem Wert unter dem Fehler
  (14 davon geliefert). In 298 davon deckt die korrigierte Schranke den Fehler, die übrigen 13 sind
  nicht verifiziert und werden abgelehnt.

**Status: Schranke unter benannten Voraussetzungen.** Unter dem Rundungsmodell des
Gleitkomma-Vertrags und bei erfolgreicher Verifikation ist β eine obere Fehlerschranke gegenüber der
exakten Lösung der übergebenen doubles. Das gilt für jeden Zustand, über die Feldauswertung für
jeden Feldwert und für jede Reaktion. Die Beweiskette [GV 4] hat drei Glieder: (1) Rundungsmodell
⇒ |r_exakt| ≤ d komponentenweise, (2) Verifikation ⇒ exakte Lösung in ŷ ± E, (3) daraus die
Schranke für das Funktional. Voraussetzungen V1–V6 [GV 2]: IEEE binary64 mit Rundung zur nächsten
Zahl, keine FMA-Kontraktion, graduelles *oder* abruptes Unterlaufen, kein Überlauf auf
angenommenen Pfaden (Bereichsregeln), korrekte `ScaleB`/`BitIncrement`, exakt stabiles Modell.
Geprüft ist die Kette in Runde 5 auf jeder Stufe gegen exakte Arithmetik, auf 452 Referenzläufen und
2.295 Zufallsmodellen ohne eine Verletzung [GV 9]. Der Beweis ist ein Papierbeweis; ein maschinell
geprüfter Beweis ist nicht Teil dieses Entwurfs. Offen ist die Konformität der C#-Operationsfolgen
(O2). Nicht abgedeckt ist der Positionsanteil der Extremwerte (7.3). Gemessene Schärfe je Wert:
β/Fehler im Median 10², im 1. Perzentil 10¹, im 99. Perzentil 10⁴, für alle Größen und Klassen
ähnlich **[state-acceptance-summary]**.

**Rolle der klassischen Maße:** Die Kondition wird nicht als Grenze verwendet; sie steckt in |z|.
Der Rückwärtsfehler ω ist **keine Entscheidungsgröße** und hat keine versteckte Schwelle:

- *Kein Zustand, den nur ω erkennt.* Für jede berechnete Lösung, wie auch immer sie entstanden ist,
  gilt x̂ − x* = A*⁻¹r*. β verwendet das *gemessene* Residual. Es setzt also keine
  Rückwärtsstabilität der Faktorisierung voraus, sondern nur eine verifizierte adjungierte Lösung und
  die Rundungszahlen; beides prüft ω nicht. Gegenprobe mit gestörter Faktorisierung: Die LU-Faktoren
  werden relativ um 10⁻⁸, 10⁻⁴ und 10⁻¹ verrauscht (jedes 5. Modell, 2.064 Modelle), die Nachiteration
  läuft gegen das wahre System. Kein Fall lieferte einen Fehler über τ. Bei 10⁻¹ wurden 1.992
  abgelehnt und 72 geliefert, diese mit Fehler ≤ τ **[state-gate-summary]**. Nichtendliche Werte
  fängt der Endlichkeitstest, singuläre die Nullpivotprüfung, Unterlauf die Bereichsregeln und der
  λ-Term.
- *Eine ω-Grenze wäre falsch.* Der komponentenweise Wert erreicht in *gelieferten, korrekten*
  Modellen 1 (f|1e-11|11: ω = 1, Gate 4,9·10⁻¹⁵, Fehler 1,2·10⁻¹⁶). Ursache sind Zeilen ohne Last, in
  denen alle Terme bei 10⁻³⁵ der Skala liegen und das Residual so groß ist wie die Zeile selbst. Ihr
  Beitrag zu β ist vernachlässigbar.
- ω bleibt eine Entwicklungsdiagnose (Schritt 11), nicht Teil des Genauigkeitsberichts.

**Schärfe bei dominierendem Residual:** In der Gegenprobe mit 10 % Störung liegt β ohne
Verifikation bis auf 1 % am tatsächlichen Fehler, in 14 gelieferten von 624 Modellen knapp darunter. Ursache:
Das gemessene Residual überwiegt dann die Rundungsterme, und |z|ᵀ|r̂| ist bis auf
Vorzeichenauslöschung gleich |zᵀr̂|; die übliche Reserve von zwei Größenordnungen entfällt. Dass β
dabei *unter* den Fehler fällt, liegt an der ungenauen adjungierten Lösung und wird von der
Verifikation erkannt. Mit Verifikation gilt s = 1 auch für einen Löser ohne Rückwärtsstabilität, er
wird nur öfter ablehnen (O15 geschlossen). Dieselbe Schärfe entsteht, wenn die Primärlösung ohne
Nachiterationsschritt endet (BERR-Regel, Gate/Fehler 1,1); deshalb bleibt der Pflichtschritt (5.2).

**Unterlauf (Runde 5, [GV 7, 8]):** Vier Klassen werden getrennt. (1) Unzulässige Bereiche lehnen
die Bereichsregeln *vor* der Operation ab. Danach sind Koeffizienten, Pivots, Divisoren,
Bezugsgrößen und gespeicherte Operanden normal oder null. (2) Normale Operationen folgen dem
γ-Modell. (3) Absolute Reste nahe dem Unterlauf deckt λ = 2⁻¹⁰²² je Operation in normierten
Einheiten; der Term gilt für graduelles wie abruptes Unterlaufen. Das betrifft rechte Seite,
Residual, Lastintegrale, Auswertung, subnormale Auswertungsstellen x und die Rückskalierung. λ hat
die Rolle von SAFE1 = NZ·safmin in LAPACK `xGERFS`. (4) Relative Safe-Minimum-Probleme (SAFE2)
entstehen nicht, weil keine relative Größe einen beliebig kleinen Nenner hat. Gespeicherte Lösungs-
und adjungierte Vektoren enthalten keine Subnormalen (Software-Flush auf 0; die a-posteriori-
Beziehungen gelten für jeden gespeicherten Vektor). Damit braucht kein angenommener Pfad
subnormale Arithmetik, und die Garantie hängt nicht davon ab, ob FTZ/DAZ (x64) oder FZ (ARM64)
aktiv ist. Das prüft die Untersuchung mit emuliertem FTZ/DAZ auf allen Stufen [GV 9]. Ein
Starttest auf graduelles Unterlaufen bleibt als Plattformdiagnose ohne Wirkung auf den Status. η =
2⁻¹⁰⁷⁴ aus Runde 3/4 entfällt. Die Rückrechnung in SI ist Teil des Vertrags (Bereichsregel und
λ-Anteil in β_SI, 7.2).

### 6.4 Verletzungsanalyse

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
   Rückskalierung allgemein Teil des Vertrags: S_q,SI ≥ 2⁻⁹⁶⁹ und λ-Anteil in β_SI, auch mit FTZ (mit
   FTZ hätte der Kleinstbalken 2,1·10⁻² Fehler) **[state-fpc-chain-underflow]**.
6. **Datenfehler abgeschnittener Linearlasten** (Runde 5, [GV 9.1]). Endet ein Abschnitt nahe dem
   Nulldurchgang einer Linearlast, hat die abgeschnittene Intensität q_a = v₁ + (v₂ − v₁)f einen
   absoluten Fehler u·|v|, also einen großen relativen. Das Rundungsmodell von Runde 1–4
   (`K_DIST = 40` auf |t₁| + |t₂|) hatte dafür keinen Term und unterschätzte den Fehler dieser
   Lastintegrale bis zum 9,4·10¹³-Fachen; beim Abstand 0 stimmte nicht einmal das Vorzeichen. Auf
   Ausgabewerten war es in den untersuchten Modellen nicht sichtbar (Fehler/Schranke ≤ 0,056),
   weil der Term absolut winzig ist. Abhilfe: eigener Datenterm der abgeschnittenen Intensität im
   Vertrag (Punkt 8 in [GV 5]); danach 0 Verletzungen.

Verbleibend: In gelieferten Modellen keine Unterschreitung (mit adjungierter Nachiteration, 6.3).
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
| `IllConditionedSystem` (Bedeutung: *numerisch nicht sicher lösbar*) | `ZeroPivot`: exakt stabil, F_exc > 0 und Nullpivot; `NotVerified`: adjungierte Lösungen nicht verifiziert (μ ≥ 1 für alle Gewichtskandidaten, 6.3), die Genauigkeit ist nicht nachgewiesen, das Ergebnis aber nicht als falsch erkannt; `Gate`: Gate > τ (Größe, Ort, erreichter Wert) |
| `NumericalFailure` | `NumericalRange`: eine Bereichsregel aus [GV 7] verletzt (Eingabe ≠ 0 nicht normal, L < 2⁻⁹⁶⁸, Koeffizient oder Zwischenwert nicht normal, normiertes Anregungsprimitiv ≠ 0 nicht normal, Streckenlast zu kurz, Eintrag nach Ruiz oder Pivot nicht normal, SI-Bezugsgröße außerhalb [2⁻⁹⁶⁹, 2¹⁰⁰⁰]); `NonFinite`: Lösung, β oder Gate nicht endlich; `InternalInconsistency`: Gleichungszahl ≠ Unbekannte, Federreaktions-Identität verletzt |

Die Zuordnung ist vollständig: Jede Ablehnung hat genau einen dieser Codes, und kein anderes
Kriterium lehnt ab (6.1). Kondition, ω und Eingabesensitivität führen zu keinem Code. Getrennt sind
vier Bedeutungen: `NumericalRange` (die Garantie ist im Zahlbereich nicht herleitbar, geprüft vor
jeder Ausgabe), `InternalInconsistency` (Implementierungsfehler), `NotVerified` (Genauigkeit nicht
nachweisbar) und `ZeroPivot`/`Gate` (numerisch nicht sicher lösbar). Die strukturierte Ursache ist
neu; die öffentlichen Codes bleiben bis zur Migration unverändert.

Die Umbenennung von `IllConditionedSystem` ist eine Option der Ergebnisschnittstelle. Der Code
bleibt bis zur Migration erhalten.

## 7. Numerischer Vertrag (Vorschlag für A8)

### 7.1 Vertragsgrenze und Referenzlösung

Referenz ist die **exakte mathematische Lösung des mechanischen Modells zu den doubles, die dem
Rechenkern übergeben werden** (Schnitt 1 in Abschnitt 3.2). Nicht zum Solverfehler gehören:
UI-Parsing, Einheitenumrechnung, Querschnittswerte (A, I), Werkstoff (E), das Produkt E·I und die
Zerlegung schräger Kräfte über sin/cos (DEV-12). Diese Schritte haben eigene Verträge in Core bzw.
im Adapter. Die Untersuchung in `state/` zählt die Rundung von E·I konservativ trotzdem zum
Solver. Prüfbar ist die Referenz mit den exakten Bruchrechnungen (`transfer_ref.py` und die
Macaulay-Referenz `spring_ref.py`, untereinander bitgleich, `xcheck.py`).

### 7.2 Anregung und Bezugsgrößen

**Anregungsskala.** Anregung wird auf Ebene der mechanischen Solvereingabe definiert, nicht über
Resultierende. Jedes Anregungsprimitiv trägt den Betrag seiner rechten Seite in Krafteinheiten bei,
je Teilproblem (Biegung, Axial) getrennt:

```text
F_exc = Σ|P| + Σ∫|q| + Σ|C|/L                         Biegung (heute)
F_exc,x = Σ|P_x| + Σ∫|n|                              Axial (heute)
später zusätzlich, je Primitiv in Krafteinheiten:
  vorgeschriebene Verschiebung δ:  |δ|·EI_ref/L³      vorgeschriebene Verdrehung φ:  |φ|·EI_ref/L²
  eingeprägte Krümmung κ (DEV-27): ∫|κ|·EI dx / L²    (entsprechend axial)
```

Weil nur Beträge eingehen, ist F_exc = 0 genau dann, wenn jedes Anregungsprimitiv null ist. Lasten,
die sich global aufheben, ergeben F_exc > 0 (gemessen: Resultierende und resultierendes Moment null,
max|M| = 10⁴ Nm, geliefert mit Fehler 1,5·10⁻¹⁷). Ebenso ergeben P und −P an derselben Stelle
F_exc > 0, b ≡ 0 und eine exakt verschwindende Lösung, gemessen gegen eine Skala > 0
**[state-zero]**. Spätere Anregungsarten wie Lagersenkung oder Temperatur tragen bei, sobald sie
ungleich null sind, und werden nie als „unbelastet“ behandelt.

**Ohne Anregung** (F_exc = 0 für ein Teilproblem): Dann ist b ≡ 0, und für ein exakt stabiles Modell
ist die exakte Lösung null. Der Kern liefert exakt null. Gemessen ist das für Einfeldträger,
Durchlaufträger, Federlagerung, EI-Wechsel, Gelenke, Lagerpaar im Abstand L/10¹¹ und den
Axialanteil, jeweils mit einem Nachiterationsschritt **[state-zero]**. Ein exakt stabiles, aber
numerisch singuläres Modell (Balken nur auf zwei Federn im Abstand 5·10⁻¹² m) hat ohne Anregung
einen Nullpivot, obwohl seine exakte Lösung null ist. Deshalb wird der Nullfall vor der Lösung
entschieden (6.1, Schritt 4). Es wird *kein* relatives Maß
gebildet (Skala 0). Zusage: Alle Werte sind exakt 0. Sie werden konstruiert, nicht berechnet; ein
Test prüft die exakten Nullen. Ein Indikator wird nicht gebildet; die Untersuchung zeigt nur, dass
auch eine Lösung mit dem Kern exakte Nullen ergäbe und der damalige η-Anteil ≤ 10⁻²⁹⁴ bliebe. Der
Genauigkeitsbericht weist „ohne Anregung“ aus.

**Bereichsregel (Runde 5, [GV 7]):** Jedes Anregungsprimitiv ≠ 0 muss nach Entdimensionierung und
Normierung (5.1) eine normale Gleitkommazahl ergeben, sonst `NumericalFailure` (Ursache
`NumericalRange`). Für die SI-Bezugsgrößen S_q unten gilt S_q,SI ∈ [2⁻⁹⁶⁹, 2¹⁰⁰⁰], und die
Obergrenzen von |Q| je Stück dürfen in SI nicht überlaufen. Dann ist der absolute Fehler der
Rückskalierung, auch mit FTZ (< 2⁻¹⁰²²), relativ zu S_q höchstens 2⁻⁵³, und er steht als λ-Anteil
in β_SI. Das ist eine allgemeine Regel der Ausgabe, kein Sonderfall kleiner Balken: Geliefert wird
nur, wenn alle für die Zusage nötigen dimensionalen Ergebnisse und Bezugsgrößen ohne
unkontrollierten Über- oder Unterlauf darstellbar sind. Einzelne SI-Werte unter S_q dürfen
subnormal sein; ihre Rundung steht in β_SI. Die Grenze 2⁻⁹⁶⁹ statt „normal“ ist der Preis der
FTZ/DAZ-Unabhängigkeit: Sie lehnt Modelle mit SI-Skalen unter 2·10⁻²⁹² ab, etwa w ≈ 10⁻³⁰⁰ m, die
bei graduellem Unterlauf richtig wären **[state-fpc-chain-underflow]**. Unterläufe in
Zwischenergebnissen erfasst der λ-Term von β (6.3). Gegenbeispiele ohne diese Regeln: Befunde 3
und 5 in 6.4.

**Bezugsgrößen** aus der berechneten Lösung (im Produkt gibt es keine exakte):

```text
S_M = max(max|M|, F_exc·L)                S_V = max(S_M/L, F_exc)
S_θ = max(max|θ|, S_M·L/EI_min)           S_w = max(max|w|, S_M·L²/EI_min, S_θ·L)
S_N = max(max|N|, F_exc,x)                S_u = max(max|u|, S_N·L/EA_min)
```

Präzisierung (Runde 5, [GV 5] Punkt 21a): Die Maxima laufen über die Ereignisstationen (beide
Seiten), die Größen sind die der **exakten** Lösung und der exakten Daten. Der verteilte Anteil von
F_exc ist Σ(|q_a| + |q_b|)/2·Länge, also ∫|q| für Lasten ohne Vorzeichenwechsel. Das Produkt kennt
die exakte Lösung nicht und rechnet mit rigorosen Untergrenzen: max(|Q̂| − β) nach unten, F_exc nach
unten, EI_min nach oben gerundet. Damit ist der normierte Wert im Gate eine obere Schranke des
normierten Fehlers im Vertragsmaß. Kleinere Skalen machen das Gate nur strenger. Das löst auch die Reihenfolge im Datenfluss:
Qualitätsbewertung (Schritt 7) vor den Extremwerten (Schritt 9).

| Größe | Fehlermaß |
|---|---|
| w, θ, u | \|Fehler\| / S_* |
| M, N, Reaktionsmoment | \|Fehler\| / max(\|Wert\|, S_M bzw. S_N) |
| V, Reaktionskraft | \|Fehler\| / max(\|Wert\|, S_V) |

Das gemischte Maß für V und Reaktionen ist bewusst strenger als das globale Feldskalenmaß der
bisherigen Untersuchungen. Physikalisch große Querkräfte zwischen nahen Lagern (31 GN im 6-nm-Fall)
dürfen den Maßstab für die übrige Querkraft nicht vergrößern. Gemessen ist es genauso erreichbar wie
das globale Maß (Abschnitt 7.6).

### 7.3 Qualitätsgate über den ganzen Balken und Zusagen

Eine `BeamSolution` wird einmal angenommen und erlaubt danach `EvaluateAt(x)` an *beliebigen*
Stellen. Das Gate muss deshalb den kontinuierlichen Balken abdecken, nicht nur die Stationen.

**Herleitung.** Im mechanischen Abschnitt j ist an der lokalen Stelle s

```text
β_q(s) = (1+γ)·Σ_k |g_qk(s)|·β_k  +  Σ_k γ(·)|g_qk(s)·X_k|  +  γ(m)·P_abs,q(s)  +  δP_q(s)  +  c·λ
g ∈ {1, s, s²/(2EI), s³/(6EI)} (je Komponente)
```

Dabei sind X_k der Zustand am Abschnittsanfang, β_k sein Indikator, P_abs,q(s) die Summe der Beträge
der Lastintegralterme und m deren Anzahl. Geprüft, welcher Term in s nichtfallend ist:

| Term | nichtfallend, weil | gilt für |
|---|---|---|
| \|g_qk(s)\|·β_k, γ\|g_qk(s)·X_k\| | g ist ein Polynom in s ≥ 0 mit nichtnegativen Koeffizienten | alle Abschnitte |
| P_abs,q(s) | jeder Lastterm ist \|Intensität\| mal ein Polynom in Abständen (x − Lastort, Lastlänge) mit nichtnegativen Koeffizienten | Punktkraft, Punktmoment, linear veränderliche Streckenlast; ebenso eingeprägte Krümmung (DEV-27: κ·d, κ·d²/2) |
| m, c·λ | konstant im Stück, denn eine Last wechselt den Zustand nur an Ereignisstationen | alle |

Die Monotonie hängt nicht an der Auslöschungsfreiheit von `Difference`/`Ramp`. Auch die naive
Macaulay-Form hätte eine monotone Betragssumme, nur eine viel größere. Sie hängt daran, dass der
Indikator als Summe von Beträgen geschrieben ist, deren jeder in s wächst. In Gleitkomma ist jede
dieser Operationen monoton (Rundung ist monoton für +, ×, ÷ mit festem Vorzeichen). Potenzen müssen
dafür als Produkte gebildet werden, nicht über `Math.Pow`; die Rundungszahlen sind ohnehin so
gezählt. Ausnahme ist das geschlossene linke Stückende, wenn dort eine Streckenlast endet. Dort
rechnet `Difference`/`Ramp` noch im Zweig x ≤ b, das Innere im Zweig x > b. Gemessen ohne Toleranz:
28 Abfälle in 28 von 10.346 Modellen, alle an dieser Stelle, höchstens 3,6·10⁻¹⁶ relativ (≈ 2 ulp)
**[state-gate-summary, state-mono-check]**. Der linke Endwert ist selbst ein Stationswert und wird vom
Stationsgate erfasst. Im offenen Stück gibt es keinen Zweigwechsel, die Majorante bei x₁⁻ gilt dort
ohne Zuschlag. Der Faktor (1 + γ(4)) ist deshalb nur eine Reserve für die Auswertung der Majorante,
nicht die Begründung. Das alles gilt für β, *nicht* für den tatsächlichen Fehler.

**Allgemeine Form, nicht Monotonie als Invariante.** Das Gate braucht je Stück und Größe eine
berechenbare **Majorante** B_q ≥ sup_{x∈Stück} β_q(x). Mit den heutigen Lastprimitiven ist
B_q = (1 + γ(4))·β_q(x₁⁻). Ein künftiger Lasttyp, dessen Terme nicht monoton sind (etwa eine
periodische Last), muss seine eigene Majorante liefern, zum Beispiel jeden Term an seinem Maximum
über das Stück. Architekturinvariante ist die Majorante, nicht die Monotonie.

**Stück-Gate:** je Stück [x₀, x₁] der Länge h und Größe q

```text
G_q = B_q / D_q,    D_q = S_q (w, θ)    bzw.    D_q = max(S_q, LB_q)   (M, V)
LB_V = (max(|V(x₀⁺)|, |V(x₁⁻)|) − h·q_sum − β_V(x₁⁻))·(1 − γ(8))
LB_M = (max_e (|M_e| − |V_e|·h − q_sum·h²/2) − β_M(x₁⁻))·(1 − γ(8)),   e ∈ {x₀⁺, x₁⁻}
q_sum = Σ über Streckenlasten des Stücks: max(|q(x₀)|, |q(x₁)|)
```

LB_q ist eine **geschlossene, beweisbar konservative Untergrenze** von min |Q̂| im Stück, also des
berechneten Feldes: Es gilt |V(x) − V_e| ≤ ∫|q| ≤ h·q_sum und |M(x) − M_e| ≤ |V_e|·h + q_sum·h²/2. Der
Abzug von β_q(x₁⁻) deckt die Auswertungsrundung ab. Er deckt auch den Unterschied zum exakten Q, aber
nur, soweit β den Fehler deckt. Gemessen an allen dichten Punkten von 68.442 Stücken mit LB_q > 0
**[state-lb-*]**: |Q̂(x)| < LB_q nie. |Q_exakt(x)| < LB_q an 5 Punkten eines einzigen Modells
(f|1e-06|872), das abgelehnt wird (Gate 10⁻³). Dort ist der Fehler 5,8·10⁻³ größer als β = 5,2·10⁻³,
die erste Ordnung gilt nicht mehr. In gelieferten Modellen gab es keine Ausnahme. Es gibt weder
Nullstellensuche noch Stichproben.

**Unabhängige Gegenprüfung** (Runde 4, [Audit 3.5]): Das Stückpolynom p̂ der berechneten Daten und das
exakte Stückpolynom p werden in Bruchrechnung gebildet. Ihre Bernstein-Koeffizienten werden exakt
gebildet und exakt unterteilt (Cargo/Shisha-Einschließung). Daraus folgt eine zertifizierte
Einschließung von min|p̂| und min|p| je Stück. Auf 18.828 Stücken mit LB_q > 0 in 1.833 Modellen
**[state-piece-*]**: LB_q > min|p̂| nie, LB_q > min|p| nie, kleinster Abstand (min|p̂| − LB_q) = 0,88·β.
Ein Gate mit der Bernstein-Untergrenze statt LB_q ist identisch (Verhältnis 1,000) und ändert keine
Entscheidung. Variante C bleibt; Bernstein ist Testoracle, kein Produktverfahren, denn als
Gleitkommaverfahren bräuchte es gerichtete Rundung. Drei Begriffe sind dabei zu trennen: das mathematisch exakte
min|Q| (unbekannt), ein numerisch berechnetes min|Q̂| (etwa über `PolynomialRoots`; ohne Fehlerschranke
keine sichere Untergrenze) und eine wirklich konservative Untergrenze (LB_q). Das Gate verwendet nur
die letzte. Das Modell-Gate ist das Maximum aus allen G_q, den Stationswerten und den Reaktionen.
Weil das normierte β_q(x) ≤ G_q für jedes x im Stück gilt, hat nach der Annahme **jeder** Wert von
`EvaluateAt(x)` und `Extrema` ein normiertes β ≤ τ. Die Lösung wird dabei nicht verändert, und
spätere Aufrufe rechnen nichts neu. Für den Axialanteil gilt dasselbe mit LB_N (4.3).

**Varianten verglichen** auf allen 10.346 exakt stabilen Modellen (840 gezielte, 9.506 zufällige)
**[state-gate-summary]**:

| Variante für M, V | geliefert / abgelehnt | davon abgelehnt mit Fehler ≤ τ | Entscheidungen anders als B |
|---|---|---|---|
| A: D_q = S_q | 10.072 / 274 | 266 | 263 |
| B: D_q = max(S_q, min\|Q̂\| aus 65 Stützstellen + Parabel) | 10.335 / 11 | 3 | – |
| C: D_q = max(S_q, LB_q) | 10.335 / 11 | 3 | 0 |

Variante A lehnt 263 korrekte Modelle zusätzlich ab (größter Fehler 2,3·10⁻¹²): Stücke zwischen
nahen Lagern und Federn der Länge 10⁻¹¹ L, in denen |V| bis 6·10¹⁰-mal größer ist als S_V; dazu
gehört die Klasse des 6-nm-Falls. Das ist fachlich nicht vertretbar. C trifft dieselben
Entscheidungen wie B. Ihr Gate ist in gelieferten Modellen höchstens 7 % größer, das liegt am Abzug
von β, und C kommt ohne Nullstellensuche aus. **Entscheidung: Variante C.**

**Gemessen** mit dichter Auswertung (Variante B; das Stück-Gate von C ist in allen Modellen ≥ B,
die Aussagen gelten also auch für C): je Stück 15
gleichverteilte Punkte und geometrisch an beide Enden heranrückende Punkte bis 2⁻⁴⁰ der Stücklänge,
auf 840 gezielten und 9.506 Zufallsmodellen **[state-coverage-*]**:

- β war bis auf die oben genannten Abfälle um höchstens 2 ulp am linken Stückende nirgends längs
  eines Stücks fallend (mit Toleranz 10⁻¹² keine Ausnahme).
- Das normierte β(x) lag nirgends über seinem Stück-Gate.
- Das *normierte* β ist im Inneren bis 4 % größer als an den Stückenden, weil |Q(x)| im Nenner
  variiert. Ein reines Stations-Gate wäre also nicht ausreichend.
- Der *tatsächliche* Fehler hat sein Maximum teils im Inneren (bis 22,5-fach über dem Endwert, auf
  Fehlerniveau 10⁻¹⁸). Auch deshalb setzt das Gate auf β und nicht auf Stichproben des Fehlers.
- In gelieferten Modellen lag der tatsächliche Fehler an keiner der dicht ausgewerteten Stellen über
  dem Gate, mit adjungierter Nachiteration auch an keiner Stelle über dem punktweisen β(x).
- Grenzbereich: 4 Modelle mit Gate in (10⁻¹⁰, 10⁻⁸], größter Fehler 2·10⁻¹¹, Gate/Fehler ≥ 31.
  Gezielt ergänzt (Runde 4): 75 Modelle mit Gate nahe τ (7.6), davon 41 in (10⁻¹⁰, 10⁻⁸].

**Zusagen:**

| Ausgabe | Zusage |
|---|---|
| Feldwert an beliebigem x (`EvaluateAt`, beide Seiten), Stationswerte, Reaktionen | Normiertes β ≤ Gate ≤ τ; sonst kein Ergebnis. Unter dem Rundungsmodell des Gleitkomma-Vertrags (V1–V6) und bei erfolgreicher Verifikation ist β eine obere Fehlerschranke, also auch der tatsächliche Fehler ≤ τ (Beweiskette [GV 4], Papierbeweis). Auf jeder Stufe gegen exakte Arithmetik geprüft, ohne Ausnahme ([GV 9], 7.6). Offen ist die Konformität der C#-Implementierung (O2). |
| Wert eines Extremums | Fehler ≤ β(x_c) + Positionsanteil. β(x_c) ≤ Gate ≤ τ wie bei jedem Feldwert. Der Positionsanteil \|Q(x_c) − Q(x*)\| ist *nicht* in β enthalten. Für einfache Wurzeln ist er zweiter Ordnung (gemessen ≤ 1,41 × \|Q''\|Δx²/2); für mehrfache oder fast mehrfache Wurzeln gilt nur ≤ \|Δx\|·max\|Q'\| zwischen x_c und x*, wo \|Q'\| auf Rundungsniveau liegt. Gemessen ≤ 8,7·10⁻²⁹ der Skala. Voraussetzung, die das Gate nicht abdeckt: Die Nullstellensuche findet jedes lokale Extremum. Geprüft gegen exakte Wurzelisolation (Sturm, quadratfreie Zerlegung, Gegenprobe mit sympy) auf 1.833 Modellen einschließlich doppelter und fast doppelter Wurzeln: kein übersehener Kandidat, Extremwertfehler gelieferter Modelle ≤ 3,1·10⁻¹⁴ **[state-piece-*]**, [Audit 3.6]. Das ist eine experimentell sehr stark validierte, aber keine mathematisch zertifizierte Kandidatenvollständigkeit: `PolynomialRoots` hat keinen Vollständigkeitsbeweis. Vorschlag: keine zertifizierte Vollständigkeitszusage (Variante B, [GV 11]); `PolynomialRoots` bleibt stark validierter Produktalgorithmus, das exakte Oracle wird Testreferenz. Endgültig ist das eine Vertragsentscheidung (O5). |
| Ort eines Extremums | Keine Genauigkeitszusage. Zugesagt ist die Reproduzierbarkeit: `EvaluateAt` am gemeldeten Ort und der gemeldeten Seite liefert den gemeldeten Wert. Das gilt durch Konstruktion, wenn der Wert erst nach dem Runden von x = x₀ + t·h mit der Seite aus dem exakten Vergleich mit den Stückenden ausgewertet wird (gemessen: alle Kandidaten reproduzierbar). Bei flachen Extrema ist der Ort schlecht konditioniert. |
| ohne Anregung (7.2) | alle Werte exakt 0 |

τ ist ein **Produktwert**, kein Tuning-Parameter der Numerik. Vorschlag: **τ = 10⁻⁸** im Maß aus 7.2,
gleich für alle Größen. Begründung: Ergebnisse werden mit bis zu 15 Stellen angezeigt
([ENGINEERING.md](ENGINEERING.md#numerische-darstellung-in-app-und-bericht)), fachlich relevant sind
höchstens 4–6. τ lässt damit zwei Größenordnungen Reserve. Gelieferte Ergebnisse hatten tatsächlich
höchstens 2·10⁻¹¹ Fehler, typisch 10⁻¹⁶ (Median) bis 10⁻¹⁴ (p99) **[state-acceptance-summary]**.

### 7.4 Gültigkeitsbereich

Der Vertrag gilt für alle Modelle, die:

- Eingabeprüfung und Mechanismusprüfung bestehen,
- alle Bereichsregeln des Gleitkomma-Vertrags erfüllen ([GV 7]: Eingaben, Koeffizienten mit
  Zwischenwerten, normierte Anregungen, Einträge nach Ruiz und Pivots normal oder exakt null),
- deren adjungierte Lösungen verifiziert sind (μ < 1, 6.3) und deren Gate ≤ τ ist,
- deren SI-Bezugsgrößen in [2⁻⁹⁶⁹, 2¹⁰⁰⁰] liegen (7.2),

und er gilt unter den Plattformvoraussetzungen V1–V6 [GV 2]. FTZ/DAZ gehört nicht dazu.

Eine Zusage zum *Anteil* gelieferter Modelle gibt es nicht. Sie hängt von der Kondition des
Modells ab und ist nur statistisch beschrieben (7.6).

### 7.5 Prüfbarkeit

- **Vertragstest (hart, ohne Python):** eingecheckte exakte Sollwerte. Jeder Fall prüft entweder
  „geliefert und tatsächlicher Fehler ≤ τ“ (an Stationen, im Feld und an Extremwerten) oder den
  erwarteten Ablehnungscode. Nullanregungsfälle prüfen exakte Nullen.
- **Schranke (hart):** Unter dem Rundungsmodell des Gleitkomma-Vertrags und bei erfolgreicher
  Verifikation ist β eine Schranke (6.3). „Fehler ≤ β“ an jedem Sollwert wird deshalb harte
  Testbedingung. Eine Verletzung bedeutet eine Abweichung der Operationsfolge vom Vertrag, eine
  falsche Rundungszahl oder eine fehlende Aufrundung und wird einzeln untersucht (6.4), nicht durch
  Kalibrierung behoben. Gemessen: 2.811 Modelle (Runde 4) und 452 Kettenläufe plus 2.295
  Zufallsmodelle (Runde 5) ohne eine Verletzung **[state-refine-summary, state-fpc-summary]**.
- **Konformität mit dem Gleitkomma-Vertrag (hart, Runde 5):** die Stufen A–F aus [GV 9] mit
  eingecheckten exakten Sollwerten. A: jede Formel gegen ihre Schranke auf den strukturierten
  Grenzfällen. B: |r_exakt| ≤ d. C: |I − R S*| ≤ M. D: Fehler ≤ E. E: Feldwerte, Majorante und
  Untergrenze im Stück. F: SI-Ausgabe und Bereichsfälle. Dazu ein Bit-Vergleich je Formel gegen
  `state/fpc.py` auf festen Eingaben und Plattformtests für V1, V2, V5 auf x64 und ARM64. Alle
  Prüfungen auch mit eingeschaltetem FTZ/DAZ.
- **Indikatorgüte (überwacht, keine Testinvariante):** Die Verteilung β/Fehler je Größe wird als
  Qualitätskennzahl gegen ein dokumentiertes Band verfolgt (heute 1. Perzentil ≥ 10¹, Median ≈ 10²,
  **[state-acceptance-summary]**). Sie misst den Pessimismus (O3), nicht die Gültigkeit.
- **Gate-Abdeckung (hart):** normiertes β(x) ≤ Stück-Gate an dichten Punkten jedes Stücks; die
  Untergrenze LB_q ≤ |Q̂(x)| des berechneten Feldes an denselben Punkten (rigoros, daher hart;
  gegen das exakte Feld nur überwacht, 7.3). Monotonie im *offenen* Stück wird für die
  heutigen Lastprimitive ohne Toleranz geprüft. Am linken Stückende ist ein Abfall auf
  Rundungsniveau zulässig, dort greift das Stationsgate.
- **Selbsttests (hart, mit eingebauten Fehlern):** Identität von Sprung und Federgesetz,
  Gleichungszahl, Endlichkeit. Ein absichtlich verfälschter Koeffizient (k, Stationslast,
  Lastintegral) muss als `InternalInconsistency` erkannt werden.
- **Verifikation (hart, mit gestörter Faktorisierung):** Mit um 10⁻¹ gestörten LU-Faktoren muss jedes
  gelieferte Modell eine verifizierte Schranke haben (Fehler ≤ β an allen Werten), und die Fälle
  mit β unter dem Fehler müssen als nicht verifiziert abgelehnt werden (6.3).
- **Faktorisierung (hart):** Bandbreite (2,2) und Multiplikatorabstand ≤ 2 für die gezielten
  Familien (Invariante 2); Pivotwachstum ρ protokolliert; ein Beispiel mit Gradierung, bei der QR
  scheitert und GEPP liefert (`gemischt|1e-09|0`, 5.2).
- **Exakte Oracles (Testreferenz, nicht Produkt):** Extremwerte gegen exakte Wurzelisolation, Stück-
  Untergrenze gegen exakte Bernstein-Einschließung (`state/piece_oracle.py`, 7.3).
- **Unterlauf (hart):** die Fälle aus [Audit 3.4], insbesondere der Kleinstbalken mit unterlaufender
  SI-Rückrechnung → `NumericalRange`.
- **Fehlercodes (hart):** Für jede Ursache aus 6.6 mindestens ein Fall mit genau diesem Code.
  Fälle mit großem ω, aber kleinem Gate werden geliefert (ω ist keine Grenze).
- **Aufwand (Zähltest hart, Zeit verfolgt):** wie in 5.6.
- **Diagnosen:** Die 1-ulp-Sensitivität wird zu Referenzfällen als diagnostische Zusatzinformation
  geführt, nicht als Begründung einer Ablehnung. Ein Test stellt sicher, dass Diagnosen Lösung und
  Status nicht ändern (6.5).
- **Untersuchungsläufe** wie in 7.6 bleiben Entwicklungswerkzeuge für neue Grenzfälle.

### 7.6 Gemessene Folgen

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
  Ablehnungen **[state-acceptance-summary]**. Mit dem endgültigen Gate (Variante C, β-Regel der
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
- **Regression Runde 5** (Gleitkomma-Vertrag, jedes 5. Zufallsmodell): 2.295 exakt stabile Modelle,
  2.291 geliefert, kein Wert über seiner Schranke, größter gelieferter Fehler 2,5·10⁻¹³; dieselbe
  Entscheidung wie Runde 4 in 1.894 von 1.896 Modellen. Die zwei Abweichungen sind die früher nicht
  verifizierbaren Modelle, jetzt geliefert **[state-fpc-summary]**.
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
| Reaktion | je Halterung: Position, Teilpunkt, Richtung, Kraft bzw. Moment |
| Stück | Grenzen, Feldpolynome |
| Extremwerte | w, V, M (N nach DEV-12) mit Ort und Seite |
| Genauigkeitsbericht | Gate und größtes normiertes β je Größe relativ zu τ, Verifikation (μ), „ohne Anregung“, Nachiterationsschritte; Diagnosen (Sensitivität, ω) getrennt davon und nur in Entwicklung/Validierung |

**`BeamSolution` als Fassade** erhält den heutigen Vertrag:

- `Nodes` sind die Ereignisstationen; das sind dieselben Positionen wie die heutigen FE-Knoten.
  `NodeIndex` ist der Stationsindex. u, w und θ sind die rechtsseitigen Werte, ReactionX/Y/Moment
  die je Station summierten Reaktionen, wie heute.
- `EvaluateAt(position, side)` und `Extrema` bleiben unverändert in Signatur und Semantik,
  einschließlich der Gleichstandsregeln aus [SOLVER.md](SOLVER.md#globale-extremwerte).
- Neu kommen hinzu: Werte links und rechts je Station (θ am Gelenk), Sprungarten, Reaktionen je
  Halterung, Genauigkeitsbericht. Ob `Nodes` später umbenannt wird, entscheidet die Integration.

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
- Core bleibt frei von Numerik. Die Koinzidenzregeln liegen im Solver, ihre fachlichen Einträge
  kommen aus den Tracks.

## 11. Validierungsstrategie (A7)

**Übernahme in dauerhafte Abnahmefälle** (exakte Sollwerte, eingecheckt, CI ohne Python):

| Quelle | Fälle |
|---|---|
| Gezielte Matrix (`state/cases.py`) | jede Konfiguration bei L/10³, L/10⁷, L/10¹¹ und k·L³/EI = 1, 10⁶, 10¹² |
| `s_fuzz.py t` / `s_detail2` | Federtabelle (Paar, Nebenlager, nur Federn, elastische Einspannung, am Gelenk, drei, Drehpaar) |
| Zufall | je Klasse die schwierigsten gelieferten Modelle und alle abgelehnten (erwarteter Code; Sensitivität als Diagnose) |
| Anregung | die sechs Nullfälle aus `state/zero.py`, kompensierte Lasten, P und −P am selben Ort, Unterlauf 10⁻³¹⁰ und 10⁻³²⁰ N (erwartet `NumericalFailure`, Ursache `NumericalRange`) |
| Axial | Zufallsmodelle aus `state/zero.py` gegen die exakte Axialreferenz `state/axial.py`, mit Gate (`state/axial_quality.py`) |
| Federn extrem | die 144 Fälle aus `state/spring_extremes.py` (k·L³/EI bis 10¹⁵, Abstände bis L/10¹¹) |
| Numerisch singulär | Balken nur auf zwei Federn im Abstand 5·10⁻¹² m (erwartet `IllConditionedSystem`) |
| Grenzfälle nahe τ | die 75 Modelle aus `state/near_tau.py` (Gate 10⁻¹⁰ … 3·10⁻⁸), erwartete Entscheidung je Fall |
| Extremwerte | die 37 konstruierten Fälle aus `state/piece_study.py` (Wurzel am Rand, doppelte und fast doppelte Wurzeln, Extremum neben Ereignisstation, kurze Stücke, Skalen), Sollwerte aus exakter Wurzelisolation |
| Unterlauf | die 64 Fälle aus `state/underflow_study.py`, darunter der Kleinstbalken mit unterlaufender SI-Rückrechnung (erwartet `NumericalRange`) |
| Faktorisierung | Stressfamilien und die Modelle, an denen QR scheitert (`state/factor_study.py`, `state/cond_check.py`) |
| Gleitkomma-Vertrag | Stufen A–F aus `state/fpc_stage_a.py` und `state/fpc_chain.py`: Formelfamilien mit strukturierten Grenzwerten, die 452 Kettenläufe (gezielt, nahe τ, gestörte LU, Unterlauf, Nulldurchgang, kurze schwere Lasten, EI-Wechsel 1 ulp neben Feder, 6-nm-Paar), je mit und ohne FTZ/DAZ; Sollwerte aus exakter Arithmetik |
| Bestehend | V01–V15, 6-nm-Fall (`SixNanometerRenderingTests`), 35 CAD-Fälle aus `compare.py`, Prototyp-Tests (Gelenk, Federn, EI, Linearlast) |

Geprüft werden einzelne Reaktionen sowie w, θ, M und V an Stationen (beide Seiten) und im Feld,
außerdem Extremwerte. Erzeugt werden die Sollwerte von beiden exakten Referenzen, mit `xcheck.py`
bitgleich gegeneinander geprüft. Für den Axialanteil gibt es bisher eine exakte Referenz
(`state/axial.py`); eine zweite unabhängige ist zu ergänzen (O4).

**Neue Architekturtests:**

- Phantomstation: Eine zusätzliche Last ändert die Matrix nicht (Invariante 1).
- Strukturzählung: Gleichungen = Unbekannte, Bandbreite höchstens (2,2), Multiplikatoren höchstens 2
  unter der Diagonale (Invariante 2).
- Verifikation: mit gestörten LU-Faktoren nie ein verifiziertes β unter dem Fehler (6.3, 7.5).
- Zweierpotenz-Skalierung bitgleich; allgemeiner Einheitenwechsel innerhalb des Vertrags (zwei
  getrennte Tests).
- Spiegelung x → L − x, Superposition, Reihenfolge der Eingabe: innerhalb des Vertrags.
- Sprungbilanzen an jeder Station: V⁺ − V⁻ = P + R, M⁺ − M⁻ = −(C + C_R), θ-Sprung nur am Gelenk;
  geprüft gegen die Summe der beteiligten Indikatoren, nicht exakt und nicht gegen τ (4.2).
- Vertrag: gelieferte Sollwertfälle haben tatsächlichen Fehler ≤ τ (hart). Die Indikatorgüte
  (β/Fehler-Band) wird überwacht, ist aber keine Testinvariante (7.5).
- Gate-Abdeckung: β(x) ≤ Stück-Gate und LB_q ≤ |Q̂(x)| an dichten Punkten; Monotonie im offenen
  Stück (7.3, 7.5).
- Nullanregung: exakte Nullen ohne Lösung, auch beim numerisch singulären Modell; Unterlauf der
  Anregung → `NumericalFailure`.
- Diagnosen ohne Rückwirkung: Lösung und Status mit und ohne Sensitivität bitgleich.
- Federreaktionen: Identitätsprüfung (4.2) mit eingebauten Fehlern; Wahl nach kleinerem β;
  weiche Feder zwischen zwei nahen Lagern und sehr steife Federn als Sollwertfälle.
- Fehlercodes: je Ursache aus 6.6 ein Fall; Fälle mit mehreren Ursachen prüfen die Reihenfolge aus
  6.1; ein Fall mit ω ≈ 1 und kleinem Gate wird geliefert.
- Adjungierte Nachiteration: mindestens ein Schritt, Schrittzahl ≤ 5, Abbruch nach der β-Regel.
- Aufwand: Zahl der adjungierten Lösungen = n + n_r, Bandbreite unabhängig von der Stationszahl,
  Zeitbudget bei n_m = 10, 30, 100 und bei 1.000 Lasten in einem Abschnitt (5.6).
- Mechanismus: Bareiss-Urteil gleich exakter Singularität (Sollwerte).
- Unerreichbarkeit nicht freigegebener Primitive über `EulerBernoulliBeamSolver.Solve`.
- Zuordnung: 1-ulp-Abschnitt mit Eigenschaftswechsel; Last 1 ulp vor einer Station.

**Eingebaute Fehler:** Die Fehlerklassen aus `mutate2.py`/`mutate3.py` werden auf die neuen Regeln
übertragen (je Stationsregel, Übertragungskoeffizient, Lastintegral, Elimination, Nachiteration,
Indikator, Extrema). Jeder nicht äquivalente Mutant muss von dauerhaften Tests erkannt werden.

**Plattform:** Der Kern benutzt nur +, −, ×, ÷, √ (nur zur Wahl der Ruiz-Faktoren), `Math.ScaleB`/
`ILogB` (Zweierpotenzen), `Math.BitIncrement`/`BitDecrement` (gerichtete Rundung der Schranken) und
exakte Vergleiche. `Math.Pow` für kleine Potenzen und FMA sind ausgeschlossen. Dass der JIT ohne
ausdrückliches `FusedMultiplyAdd` nicht zu FMA kontrahiert, ist Voraussetzung V2 und wird auf x64 und
ARM64 getestet [GV 2]. FTZ/DAZ ist keine Voraussetzung der Garantie [GV 7]. Bitgleichheit zwischen macOS
und Windows ist damit *angestrebt*, aber nicht gemessen (O12). Geprüft wird sie über einen
Ergebnis-Hash in der Windows-CI. Gemessen ist bisher nur die bitgleiche Zweierpotenz-Invarianz
innerhalb einer Plattform.

## 12. Migration (A10)

| Phase | Inhalt | Ende |
|---|---|---|
| M1 | Neuer Kern im Solverprojekt neben dem bestehenden Pfad, interner Einstieg, Adapter für den heutigen Funktionsumfang | Kern rechnet alle heutigen Modelle |
| M2 | Sollwertfälle, Architekturtests, Mutationslauf; alter FE-Solver als Vergleich auf V01–V15 und den CAD-Fällen | Abnahme grün auf macOS und Windows |
| M3 | `EulerBernoulliBeamSolver.Solve` auf den neuen Kern; `BeamSolution`-Fassade; `SupportSpanSolver`, `SolverLoads`, `ReducedSystemSolver`, `DofMap`, `BeamAssembly`, `EulerBernoulliElement` verlassen den Produktpfad | ein Produktionssolver |
| M4 | [KONZEPT.md](KONZEPT.md), [SOLVER.md](SOLVER.md), [VALIDATION.md](VALIDATION.md), CI-Solver-Hash anpassen | Voraussetzungen für `main` erfüllt |
| M5 | Feature-Tracks nacheinander (Koinzidenzregeln, Core-Typ, Adapter, Freigabe) | je Track |

**Alter FE-Solver:** Er wird als eingefrorene Kopie in ein Test- oder Validierungsprojekt verschoben
und gehört nicht mehr zum Produkt. Er ist ein unabhängiges, *nicht exaktes* Verfahren für den
heutigen Funktionsumfang mit dokumentiertem Gültigkeitsbereich: gut getrennte Stationen,
Abweichung bei engen Stationen bekannt **[compare-rand]**. Er dient als Differenzialtest im Sinne
von A7 „Fremdsolver“, wird nicht weiterentwickelt und bekommt keine neuen Features. Der Prototyp wird
nach M3 entfernt; seine Testfälle bleiben als Sollwertfälle erhalten.

## 13. Offene Entscheidungen und Risiken

| Nr. | Punkt | Art |
|---|---|---|
| O1 | τ = 10⁻⁸ bestätigen (Produktwert) | Entscheidung vor `main` |
| O2 | Mathematisch für die festgelegte Operationsfolge geschlossen (Runde 5): Gleitkomma-Vertrag mit verbindlichen Folgen, Rundungszahlen aus der Folge abgeleitet, Beweiskette unter V1–V6, Stufen A–F ohne Verletzung, auch mit FTZ/DAZ [GV]. Verschoben, nicht geschlossen: die Konformität der C#-Implementierung mit diesen Folgen (Testpflicht: Stufen A–F mit Sollwerten, Bit-Vergleich je Formel) und die Plattformtests V1, V2, V5 auf x64 und ARM64. Der Beweis ist ein Papierbeweis, kein maschinell geprüfter | Implementierung und Abnahme |
| O3 | Pessimismus von β (Median 10², p99 10⁴) lehnt korrekte Ergebnisse ab: 3 der 16 Ablehnungen der Zufallsläufe, alle 23 Ablehnungen der Grenzfälle nahe τ (Fehler ≤ τ, Gate/Fehler ≥ 11,8) (die 2 in Runde 4 nicht verifizierbaren Modelle sind mit Neumann-Gewichten verifiziert, 6.3). Hebel: adjungierte Lösung je kritischer Größe, genauere Rundungszahlen; kein Sicherheitsfaktor (s = 1). Eine Untergrenze ohne Abzug von β (Variante B) oder mit Bernstein brächte nichts (7.3) | Risiko |
| O4 | Axial: eine exakte Referenz und ein Indikator mit Gate liegen in `state/axial.py` und `state/axial_quality.py`; zweite unabhängige Referenz, Sollwerte und das Federgesetz der Axialfeder fehlen; der Gleitkomma-Vertrag (Operationsfolgen, Stufen A–F) ist für (u, N) noch nicht umgesetzt [GV 5, Punkt 24] | Vor Implementierung |
| O5 | Experimentell sehr stark validiert, nicht mathematisch zertifiziert: exakte Wurzelisolation als Oracle, kein übersehener Kandidat auf 1.833 Modellen, Extremwertfehler ≤ 3,1·10⁻¹⁴ (7.3, [Audit 3.6]). `PolynomialRoots` (Toleranzen 64ε, Clusterregel) hat keinen Vollständigkeitsbeweis. Vorschlag Variante B: keine zertifizierte Vollständigkeitszusage, `PolynomialRoots` bleibt validierter Produktalgorithmus, das Oracle wird Testreferenz [GV 11]. Variante A (zertifizierter Fallback) nur, wenn das Produkt Vollständigkeit zusagen will | Vertragsentscheidung |
| O6 | Koinzidenztabelle je Track (Gelenk auf Lager, Feder auf Lager, Abschnittswechsel an Halterung, Moment auf Gelenk) | Track |
| O7 | N-Extrema in der Schnittstelle (DEV-12) | Track |
| O8 | Eingabesensitivität anzeigen ja/nein | Produkt |
| O9 | Aufwand (5.6, Größenordnung in [GV 12]): Engpass ist der Qualitätsvertrag mit n + n_r adjungierten Lösungen und 1 + 3 Verifikationsdurchgängen, nicht die Faktorisierung. Band, alles speichern: bei n_m = 100 etwa 9 ms und 1,9 MB, bei 1.000 etwa 0,9 s und 190 MB, bei 5.000 etwa 23 s und 4,6 GB. Neu lösen statt speichern: Faktor ≈ 3,7 Zeit, Speicher O(n). Dicht ab etwa 250 Stationen im Sekundenbereich. Schätzungen aus Operationszahlen. Zu entscheiden: größte unterstützte Modellgröße, Latenzziel für Live-Diagramme, Speichern oder neu lösen; C#-Zeitmessung fehlt | Produkt und Implementierung |
| O10 | Bestätigung „keine Federgruppierung nötig“ durch die C#-Abnahme | Implementierung |
| O11 | Umbenennung von `IllConditionedSystem` / `Nodes` | Integration |
| O12 | Bitgleichheit macOS/Windows nachweisen (bisher nur angestrebt) | Abnahme |
| O13 | Geschlossen (Runde 4): 75 gezielte Grenzfälle, 41 mit Gate in (10⁻¹⁰, 10⁻⁸], kein gelieferter Fehler > τ, Gate/Fehler ≥ 11,8 (7.6). Die Fälle werden in die Abnahme übernommen | – |
| O14 | Anregungsskala für spätere Anregungsarten (Lagersenkung, Temperatur) je Track konkret festlegen; ebenso die Majorante von β und die Schranke für die Änderung von \|Q\| im Stück (7.3) für jeden neuen Lasttyp | Track |
| O15 | Geschlossen (Runde 4): Ein überwiegendes Residual macht β scharf, aber mit verifizierter adjungierter Lösung nicht falsch; bei 10 % gestörter Faktorisierung lag der Fehler in jedem verifizierten Modell unter β (Messung, 6.3); seit Runde 5 folgt das aus der Beweiskette des Gleitkomma-Vertrags | – |
| O16 | Geschlossen (Runde 4, präzisiert Runde 5): Verifikation der adjungierten Lösungen nach Rump (2010, Satz 10.2), μ < 1 nach oben gerundet, Gewichte mit Neumann-Schritten, sonst Ablehnung mit Ursache `NotVerified` (6.3, [GV 6, 10]) | – |
| O17 | Voraussetzungen der Faktorisierungswahl: lokale Kopplung (Band (2,2)) und polynomiale Übertragung. Eine Bedingung zwischen nicht benachbarten Stationen oder Exponentialmoden (elastische Bettung, Theorie II. Ordnung) verlangen eine neue Prüfung (5.2, [Audit 3.1]) | Track |
| O18 | Geschlossen (Runde 5): Graduelles Unterlaufen ist keine Voraussetzung mehr. Lastnormierung, λ = 2⁻¹⁰²², Software-Flush und Bereichsregeln machen die Garantie unabhängig von FTZ/DAZ/FZ; geprüft mit emuliertem FTZ/DAZ auf allen Stufen [GV 7, 9]. Ein Starttest bleibt Plattformdiagnose. Bitgleichheit über Modi und Plattformen gehört zu O12 | – |
| O19 | Plattformvoraussetzungen V1, V2, V5 auf x64 und ARM64 im Release-Build messen (keine implizite FMA-Kontraktion, `ScaleB`/`BitIncrement` an Grenzwerten) [GV 2] | Abnahme |
| O20 | Gerichtete Rundung der Schranken (L5) und Software-Flush sind neue Implementierungspflichten; ihre Kosten sind in [GV 12] nicht gesondert gemessen | Implementierung |

## 14. Abgleich mit A1–A10 und den Feature-Tracks

| Anforderung | Erfüllung im Entwurf | Nachweis / offen |
|---|---|---|
| A1 Stationen und Unbekannte | E2, E3, Invariante 1, 2 | Phantomstationstest; gezielte Matrix |
| A2 Positionen | Invariante 3, 4; E5 Teilpunkte mit Koinzidenztabelle | Verletzungsanalyse 6.4; Semantiken O6 |
| A3 Modell und Numerik | Invariante 5; nur exakte Skalierungen | Ablation |
| A4 Gemeinsame Mechanik | Eine Formulierung, Tabelle 4.2, Axial 4.3, Erweiterungen 4.5 | Axialreferenz O4 |
| A5 Stabilität, Kondition, Fehler | 6.1–6.6; drei Fälle getrennt; vollständige Code-Zuordnung mit strukturierter Ursache (6.6, `NotVerified` neu); Schranke unter dem Gleitkomma-Vertrag und Verifikation statt Pauschalgrenze; ω, Kondition und Sensitivität nur Diagnose; Selbsttests → `NumericalFailure`; Bereichs- und Subnormalvertrag unabhängig von FTZ/DAZ, auch für die SI-Rückrechnung; Literaturabgleich jeder Regel ([Audit]) | Ablehnungen 7.6; O2 (C#-Konformität), O3, O17, O19 |
| A6 Felder und Extremwerte | Abschnitt 8, beide θ am Gelenk, Sprungarten; Extremwerte gegen exakte Wurzelisolation geprüft | Vollständigkeit validiert, nicht zertifiziert (O5, Variante B vorgeschlagen); N-Extrema O7 |
| A7 Validierung | Abschnitt 11, zwei exakte Referenzen, Mutationen, exakte Oracles für Stück-Gate und Extremwerte | Axial O4 |
| A8 Numerischer Vertrag | Abschnitt 7 und Gleitkomma-Vertrag: Anregungsskala und Nullfall, Bereichsregeln einschließlich SI-Rückskalierung, Gate über den ganzen Balken mit Majorante und geschlossener Untergrenze (unabhängig gegen exakte Bernstein-Einschließung geprüft), s = 1 auch nahe τ belegt, getrennte Zusagen für Feldwert, Extremwert (mit Positionsanteil) und Extremort; Axialanteil mit demselben Gate | τ O1, O2, O5 |
| A9 Grenze Fachmodell | Abschnitt 10 | – |
| A10 Migration | Abschnitte 9, 12 | Windows O12 |
| DEV-20 Gelenke | Momentengelenk als Stationsregel; θ links/rechts; exakte Mechanismusprüfung über Teilkörper | Koinzidenz O6 |
| DEV-19 Federlager | Weg-, Dreh-, Kombi-Federn als Gesetz auf dem lokalen Zustand; keine Gruppierung; Reaktion aus Sprung oder Federgesetz nach kleinerem Indikator, Identität beider Wege als Selbsttest (4.2); k = 0 entscheidet der Track | O10; axiale Feder als Regel möglich, Federgesetz axial O4; Feder an starrem Lager O6 |
| DEV-11 Mehrsegment | Abschnittswechsel als mechanische Station mit EI und EA; Zuordnung über Stationsfolge | Wechsel an Halterung O6 |
| DEV-6 Linearlasten | Lastintegrale `Difference`/`Ramp` mit globalen Abständen; Querkraftextremum im Feld | Extrema O5; ein oder zwei Lasttypen ist Core-Frage |
| DEV-12 Axial/schräg | Entkoppeltes (u, N)-System mit eigenem Indikator und Gate (4.3); Zerlegung im Adapter außerhalb der Vertragsgrenze | Axialreferenz O4; N-Extrema O7 |
