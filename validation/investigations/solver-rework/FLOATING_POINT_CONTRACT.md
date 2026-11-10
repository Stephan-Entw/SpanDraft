# Gleitkomma-Vertrag des Rechenkerns (O2)

Arbeitsdokument der Runde 5 zum Architekturentwurf
[`docs/SOLVER_ARCHITEKTUR.md`](../../../docs/SOLVER_ARCHITEKTUR.md), **nicht freigegeben**. Es legt
fest, welche Gleitkommaoperationen der spätere C#-Kern für jede numerisch relevante Formel ausführt,
welche Rundungs- und Bereichsfehler daraus entstehen und unter welchen Voraussetzungen die
Fehlerschranke β gilt. Die ausführbare Form ist `state/fpc.py`: Sie rechnet genau die Folgen dieses
Dokuments und dient als Referenz für die C#-Konformitätstests. Jede Zahl verweist auf ein Protokoll
**[Name]** in `results/`. Quellen in eckigen Klammern sind die von
[`LITERATURE_AUDIT.md`](LITERATURE_AUDIT.md).

## 1. Ergebnis in Kürze

- **Beweiskette:** Für die hier festgelegte Operationsfolge ist die Kette von der lokalen Rundung
  bis zu β und Gate geschlossen, unter den Plattformvoraussetzungen V1–V6 (Abschnitt 2). Jede
  Schranke folgt aus der Operationsfolge und den Lemmata L1–L7. Keine Zahl ist an Messungen
  angepasst. Der Beweis ist ein Papierbeweis (Abschnitte 3–6). Die Rundungszahlen leitet
  `state/fpc_counts.py` mechanisch aus den Folgen ab. Ein maschinell geprüfter Beweis liegt nicht vor.
- **Prüfung:** In 452 Kettenläufen auf schwierigen Referenzmodellen und 2.295 Zufallsmodellen der
  Runde 4 lag kein tatsächlicher Fehler über seiner Schranke. Das gilt für jede Stufe der Kette und
  auch mit emuliertem Flush-to-Zero (FTZ, subnormale Ergebnisse werden 0) und
  Denormals-are-Zero (DAZ, subnormale Operanden werden als 0 gelesen) (Abschnitt 9).
- **Gegenbeispiel zum bisherigen Rundungsmodell:** Das Modell von `state.py` aus Runde 1–4
  (`K_DIST = 40` auf |t₁| + |t₂|) unterschätzt den Fehler der Lastintegrale bis zum
  9,4·10¹³-Fachen, wenn eine Linearlast mit Nulldurchgang an einer Station nahe dem Nulldurchgang
  abgeschnitten wird. Der Datenfehler der abgeschnittenen Intensität fehlte. Auf Ausgabewerten ist
  das in den untersuchten Modellen nicht sichtbar, logisch war die Schranke dort aber unvollständig
  (Abschnitt 9.1).
- **Unabhängig von FTZ/DAZ:** Mit Lastnormierung der rechten Seite, dem Unterlaufterm λ = 2⁻¹⁰²²
  je Operation, Software-Flush gespeicherter Vektoren und Bereichsregeln gilt die Garantie mit
  graduellem wie mit abruptem Unterlauf. FTZ/DAZ ist keine Voraussetzung mehr, höchstens Diagnose
  (Abschnitt 7).
- **Verifikation:** β wird nach Rump verifiziert (Abschnitt 6). Das Verfahren braucht nach oben
  gerundete Schrankenarithmetik; .NET emuliert sie ohne Rundungsmodus über `Math.BitIncrement`. Die
  zwei in Runde 4 nicht verifizierbaren Modelle scheiterten an einer vermeidbaren Gewichtswahl,
  nicht an fehlender Information. Drei Neumann-Schritte für den Gewichtsvektor verifizieren beide
  rigoros (Abschnitt 10).
- **Offen nach dieser Runde:** die Konformität der C#-Operationsfolgen mit diesem Dokument
  (Testpflicht), die Plattformvoraussetzungen V2 und V5 auf x64 und ARM64 (Messpflicht), der
  Axialanteil (O4) und die Vollständigkeit der Extremwertkandidaten (O5, Vertragsvariante B). Die
  genaue Liste steht in Abschnitt 13.

## 2. Voraussetzungen

| Nr. | Voraussetzung | Prüfung |
|---|---|---|
| V1 | IEEE 754 binary64 [IEEE19], Rundung zur nächsten Zahl (gerade bei Gleichstand) für +, −, ×, ÷ und √, keine erweiterte Zwischengenauigkeit. .NET auf x64 (SSE2) und ARM64 rechnet so; 32-Bit-x87 ist ausgeschlossen. | C#-Selbsttest mit Gleichstandsfällen |
| V2 | Keine Kontraktion zu FMA: Jede Operation des Vertrags wird einzeln gerundet. `Math.FusedMultiplyAdd` kommt nur vor, wo der Vertrag es ausdrücklich vorsieht (nirgends). | C#-Test mit Eingaben, für die a·b + c und fma(a, b, c) verschieden runden, auf x64 und ARM64 im Release-Build |
| V3 | Unterlauf graduell **oder** abrupt (FTZ, DAZ, ARM FPCR.FZ). Ob Tininess vor oder nach dem Runden erkannt wird, ist gleich. Abgedeckt durch L4. | Abschnitt 7; Startdiagnose ohne Wirkung auf den Status |
| V4 | Kein Überlauf auf einem angenommenen Pfad: Die Bereichsregeln (Abschnitt 7) schließen ihn aus, sonst `NumericalRange` bzw. `NonFinite`. | Bereichsregeln, Endlichkeitstest |
| V5 | `Math.ScaleB` skaliert exakt mit einer Zweierpotenz und rundet nur bei Unterlauf; `Math.ILogB` liefert den Exponenten; `Math.BitIncrement`/`BitDecrement` liefern die Nachbarzahl; `Math.Abs` und Vergleiche sind exakt. | C#-Selbsttest an Grenzwerten (2⁻¹⁰²², subnormal, ±0) |
| V6 | Das Modell hat die exakte Mechanismusprüfung bestanden; die exakte Matrix S* ist dann regulär (SOLVER_ARCHITEKTUR.md 6.2). | Bareiss (E9) |

Vertragsgrenze wie in SOLVER_ARCHITEKTUR.md 7.1: Referenz ist die exakte Lösung zu den übergebenen
doubles. EI ist Solvereingabe. Die Untersuchung bildet EI = fl(E·I) selbst und zählt diese eine
Rundung konservativ als Datenfehler mit (k_e = 1).

## 3. Lemmata

u = 2⁻⁵³, γ(k) = k·u/(1 − k·u), γ̄(k) = (k + 1)·u (exakt darstellbar, ≥ γ(k) für k(k+1)u ≤ 1),
λ = 2⁻¹⁰²² (kleinste normale Zahl).

- **L1 Rundung einer Operation und Produktketten** ([Hi02] (2.4), (2.5), Lemma 3.1). Ist das Ergebnis
  r = fl(x ∘ y) normal, so gilt r = (x ∘ y)(1 + δ) = (x ∘ y)/(1 + δ′) mit |δ|, |δ′| ≤ u. Für einen Wert
  aus Produkten und Quotienten von Eingaben mit insgesamt k Rundungen gilt |v̂ − v| ≤ γ(k)|v|. Für
  die Summe zweier gleichvorzeichiger Terme mit k_a und k_b Rundungen gilt max(k_a, k_b) + 1, ohne
  Auslöschung. Das Programm zählt so: `T` in `state/fpc.py`, Ausgabe **[state-fpc-counts]**.
- **L2 Rekursive Summation** ([Hi02] Abschnitt 4.2). Für m berechnete Terme gilt
  |fl(Σ t̂) − Σ t̂| ≤ γ(m − 1)·Σ|t̂|, unabhängig von Vorzeichen und Reihenfolge.
- **L3 Umrechnung auf den berechneten Wert.** Aus |v̂ − v| ≤ γ(k)|v| folgt |v̂ − v| ≤ γ(k + 1)|v̂|
  ≤ γ̄(k + 1)|v̂|. Schranken können deshalb aus berechneten Beträgen gebildet werden.
- **L4 Unterlauf, plattformunabhängig.** Für Operanden, die normal oder null sind, gilt
  fl(x ∘ y) = (x ∘ y)(1 + δ) + ε mit |δ| ≤ u, |ε| ≤ λ, δ·ε = 0. Bei graduellem Unterlauf gilt sogar
  |ε| ≤ 2⁻¹⁰⁷⁵ [De84]. Bei FTZ wird ein Ergebnis unter 2⁻¹⁰²² zu 0, also |ε| < λ. Ein Ergebnis
  |r| ≤ λ kann gerundet sein, auch genau λ (Aufrunden aus dem subnormalen Bereich); der Vertrag
  behandelt deshalb |r| ≤ λ und nicht |r| < λ als „möglicherweise ungenau“. Eine
  Zweierpotenz-Skalierung ist exakt, wenn |r| > λ. DAZ wirkt nur auf subnormale *Operanden*; der
  Vertrag schließt sie für gespeicherte Werte aus (Software-Flush, Abschnitt 7).
- **L5 Nach oben bzw. unten gerundete Schrankenoperationen.** Für eine Operation mit RN-Ergebnis r
  (auch nach FTZ) und exaktem Ergebnis c gilt dn(r) ≤ c ≤ up(r). Dabei ist
  up(r) = BitIncrement(r) für |r| ≥ λ, sonst λ, und dn(r) = BitDecrement(r) für |r| ≥ λ, sonst −λ.
  Ist das exakte Ergebnis sicher 0 (ein Faktor 0, beide Summanden 0), wird 0 geliefert.
  Begründung: Bei RN liegt c zwischen den Nachbarn von r. Ist |r| < λ, so ist |c| < λ, auch unter
  FTZ (geflusht wird nur bei |c| < λ). up und dn sind monoton und liefern nie eine subnormale Zahl.
  Jede Schranke (d, β, Mv, μ, Gate, Bezugsgrößen) wird mit diesen Operationen ausgewertet.
- **L6 Verifikation** ([Ru10] Satz 10.2, komponentenweise). Abschnitt 6.
- **L7 Monotone Majorante im Stück.** Rundung zur nächsten Zahl ist monoton, ebenso up und dn.
  Im offenen Stück (x₀, x₁] ändert keine Lastformel ihren Zweig (SOLVER_ARCHITEKTUR.md 7.3). Die
  Schranke β̄(x) ist eine Summe nichtnegativer Terme aus Beträgen, die in s nicht fallen; ihr
  Unterlaufanteil ist im Stück konstant (Punkt 20). Daher ist die berechnete Schranke β̄(x)
  nichtfallend auf (x₀, x₁], und max(β̄(x₀⁺), β̄(x₁⁻)) majorisiert sie auf dem ganzen Stück.

## 4. Beweiskette

**Satz.** Ein Modell besteht die Prüfreihenfolge 6.1 aus SOLVER_ARCHITEKTUR.md, einschließlich der
Bereichsregeln, Verifikation μ < 1 und Gate ≤ τ, und es gelten V1–V6. Dann gilt für jede
gelieferte Größe y (Stationswert, Feldwert an beliebigem x, Reaktion) |ŷ_SI − y*_SI| ≤ β_SI(y)
und β_SI(y)/D_y ≤ Gate_SI ≤ τ. Dabei ist D_y der Nenner des Vertragsmaßes (7.2) mit den
Bezugsgrößen der exakten Lösung. Positionsanteil und Vollständigkeit der Extremwerte deckt der
Satz nicht (Abschnitt 11).

Kette (S* und sb* sind die Formeln dieses Dokuments in exakter Arithmetik auf den gespeicherten
doubles, ŷ die gespeicherte Lösung):

| Schritt | Aussage | stützt sich auf | Prüfstufe |
|---|---|---|---|
| K1 Daten | \|Ŝ − S*\| ≤ δS eintragsweise, \|ŝb − sb*\| ≤ δsb | Punkte 1–12, L1–L4 | A, B |
| K2 Residual | \|r*\| ≤ d := \|r̂\| + δr + δS\|ŷ\| + δsb mit r* = sb* − S*ŷ | Punkt 14, L2, L4 | B |
| K3 Adjungierte | M := S̄ + \|Ẑ\|ᵀδS ≥ \|I − R S*\| mit R = Ẑᵀ | Punkt 17, L2, L4 | C |
| K4 Verifikation | μ < 1 ⇒ \|ŷ − y*\| ≤ E := β + Mv/(1 − μ), β = \|Ẑ\|d | L6, L5 | C, D |
| K5 Funktionale | Reaktionen: \|ĉᵀ(ŷ − y*)\| ≤ β_c + m_cᵀE; Feld: \|v̂(x) − v*(x)\| ≤ Σ\|g\|E + Auswertung + Daten | Punkte 18–20, L1–L4 | D, E |
| K6 Gate | Untergrenzen der Nenner, Majorante je Stück, SI-Rückskalierung | Punkte 21, 23, L5, L7 | E, F |

Beweisskizze K2: r* = (ŝb − Ŝŷ) + (sb* − ŝb) − (S* − Ŝ)ŷ, und |r̂ − (ŝb − Ŝŷ)| ≤ δr. K4 und K5 in
Abschnitt 6. K6: β̄_SI(x) ist nach L7 auf dem Stück durch B majorisiert. Jeder Nenner ist eine
rigorose Untergrenze des Vertragsnenners (Punkt 21). Gate_SI enthält die Rückskalierung (Punkt 23).

## 5. Operationsvertrag

Bezeichnungen: e_L, e_E, e_F ganzzahlige Exponenten mit L_ref = 2^e_L, EI_ref = 2^e_E. 2^−e_F
normiert die rechte Seite (5.1 in SOLVER_ARCHITEKTUR.md). „k“ ist die Rundungszahl aus L1,
mechanisch abgeleitet **[state-fpc-counts]**. „Beitrag“ ist der Term in d, β oder Gate.
Summationsreihenfolgen sind für die Reproduzierbarkeit festgelegt; die Schranken gelten für jede
Reihenfolge (L2).

| Nr. | Bereich | Größe | Gleitkommafolge (verbindlich) | Rundungsmodell | Bereichsvoraussetzung | Beitrag |
|---|---|---|---|---|---|---|
| 1 | Globale Abstände | d = (x₂ − x₁)/L_ref | `ScaleB(x2 - x1, -eL)` aus zwei gespeicherten Koordinaten, nie aus lokalen | k = 1; bei \|x₂ − x₁\| ≤ λ (FTZ) absolut λ_d = λ·2^−e_L | Stationsabstände x₂ ≠ x₁: \|x₂ − x₁\| > λ, sonst `NumericalRange` | in Koeffizienten, Lasttermen, Auswertung |
| 2 | Entdimensionierung, Normierung | e_L, e_E aus `ILogB`, e_F aus dem berechneten F_exc; Faktoren für w, θ, M, V, Lasten, Federn | jede Umrechnung ein `ScaleB` | exakt, wenn \|Ergebnis\| > λ | Eingaben ≠ 0 normal; L ≥ 2⁻⁹⁶⁸; normierte Anregungsprimitive normal | keiner (exakt) |
| 3 | Abschnittsübertragung | w⁻, θ⁻, M⁻, V⁻ aus dem Zustand rechts der Vorstation | Matrixeinträge einzeln: 1, −a, −a/e, −a·a/2/e, −a·a·a/6/e; jeder Eintrag *eine* Formel, keine Summen in Einträgen | Eintrag: γ(k)\|S*\| mit k aus 4, 5 | – | δS_ij = γ̄(k+1)\|Ŝ_ij\| |
| 4 | Potenzen | a², a³, s², s³, Abstandspotenzen in Lasten | `a*a`, `(a*a)*a`, ohne `Math.Pow` | a·a: k = 3, (a·a)·a: k = 5 (mit Abstand) | alle Zwischenwerte der Koeffizienten > λ | über 3 |
| 5 | Division durch EI (EA) | e = EI/EI_ref; a/e, a·a/2/e, a·a·a/6/e | `ScaleB(EI, -eE)`; `((a*a)/2)/e`, `(((a*a)*a)/6)/e`, `a/e` | k = 3, 5, 8 (mit k_e = 1 der Untersuchung) | e > λ, jeder Zwischenwert > λ | über 3 |
| 6 | Lastintegrale `Difference`/`Ramp` | ∫ der Linearlast im Abschnitt | Intensität zuerst: q·n·n·…/p! (innen), q·d·(Polynom in near, far)/p! (jenseits); Rampe als `rise*(n/d)*n…/(p+1)!` bzw. `rise*d*(…)`; Polynome wie in `fpc.diff_term`/`ramp_term` | k je p: innen 2, 4, 7, 9 (Rampe 7, 10, 12, 14), jenseits 2, 5, 9, 11 (Rampe 3, 8, 10, 14); Unterlauf λ_d·64·(\|q\| + 8) je Term | skalierte Lastlänge ≥ 2⁻⁹⁶⁹ | je Term (γ̄(k+1) + γ̄(m−1))\|t̂\| + Daten + λ-Term |
| 7 | Punktkraft, Punktmoment | v·dᵖ⁻¹/(p−1)!, −C·dᵖ⁻²/(p−2)! | `v`, `v*d`, `((v*d)*d)/2`, `(((v*d)*d)*d)/6`; `-C`, `-C*d`, `((-C*d)*d)/2` | Kraft k = 0, 2, 4, 7; Moment k = 0, 2, 4 | normierte Last normal | wie 6 |
| 8 | Linear veränderliche Streckenlast | abgeschnittene Intensitäten q_a, q_b an Stationen | `v1 + (v2 - v1)*((c - la)/(lb - la))` auf normierten Werten; exakt, wenn c die Lastgrenze ist oder v₁ = v₂ | \|q̂ − q\| ≤ γ̄(6)\|(v₂ − v₁)f\| + u\|q̂\| + λ(\|v₂ − v₁\| + 8) | – | Datenterm δq_a·\|D₁\| + (δq_a + δq_b)·\|R₁\| je Lastterm (D₁, R₁ die Polynome mit Intensität 1) |
| 9 | Stationsgleichungen | rechte Seite: Lastintegral links + Stationslast | `L + Pst`, Pst rekursive Summe der Stationslasten | γ̄(m−1)Σ\|P\| + u\|b̂\| + λ bei \|b̂\| ≤ λ | – | δb |
| 10 | Federterme | k·L_ref³/EI_ref, k_θ·L_ref/EI_ref | `ScaleB(k, 3eL - eE)`; mehrere Federn einer Richtung am selben Teilpunkt: rekursive Summe | exakt bzw. k = m − 1 | skaliert > λ | δS |
| 11 | Matrixassembly | Zeilen je Station (4.2) | eliminierte Spalten fallen exakt weg; Gleichungszahl = Unbekannte | keine Rundung | – | Selbsttest `InternalInconsistency` |
| 12 | Ruiz | Zeilen- und Spaltenfaktoren 2^k | Faktorwahl beliebig (z. B. `ILogB` der Zeilenmaxima); Skalierung exakt | exakt, wenn Ergebnis > λ | jeder Eintrag ≠ 0 von S > λ, sonst `NumericalRange`; rechte Seite darf subnormal werden | δS, δsb mit denselben Faktoren (nach oben) |
| 13 | LU/GEPP | Faktoren L, U | GEPP wie 5.2 | **kein Modell nötig**: β misst das Residual der gespeicherten Lösung, beliebige Fehler von L, U wirken nur auf Residual und μ | Pivot = 0 → `NotReliablySolvable` (`ZeroPivot`); 0 < \|Pivot\| ≤ λ → `NumericalRange` | – |
| 14 | Residual primär | r_i = sb_i − Σ_j S_ij y_j | `r = sb[i]; r = r - S[i][j]*y[j]` in Spaltenreihenfolge, getrennt × und − | δr_i = γ̄(m+1)(\|sb_i\| + Σ\|S_ij\|\|y_j\|) + 2mλ (m Nichtnullen) | y ohne subnormale Komponente (Flush) | d |
| 15 | Primär-Nachiteration | y ← y + Δy | Regel A (5.2); nach jedem Schritt Flush | kein Modell nötig (a posteriori) | endlich | – |
| 16 | Adjungierte Systeme | Sᵀz = c | dieselbe Faktorisierung; Regel 1/16; Flush | kein Modell nötig | endlich | – |
| 17 | Residual und Verifikation der Adjungierten | s_j = c_j − Σ_i S_ij z_i; M v; μ | double wie 14 (Dot2 nur Option); M v, μ, E mit up/dn (L5); v = β + δD, dann 3 Neumann-Schritte | s̄_j = \|ŝ_j\| + γ̄(m+1)(\|c_j\| + Σ\|S_ij\|\|z_i\|) + 2mλ | v > 0 | M, μ, E (Abschnitt 6) |
| 18 | Reaktion aus Sprung | R = V⁺ − V⁻ − P bzw. M⁻ − M⁺ − C | `(Σ c_k*x_k + const) - Pst` (normiert), dann Rückskalierung | β_c + m_cᵀE + Σ γ̄(k_c+1)\|ĉ_k\|(\|x_k\| + E_k) + γ̄(m)·Σ\|Terme\| + δconst + 4mλ | – | Reaktions-Gate |
| 19 | Reaktion aus Federgesetz | R = −k·w⁺ | `-kn*w` | k_n·E_w + u\|R\| + λ | – | kleinere Schranke wird geliefert; Identität als Selbsttest (4.2) |
| 20 | Feldrekonstruktion | Q(x) vom Abschnittsanfang | s = Punkt 1; g wie 4.1 mit Folgen aus 4, 5 (k(g) w: 0, 1, 5, 8; θ: 0, 3, 5; M: 0, 1); `((((g0*X0 + g1*X1) + g2*X2) + g3*X3) + pv)` | Σ\|ĝ_k\|(1 + γ̄(k_g+1))E_k + Σ(γ̄(k_t+1) + γ̄(m−1))\|t̂\| + γ̄(m−1)P_mag + δP + λ_eval, λ_eval = 64λ_d(Σ\|X\| + 8)(1 + 1/e) | x beliebig, auch subnormal (λ_d) | β̄(x) |
| 21 | Stück-Gate | G_q = B/D_q | B = max(β̄(x₀⁺), β̄(x₁⁻)); LB_V = dn(max\|V̂_e\| − h·q_sum − 2B); LB_M = dn(max_e(\|M̂_e\| − (\|V̂_e\| + B_V)h − q_sum·h²/2) − 2B); D = max(S_low, LB); q_sum = Σ(\|q̂_a\| + \|r̂ise\|) nach oben | L5, L7 | – | Gate |
| 21a | Bezugsgrößen | S_low ≤ S (7.2, exakte Lösung) | Maxima über die Ereignisstationen (beide Seiten) von \|Q̂\| − β̄ nach unten; F_low nach unten; EI_min nach oben | L5 | – | Nenner |
| 22 | Extremwertauswertung | Wert am Kandidaten x_c | x_c gerundet, dann Punkt 20 an x_c mit der Seite aus dem exakten Vergleich | wie 20; β̄(x_c) ≤ B nach L7 | – | Positionsanteil nicht enthalten (Abschnitt 11) |
| 23 | SI-Rückskalierung | v_SI = v_n·2^E_q | Flush von v_n (\|v_n\| < λ → 0, +λ in normierten Einheiten), dann `ScaleB(v, Eq)` | β_SI = up(β_n·2^E_q) + λ, falls \|v_SI\| ≤ λ; Gate_SI = Gate + λ/S_q,SI | S_q,SI ∈ [2⁻⁹⁶⁹, 2¹⁰⁰⁰]; Obergrenzen je Stück in SI endlich | β_SI, Gate_SI |
| 24 | Axialanteil | (u, N): a/e_A, Lasttermen p = 1, 2 | gleiche Muster wie 1–23 | gleiche Lemmata | gleiche Regeln | **in `fpc.py` nicht umgesetzt**, Teil von O4 |

**Formeländerungen gegenüber `state.py`** (gleiche Mathematik, andere Operationsfolge):

- *Intensität zuerst.* `state.py` bildet `Difference` ohne Intensität und multipliziert danach.
  Unterläuft dort ein Zwischenwert (kurze Last, Abstand nahe λ), wird er mit einer großen Intensität
  (bis 2/Lastlänge) vervielfacht. Mit q zuerst sind alle späteren Faktoren Abstände ≤ √2, und der
  Unterlaufanteil bleibt bei λ·(|q| + 8)·64.
- *Rampe als rise·(n/d)·nᵖ.* `rise/d` kann bei kurzen Lasten überlaufen; n/d ≤ 1 nicht.
- *Datenfehler der abgeschnittenen Intensität* als eigener Term (Punkt 8). Er fehlte bisher (9.1).
- *Rechte Seite normiert* (Punkt 2): F_exc liegt normiert in [1, 2). Alle Bezugsgrößen sind dann
  in normierten Einheiten von der Größenordnung 1. Damit kostet λ = 2⁻¹⁰²² statt η = 2⁻¹⁰⁷⁴ nichts
  Messbares und macht die Garantie unabhängig vom Unterlaufmodus.

## 6. Verifikation der adjungierten Lösungen

**Verwendete Aussage.** [Ru10] Satz 10.2: Ist R eine genäherte Inverse und ‖I − RA‖ < 1, so ist A
regulär und ‖A⁻¹b − x̃‖ ≤ ‖R(b − Ax̃)‖/(1 − ‖I − RA‖). SpanDraft nutzt die komponentenweise Form in
der mit v gewichteten ∞-Norm (L6):

```text
R    Zeilen = berechnete adjungierte Lösungen ẑ_i (Ŝᵀẑ_i ≈ e_i), also R ≈ S*⁻¹
C    = I − R S* (exakt, unbekannt)          M ≥ |C| eintragsweise, berechnet nach oben
e    = y* − ŷ = S*⁻¹ r* = R r* + C e        (exakte Identität, wegen S*⁻¹ = R + C S*⁻¹)
|e| ≤ β + M|e|,   β = |R| d,   |r*| ≤ d
v > 0, μ := max_i (M v)_i / v_i < 1   ⇒   |e_i| ≤ β_i + (M v)_i / (1 − μ)
```

Beweis der letzten Zeile: Sei t = max_i |e_i|/v_i (endlich, S* regulär nach V6). Mit β ≤ v folgt
|e| ≤ β + t·Mv ≤ v + tμv, also t ≤ 1 + tμ und t ≤ 1/(1 − μ). Damit |e_i| ≤ β_i + t(Mv)_i.

**M rigoros.** Zeile i von C ist e_iᵀ − ẑ_iᵀS* = s_iᵀ + ẑ_iᵀ(Ŝ − S*), mit dem exakten Residual
s_i = e_i − Ŝᵀẑ_i der *gespeicherten* Matrix. Also gilt |C_ij| ≤ s̄_ij + (|ẑ_i|ᵀδS)_j =: M_ij.
s̄_ij ist die Schranke aus Punkt 17 (berechnetes Residual plus Rundungsschranke), δS die aus
Punkt 3. Damit sind die Rundungsfehler der Verifikation selbst erfasst: das Residual über s̄, die
Datenrundung über δS und die Auswertung von Mv, μ und E über up/dn (L5).

**Kompensiertes Residual.** Nicht nötig. Der Rundungsanteil von s̄ hat dieselbe Struktur wie der
Datenanteil |ẑ|ᵀδS, γ̄(m+1)|Ŝ|ᵀ|ẑ| gegen γ̄(k+1)|Ŝ|ᵀ|ẑ|. Ein genaueres Residual verkleinert μ nur um
einen festen Faktor. Gemessen: Mit double-Residual und Neumann-Gewichten waren alle 2.293
verifizierten Zufallsmodelle und alle Referenzmodelle verifizierbar **[state-fpc-summary]**. Dot2
bleibt eine Option, braucht aber eigene Unterlaufterme, weil die fehlerfreien Transformationen
bei Unterlauf nicht fehlerfrei sind.

**Gewichte.** Jedes v > 0 ist zulässig; das Gewicht entscheidet nur, ob μ < 1 *nachweisbar* ist.
Vertrag: v⁽⁰⁾ = β + δ·D (D Nenner des Gates in skalierten Einheiten, δ = 2⁻²⁰⁰). Dann drei
Neumann-Schritte v⁽ᵗ⁺¹⁾ = up(β + Mv⁽ᵗ⁾ + δD). Verwendet wird der letzte Kandidat mit μ < 1. Jeder
Durchgang kostet O(n²) (Abschnitt 12). Das ist keine Heuristik zur Auslieferung: μ wird für das
gewählte v rigoros berechnet, und die Aussage gilt für jedes v.

**Funktionale.** Für c (Reaktion) mit adjungierter Lösung ẑ_c gilt
cᵀe = ẑ_cᵀr* + s_c*ᵀe, also |cᵀe| ≤ |ẑ_c|ᵀd + m_cᵀE mit m_c = s̄_c + δSᵀ|ẑ_c| und E aus K4.

**Inverse.** Zusätzlich folgt S*⁻¹ − R = Σ_{k≥1} CᵏR, also
|S*⁻¹ − R| ≤ M|R| + α_k·μ²/(1 − μ)·v (α_k = max_l |R_lk|/v_l). Das prüft Stufe C exakt.

**Drei Fälle.**

| Fall | Kennzeichen | Folge |
|---|---|---|
| mathematisch falsche Lösung | tatsächlicher Fehler > τ | nie geliefert: Ist die Lösung verifiziert, ist E ≥ Fehler, also Gate > τ. Ist sie nicht verifiziert, wird abgelehnt |
| numerisch brauchbar, aber nicht verifizierbar | μ ≥ 1 für alle Kandidaten v | `IllConditionedSystem`, Ursache `NotVerified`: SpanDraft sagt nicht, das Ergebnis sei falsch, sondern dass die zugesicherte Genauigkeit nicht nachgewiesen ist |
| verifiziert | μ < 1 | β-Korrektur E; Gate entscheidet |

Eine gewöhnlich gerundete Schätzung μ < 1 genügt nicht. Geprüft wird die nach oben gerundete
Schranke μ̄ < 1, und 1 − μ̄ wird nach unten gerundet.

**Gerichtete Rundung.** Die Verifikation braucht nach oben gerundete Auswertung aller
Schrankengrößen. .NET hat keine Rundungsmodus-Steuerung; L5 emuliert sie je Operation mit
`Math.BitIncrement`/`BitDecrement`. Intervallarithmetik ist nicht nötig. Alternativ ginge ein
Faktor (1 + γ̄) je Summe mit eigener Unterlaufbehandlung. Der Vertrag wählt L5, weil es je
Operation gilt und keine Längenannahmen braucht. **Diese Voraussetzung kommt in die Architektur**
(SOLVER_ARCHITEKTUR.md 6.3). Die bisherigen Faktoren (1 + γ(n + 1)) aus `refine_study.py` reichen
übrigens knapp nicht: (1 + γ(n+1))(1 − u)(1 − γ(n)) < 1 für n ≥ 2.

## 7. Bereichs- und Subnormalvertrag

Ziel: Kein angenommener Rechenpfad braucht subnormale Arithmetik für seine Garantie. Wo Subnormale
entstehen dürfen, deckt sie λ.

| Größe | darf 0 sein | muss normal sein | darf subnormal sein | Prüfung, Ursache |
|---|---|---|---|---|
| Eingabewerte (Koordinaten, L, E, I, EI, k, Lasten) | ja (außer L, EI) | jeder Wert ≠ 0 | nein | `NumericalRange` vor dem Aufbau |
| Balkenlänge L | nein | L ≥ 2⁻⁹⁶⁸ (λ·2^−e_L ≤ u) | nein | `NumericalRange` |
| Abstand verschiedener Stationen | nein | > λ in SI und skaliert | nein | `NumericalRange` |
| EI/EA skaliert, Koeffizienten und alle Zwischenwerte (Punkt 4, 5) | nein | > λ | nein | `NumericalRange` |
| Federsteifigkeit skaliert | ja (k = 0 nach DEV-19) | > λ, wenn ≠ 0 | nein | `NumericalRange` |
| normierte Anregungsprimitive (P, C, q) | ja | ja, wenn ≠ 0 | nein | `NumericalRange` |
| Länge einer Streckenlast (skaliert) | nein | ≥ 2⁻⁹⁶⁹ | nein | `NumericalRange` |
| Einträge von S nach Ruiz | strukturell | > λ | nein | `NumericalRange` |
| rechte Seite sb | ja | nein | ja (λ in δsb) | – |
| Pivot | – | > λ | nein | 0: `ZeroPivot`; sonst `NumericalRange` |
| Residual, Korrekturen, LU-Einträge | ja | nein | ja (kein Modell bzw. λ in δr) | – |
| gespeicherte ŷ, ẑ | ja | – | nein (Flush auf 0) | – |
| Schranken d, β, Mv, μ, E, Gate | d ≥ 2mλ > 0 | – | nein (L5 liefert nie Subnormale) | – |
| normierte Feldwerte, Reaktionen | ja | nein | ja (λ_eval; Flush vor Rückskalierung) | – |
| Auswertungsstelle x (`EvaluateAt`) | ja | nein | ja (λ_d) | keine Ablehnung nach der Annahme möglich und nötig |
| normierte Bezugsgrößen S_q | nein | ≥ etwa 0,25 durch die Normierung | – | – |
| SI-Bezugsgrößen S_q,SI | nein | ∈ [2⁻⁹⁶⁹, 2¹⁰⁰⁰] | nein | `NumericalRange` |
| Obergrenzen \|Q\| je Stück in SI | – | ≤ 2¹⁰⁰⁰ | – | `NumericalRange` |
| SI-Werte | ja | nein | ja (λ in β_SI) | – |

**Allgemeine Regel der Rückskalierung (statt Sonderfall Kleinstbalken).** Geliefert wird nur, wenn
jede SI-Bezugsgröße in [2⁻⁹⁶⁹, 2¹⁰⁰⁰] liegt und keine Stück-Obergrenze in SI überläuft. Dann gilt
λ/S_q,SI ≤ u, und die Rundung der Rückskalierung, auch mit FTZ, ist ein Term von β_SI. Einzelne
SI-Werte unter S_q dürfen subnormal sein, etwa w = 10⁻³²⁰ m neben einem Lager. Sie werden
geliefert; ihre Rückskalierungsrundung steht in β_SI, und das Maß bezieht sich auf S_q. Der
Kleinstbalken (L = 10⁻⁶⁰ m, w ≈ 10⁻³⁰⁰ … 10⁻³²¹ m) hat S_w,SI < 2⁻⁹⁶⁹ und wird mit `NumericalRange`
abgelehnt, bevor ein Wert entsteht **[state-fpc-chain-underflow]**.

**Folge der Plattformunabhängigkeit.** Die untere Grenze 2⁻⁹⁶⁹ ≈ 2·10⁻²⁹² statt „normal“ (2⁻¹⁰²²)
lehnt zusätzlich Modelle mit SI-Skalen in [2·10⁻³⁰⁸, 2·10⁻²⁹²] ab. Beispiele: U1 mit P = 10⁻³⁰⁰ N
und U4 mit w ≈ 10⁻³⁰⁰ m. Bei graduellem Unterlauf wären sie korrekt (Fehler ≤ 1,3·10⁻¹⁶). Mit FTZ
hätte U4 bei w ≈ 10⁻³¹⁰ m einen Fehler von 2,1·10⁻² **[state-fpc-chain-underflow]**. Für ein
Statikprogramm sind solche Skalen bedeutungslos; die Grenze ist der Preis der
FTZ/DAZ-Unabhängigkeit.

**x64 und ARM64.** x64: MXCSR.FTZ setzt subnormale Ergebnisse auf 0, MXCSR.DAZ liest subnormale
Operanden als 0 [Intel-SDM]. ARM64: FPCR.FZ wirkt auf Operanden und Ergebnisse [Arm-ARM]. .NET
setzt keines davon; native Bibliotheken im selben Prozess könnten es. Nach diesem Vertrag ändert
das höchstens Bits der Lösung, nicht die Garantie: Gespeicherte Operanden sind normal oder null,
jeder Ergebnisverlust ist durch λ gedeckt, Vergleiche betreffen nur normale oder null Werte.
FTZ/DAZ/FZ ist damit höchstens Plattformdiagnose (Startprüfung, Bericht), keine Voraussetzung.
Bitgleichheit über Plattformen (O12) setzt dagegen weiter denselben Modus voraus.

**Emulation in der Untersuchung.** Python kann FTZ nicht setzen. `fpc.py` emuliert FTZ und DAZ für
jede modellierte Operation (MODE `ftz`): Koeffizienten, Lastintegrale, Residuen, Auswertung und
Rückskalierung. LU, Lösen und Nachiteration brauchen kein Modell. Die Schrankenoperationen (L5)
sind für beide Modi gültig.

## 8. η, λ und SAFE1/SAFE2

| Klasse | Behandlung | mathematische Funktion |
|---|---|---|
| 1 Unzulässige Bereiche | Bereichsregeln, *vor* der Operation: `NumericalRange` | garantiert, dass Koeffizienten, Pivots, Divisoren, Bezugsgrößen und gespeicherte Operanden normal oder null sind |
| 2 Normale Operationen | γ-Modell (L1–L3) | relative Rundung |
| 3 Absolute Reste nahe dem Unterlauf | λ = 2⁻¹⁰²² je Operation, in normierten Einheiten (L4) | deckt subnormale oder geflushte Ergebnisse in rechter Seite, Residual, Lastintegralen, Auswertung, Rückskalierung |
| 4 Relative Safe-Minimum-Probleme | entfallen: Es gibt keine relative Größe mit beliebig kleinem Nenner. Divisoren sind e, Pivots, v_i ≥ δD_i, 1 − μ̄ ≥ u und Bezugsgrößen ≥ 0,25 bzw. ≥ 2⁻⁹⁶⁹ | – |

λ hat dieselbe Rolle wie SAFE1 = NZ·safmin in [LAPACK-RFS], einen absoluten Boden des
Residualfehlers, und denselben Wert je Operation. SAFE2, die Fallunterscheidung im Nenner des
BERR, gibt es nicht, weil BERR keine Entscheidungsgröße ist. η = 2⁻¹⁰⁷⁴ aus Runde 3/4 entfällt.
Es war nur unter graduellem Unterlauf gültig und deckte den Verlust der SI-Rückrechnung nicht ab.
Mit der Lastnormierung kostet der größere Wert nichts Messbares: Die Gates von Runde 4 und Runde 5
treffen auf 1.894 von 1.896 Modellen dieselbe Entscheidung, die zwei Abweichungen haben andere
Ursachen (Abschnitt 10) **[state-fpc-summary]**.

**Kerncheck „keine Unsicherheit verschwindet in d = 0“.** Erfüllt: Jede Komponente hat
d_i ≥ 2m_iλ > 0. Jede Auswertungsschranke enthält λ_eval > 0. Jede Schrankenoperation liefert
nach L5 nur 0, wenn das exakte Ergebnis sicher 0 ist. Kein β wird null, solange eine Rundung
möglich ist. Die Vorgänger-Befunde 3 (Punktlast 10⁻³²⁰ N) und 5 (Kleinstbalken) fallen jetzt in
Klasse 1.

## 9. Prüfung der Kette

### 9.1 Stufe A – einzelne Formeln

`state/fpc_stage_a.py`: jede Formel mit ihrer Schranke gegen exakte rationale Werte. Die Eingaben
sind strukturiert: exakte kleine Ganzzahlen und Dyadiken, Mantissen aus lauter Einsen, Werte
neben Zweierpotenzen, Gleichstände, 1-ulp-Paare, Sterbenz-Paare, Exponenten bis 2^±60 bzw. nahe
2⁻¹⁰²², Subnormal erzeugende Kombinationen, Auslöschung, kurze Abschnitte, Lasten mit
Nulldurchgang, kurze schwere Lasten. Geprüft je Modus graduell und FTZ/DAZ **[state-fpc-stage-a]**:

| Familie | Prüfungen je Modus | tatsächlicher Fehler > Schranke | größtes Fehler/Schranke |
|---|---|---|---|
| A1 Abstände | 20.000 | 0 | 0,33 |
| A2 Koeffizienten | 80.000 bzw. 67.004 (FTZ: 3.249 Bereichsablehnungen) | 0 | 0,62 |
| A3 Punktlastterme | 116.487 bzw. 116.522 | 0 | 0,64 |
| A4 Lastintegrale (Vertrag, über `CS.loadint`) | 15.240 | 0 | 0,38 |
| A6 Residual | 20.000 | 0 | 0,63 |
| A7 up/dn-Operationen | 60.000 | 0 | – |
| A8 Feldauswertung ohne Fortpflanzung | 11.896 | 0 | 0,32 |
| A9 SI-Rückskalierung | 20.000 | 0 | 0,99 |
| **A5 Lastintegrale mit der Schranke von `state.py`** | 13.280 | **208** | **9,4·10¹³** |

Die 208 Verletzungen von A5 liegen alle in den acht Modellen mit Linearlast durch null, die an
einer Station im Abstand 0 … 10⁻³ m vom Nulldurchgang abgeschnitten wird **[state-fpc-stage-a]**.
Ursache: q_a = v₁ + (v₂ − v₁)f hat dort einen absoluten Fehler u·|v|, also einen großen relativen,
und `state.py` hatte für die abgeschnittene Intensität keinen Datenterm. Beim Abstand 0 hat der
berechnete Wert sogar das falsche Vorzeichen. Auf Ausgabewerten war das in diesen Modellen nicht
sichtbar: Fehler/Schranke von Runde 4 ≤ 0,056 **[state-fpc-chain-constructed]**, weil der Term
absolut winzig ist (10⁻⁸¹ der Skala). Unterwegs fielen zwei Lücken des Untersuchungsmodells selbst
auf und wurden vor den hier berichteten Läufen behoben: der Abstandsterm λ_d bei FTZ und das
Ergebnis genau λ bei einer Skalierung (L4).

### 9.2 Stufen B–F – ganze Kette

`state/fpc_chain.py` auf 452 Läufen **[state-fpc-chain-*]**:

- gezielte Matrix: enge Lager, Federcluster steif und weich, graduierte Matrizen, an denen QR
  scheitert (`gemischt`), EI-Wechsel im Cluster, kurze Lasten mit Vorzeichenwechsel; 148 Modelle;
- 40 Modelle nahe τ;
- 89 Modelle mit um 10⁻⁴ und mit um 10⁻¹ gestörten LU-Faktoren;
- Unterlauffälle U1–U7 einschließlich Kleinstbalken und subnormaler Auswertungsstelle, je
  graduell und FTZ;
- konstruierte Fälle: Nulldurchgang, kurze schwere Lasten, fast gleichförmige Last, EI-Wechsel
  1 ulp neben einer Feder, Last im kurzen Abschnitt, 6-nm-Lagerpaar mit kleinem Wert neben großen
  Querkräften; je graduell und FTZ;
- die zwei Modelle aus Runde 4 ohne Verifikation.

| Stufe | Aussage | geprüft | Verletzungen |
|---|---|---|---|
| B | \|Ŝ − S*\| ≤ δS, \|ŝb − sb*\| ≤ δsb, \|r*\| ≤ d | 448 Läufe mit Lösung | 0 |
| C | \|I − R S*\| ≤ M eintragsweise; \|S*⁻¹ − R\| innerhalb der Einschließung | 400 Läufe (n ≤ 30) | 0 |
| D | \|ŷ − y*\| ≤ E; Reaktionen innerhalb ihrer Schranke | 448 | 0 |
| E | SI-Feldwerte an Stückenden, im Inneren und 1 ulp neben den Enden innerhalb β_SI; β̄ im Stück ≤ Majorante; LB unter der exakten Bernstein-Einschließung von min\|Q\| | 448 (Bernstein: 434) | 0 |
| F | gelieferte Modelle: Fehler im Vertragsmaß ≤ Gate; Bereichsfälle vor jeder Ausgabe abgelehnt | 331 geliefert, 18 `NumericalRange` | 0 |

Zusätzlich gleicht die exakte Lösung von (S*, sb*) an den Stationen in jedem Lauf genau der
mechanischen Referenz `transfer_ref.py`. Die Formeln dieses Dokuments sind also mechanisch richtig.
Das Verhältnis Fehler/Schranke erreicht 1 − 10⁻¹⁵, wo das gemessene Residual d dominiert (gestörte
LU, einzelne Zeilen). Dort ist β scharf und trotzdem nie überschritten.

Ablehnungen: Mit 10⁻¹ gestörter LU werden alle 89 Modelle abgelehnt, und jedes hätte geliefert einen
Fehler > τ gehabt. Mit 10⁻⁴ werden 4 abgelehnt, zwei davon mit Fehler > τ. Nahe τ werden 10 von 40
abgelehnt; der größte gelieferte Fehler ist 2,4·10⁻¹⁰ bei Gate ≤ 3,6·10⁻⁹.

### 9.3 Regression gegen Runde 4

`state/fpc_regress.py` auf jedem 5. Modell der Zufallsmengen von Runde 4 (Federn und gemischt, je
fünf Abstandsklassen) **[state-fpc-regress-*, state-fpc-summary]**:

- 2.295 exakt stabile Modelle: 2.291 geliefert, 2 wegen Gate abgelehnt (beide mit Fehler > τ),
  2 mit Nullpivot;
- kein Wert und keine Reaktion über der Vertragsschranke; größter gelieferter Fehler 2,5·10⁻¹³;
- dieselbe Entscheidung wie Runde 4 in 1.894 von 1.896 vergleichbaren Modellen; die zwei
  Abweichungen sind die früher nicht verifizierbaren Modelle (Abschnitt 10).

## 10. Die zwei nicht verifizierbaren Modelle

**[state-fpc-unverified]**

| Modell | μ Runde 4 (Dot2, Gewichte D bzw. β + 2⁻²⁰⁰D) | μ Vertrag v⁽⁰⁾ | μ nach Neumann-Schritten | Spektralradius von M | Ergebnis |
|---|---|---|---|---|---|
| s\|1e-11\|105 (Lager im Abstand 3·10⁻¹¹ m) | 1,2·10⁷ | 7,6·10⁷ | 1 − 1,3·10⁻⁸ | 4,5·10⁻¹⁵ | verifiziert, Gate 6,2·10⁻¹⁵ |
| s\|1e-11\|800 (starre Feder im Abstand 3·10⁻¹¹ m) | 1,7 | 0,017 | 0,017 | 1,0·10⁻¹⁴ | verifiziert, Gate 6,7·10⁻¹⁶ |

**Ursache:** μ wird bei einer Komponente erreicht (V am Balkenanfang bzw. V an Station 4), deren
β auf Unterlaufniveau liegt (10⁻²⁹⁶). Ihr Gewicht ist deshalb fast nur δ·D. Sie ist über einen
Eintrag M_ij ≈ 2·10⁻¹⁵ an θ gekoppelt, dessen Gewicht um 10²² größer ist. Das Verhältnis
(Mv)_i/v_i wird dadurch groß, obwohl M als Ganzes winzig ist. **Bewertung:** eine vermeidbare
Schwäche der Gewichtswahl in Runde 4, nicht fehlende Information. Der Spektralradius von M ist
≈ 10⁻¹⁴, die Ungleichung |e| ≤ β + M|e| trägt also eine scharfe Schranke. Ein Neumann-Schritt
hebt das Gewicht dieser Komponente auf (Mv)_i und macht μ < 1 nachweisbar. Bei s\|1e-11\|800 lag μ
in Runde 4 über 1 wegen der damaligen Skalen als Gewicht; die Vertragsskalen genügen schon ohne
Neumann-Schritt. Alle Kettenstufen sind für beide Modelle ohne Verletzung, Fehler 1,9·10⁻¹⁶ bzw. 0.

Mit 1 − μ = 1,3·10⁻⁸ vergrößert die Korrektur (Mv)/(1 − μ) alle Komponenten um bis zu 7,6·10⁷.
Hier bleibt das Gate trotzdem bei 6,2·10⁻¹⁵, weil (Mv)_j ≪ v_j außerhalb der einen Komponente.
Eine schärfere Form, die Teillösungsbedingung β + δD + Mv ≤ v ⇒ |e| ≤ v, käme ohne diesen Faktor
aus. Sie ist für C# als Option vermerkt, nicht nötig.

**Vertragsfall bleibt:** Wird μ < 1 auch nach den Neumann-Schritten nicht erreicht, gilt
„nicht verifizierbar → kein Ergebnis“ (A5/A8): `IllConditionedSystem` mit Ursache `NotVerified`.
SpanDraft behauptet dann nicht, das Ergebnis sei falsch, sondern dass die zugesicherte Genauigkeit
nicht nachgewiesen werden konnte. Das ist kein falsch positives Ergebnis im Sinne des Vertrags. In
den Läufen dieser Runde kam der Fall ungestört nicht mehr vor. Bei gestörter LU retten die
Neumann-Schritte 5 bzw. 1 Modell (84 → 89 bzw. 88 → 89 verifiziert). Alle 89 Modelle mit 10⁻¹
gestörter LU werden danach wegen Gate > τ abgelehnt **[state-fpc-summary]**.

## 11. Extremwerte: Vertragsvariante

Präzise Zusage:

- β und Gate decken den **Wert an einem gefundenen Kandidaten** x_c (Punkt 22, L7).
- Die **Position** hat eine eigene Fehlerproblematik: Der Positionsanteil |Q(x_c) − Q(x*)| ist nicht
  in β (SOLVER_ARCHITEKTUR.md 7.3).
- Die exakte Wurzelisolation fand auf 1.833 Modellen keinen fehlenden Kandidaten **[state-piece-*]**.
  Das ist eine **experimentell sehr stark validierte Kandidatenvollständigkeit**, keine
  **mathematisch zertifizierte**: `PolynomialRoots` hat Toleranzen und eine Clusterregel ohne
  Vollständigkeitsbeweis.

**Vorschlag: Variante B.** Keine mathematisch zertifizierte Vollständigkeitszusage.
`PolynomialRoots` bleibt stark validierter Produktalgorithmus; das exakte Oracle wird Testreferenz.
Begründung: Für M und V bietet das Stück-Gate bereits einen rigorosen Schutz vor großen
übersehenen Werten über S_q. Variante A (zertifizierter Fallback) würde gerichtet gerundete
Intervallarithmetik auf Polynomen oder exakte Arithmetik im Produktpfad verlangen. Das ist eine
neue Komponente für eine Lücke, die auf 1.833 Modellen nie aufgetreten ist. Die Entscheidung ist
eine Produktentscheidung und bleibt im Review offen.

## 12. Aufwand der Verifikation (O9)

Operationsmodell, keine C#-Messung **[state-o9-sizing]**. Konstanten aus **[state-cost]**:
n = 3,5·n_m, n_r = 0,54·n_m, Band p = 2, q = 4, 1,002 Nachiterationsschritte je adjungierter Lösung,
1 + 3 Verifikationsdurchgänge. Zeiten bei angenommenen 10⁹ Operationen je Sekunde:

| n_m | n | adjungierte Lösungen | Band, alles speichern | Band, neu lösen | dicht | Speicher Z und S̄ (dicht, je 8 B) | Speicher Band |
|---|---|---|---|---|---|---|---|
| 10 | 35 | 40 | 0,09 ms | 0,34 ms | 0,55 ms | 19 kB | 3 kB |
| 50 | 175 | 202 | 2,3 ms | 8,4 ms | 66 ms | 0,47 MB | 16 kB |
| 100 | 350 | 404 | 9,1 ms | 34 ms | 0,53 s | 1,9 MB | 32 kB |
| 250 | 875 | 1.010 | 57 ms | 0,21 s | 8,2 s | 12 MB | 80 kB |
| 500 | 1.750 | 2.020 | 0,23 s | 0,84 s | 66 s | 47 MB | 160 kB |
| 1.000 | 3.500 | 4.040 | 0,91 s | 3,4 s | 520 s | 190 MB | 320 kB |
| 5.000 | 17.500 | 20.200 | 23 s | 84 s | 18 h | 4,6 GB | 1,6 MB |

„Alles speichern“ hält die n adjungierten Lösungen der Zustände und ihre Residualschranken (2n²
Werte); jeder Verifikationsdurchgang ist dann O(n²). „Neu lösen“ speichert nichts und löst je
Durchgang alle n adjungierten Systeme erneut (Faktor ≈ 3,7). Blockweise liegt dazwischen: gleicher
Aufwand wie „neu lösen“, Speicher 2n·b. Die Primärlösung allein kostet bei n_m = 1.000 rund 0,5 ms.
Der Qualitätsvertrag ist der Engpass, und er ist O(n²) auch mit Band, weil die adjungierten
Lösungen voll besetzt sind.

## 13. Was offen bleibt

1. **Konformität der C#-Implementierung** mit den Folgen aus Abschnitt 5. Das ist eine Testpflicht,
   keine Herleitungsfrage. Werkzeug: dieselben Stufen A–F mit eingecheckten exakten Sollwerten.
   Zusätzlich je Formel ein Bit-Vergleich gegen `fpc.py` auf festen Eingaben, damit eine
   abweichende Reihenfolge auffällt.
2. **V2 und V5 auf x64 und ARM64** messen: keine implizite FMA-Kontraktion im Release-JIT,
   BitIncrement und ScaleB an den Grenzwerten.
3. **Formaler Beweis:** Die Beweise hier sind Papierbeweise; die Rundungszahlen sind mechanisch
   abgeleitet und gegen exakte Werte geprüft. Ein maschinell geprüfter Beweis (etwa Gappa oder Coq)
   ist nicht Teil dieser Runde.
4. **Axialanteil (O4):** gleiche Muster, aber weder in `fpc.py` umgesetzt noch geprüft.
5. **Extremwerte (O5):** Vollständigkeit der Kandidaten nach Variante B nicht zertifiziert;
   Positionsanteil nicht in β.
6. **Mechanische Formeln:** Die Identität der Formeln mit der Mechanik ist algebraisch (geschlossene
   Integrale) und an jeder Station gegen die unabhängige exakte Referenz geprüft (448 Läufe, keine
   Abweichung), aber nicht symbolisch bewiesen.

## Quellen dieser Runde (zusätzlich zu LITERATURE_AUDIT.md)

| Kürzel | Quelle | genutzt für |
|---|---|---|
| [IEEE19] | IEEE Standard for Floating-Point Arithmetic, IEEE Std 754-2019. DOI 10.1109/IEEESTD.2019.8766229 | V1, Rundung zur nächsten Zahl, Unterlauf und Tininess (L4) |
| [Intel-SDM] | Intel 64 and IA-32 Architectures Software Developer's Manual, Vol. 1, Abschnitte zu MXCSR (FTZ, DAZ) | Plattformsemantik in Abschnitt 7 |
| [Arm-ARM] | Arm Architecture Reference Manual for A-profile, FPCR.FZ | Plattformsemantik in Abschnitt 7 |
