# Literatur-Audit der numerischen Entscheidungen (Runde 4)

Stand: 10. November 2026. Arbeitsdokument zu
[`docs/SOLVER_ARCHITEKTUR.md`](../../../docs/SOLVER_ARCHITEKTUR.md), **nicht freigegeben**. Es gleicht
jede wesentliche numerische Entscheidung mit der etablierten Theorie zu Mehrfachschießen,
Randwertproblemen und linearer Algebra ab. Die Literatur liefert Algorithmen, Voraussetzungen,
Garantien und bekannte Fehlerbilder. Gezielte Experimente prüfen danach, ob diese Aussagen auf die
konkrete SpanDraft-Matrix zutreffen. Jede Zahl verweist auf ein Protokoll **[Name]** in `results/`.

Statusklassen (Spalte „Status“):

1. etablierter Ansatz direkt übernommen,
2. etablierter Ansatz an SpanDraft angepasst,
3. bewusste Abweichung mit Begründung und Test,
4. noch offen, mit Risiko und nächstem Nachweis.

## 1. Quellen

Alle DOIs wurden über Crossref aufgelöst. Die LAPACK-Aussagen stammen aus dem Quelltext der
Reference-LAPACK-Routinen (`SRC/dgerfs.f`, `dgerfsx.f`, `dla_gerfsx_extended.f`, `dgeequb.f`,
`dgbtrf.f`), nicht aus Sekundärliteratur.

| Kürzel | Referenz | Relevante Aussage | Voraussetzungen | Übertragbarkeit auf SpanDraft | Konsequenz |
|---|---|---|---|---|---|
| [Wr93] | S. J. Wright: A Collection of Problems for Which Gaussian Elimination with Partial Pivoting is Unstable. SIAM J. Sci. Comput. 14(1), 231–238, 1993. DOI 10.1137/0914013 | Für eine Klasse gut gestellter Zweipunkt-Randwertprobleme wird GEPP auf der Mehrfachschieß-Matrix instabil: Bandmatrix mit zusätzlichen Endspalten, ohne Zeilentausch exponentielles Wachstum in diesen Spalten | Randbedingungen koppeln beide Enden (Block B_b in der ersten Blockzeile, also Spalten außerhalb des Bandes); Übertragungsmatrizen, bei denen keine Vertauschung stattfindet | Nein: Alle Bedingungen sind lokal (separiert), es gibt keine Spalten außerhalb des Bandes; die Bandbreite ist strukturell (2,2) (Abschnitt 3.1) | Fehlerbild strukturell ausgeschlossen; lokale Kopplung wird Voraussetzung der Faktorisierungswahl (Invariante 2) |
| [Wr92] | S. J. Wright: Stable Parallel Algorithms for Two-Point Boundary Value Problems. SIAM J. Sci. Stat. Comput. 13(3), 742–764, 1992. DOI 10.1137/0913044 | Strukturierte orthogonale Faktorisierung für Mehrfachschieß- und Kollokationssysteme, stabil auch bei gekoppelten Randbedingungen, parallelisierbar | Blockstruktur eines BVP-Systems | Struktur ja; der Nutzen (gekoppelte Randbedingungen, Parallelität) entfällt | Nicht als eigene Variante umgesetzt; ihr numerischer Kern (orthogonale Transformationen) ist durch Householder-QR vertreten, der hier schlechter ist (Abschnitt 3.1) |
| [MS84] | R. M. M. Mattheij, G. W. M. Staarink: An Efficient Algorithm for Solving General Linear Two-Point BVP. SIAM J. Sci. Stat. Comput. 5(4), 745–763, 1984. DOI 10.1137/0905053 | Mehrfachschießen mit adaptiv gewählten Schießpunkten und stabiler Entkopplung für Probleme mit Grenzschichten (schnell wachsende und abklingende Moden) | Fundamentallösungen mit exponentiell wachsenden Anteilen | Nein: Euler-Bernoulli-Biegung und Längskraft haben nur polynomiale Fundamentallösungen | Keine adaptive Knotenwahl nötig; Bedingung für spätere Features (Bettung, Theorie II. Ordnung) |
| [Ma85] | R. M. M. Mattheij: Decoupling and Stability of Algorithms for Boundary Value Problems. SIAM Review 27(1), 1–44, 1985. DOI 10.1137/1027001 | Stabilität von Mehrfachschießen, Kollokation und Invariant Imbedding beruht auf Entkopplung wachsender und abklingender Moden; verknüpft mit der Dichotomie der ODE | Dichotomie der ODE, Behandlung der Randbedingungen | Dichotomie trivial (keine Exponentialmoden); Entkopplung ist hier die exakte Elimination der vorgeschriebenen Komponenten | Formulierung entspricht einem entkoppelten Mehrfachschießen ohne Exponentialmoden |
| [HM87] | F. R. de Hoog, R. M. M. Mattheij: On Dichotomy and Well Conditioning in BVP. SIAM J. Numer. Anal. 24(1), 89–105, 1987. DOI 10.1137/0724008 | Gute Kondition des BVP ist äquivalent zu einer Dichotomie mit moderaten Konstanten | lineares BVP | Ja, aber trivial erfüllt | Kondition entsteht bei SpanDraft aus Geometrie (nahe Lager, Federn), nicht aus Moden |
| [HM87b] | F. R. de Hoog, R. M. M. Mattheij: An algorithm for solving multi-point boundary value problems. Computing 38, 219–234, 1987. DOI 10.1007/BF02240097 | Mehrpunkt-Randwertprobleme mit Zwischenbedingungen im Mehrfachschießen | lineare Zwischenbedingungen | Ja: Lager, Gelenke, Federn sind Zwischenbedingungen | Stationsregeln entsprechen etablierten Zwischenbedingungen |
| [AMR95] | U. M. Ascher, R. M. M. Mattheij, R. D. Russell: Numerical Solution of Boundary Value Problems for Ordinary Differential Equations. SIAM Classics 13, 1995. DOI 10.1137/1.9781611971231 | Lehrbuchdarstellung von Mehrfachschießen, Kompaktifizierung, Entkopplung, Mehrpunktproblemen | – | Rahmen | Begriffe und Einordnung |
| [Va76] | J. M. Varah: Alternate Row and Column Elimination for Solving Certain Linear Systems. SIAM J. Numer. Anal. 13(1), 71–75, 1976. DOI 10.1137/0713008 | Stabile Elimination fast blockdiagonaler Systeme mit separierten Randbedingungen ohne zusätzliches Auffüllen | separierte Randbedingungen, Blockstruktur | Struktur ja | Nicht nötig: GEPP auf dem Band ist hier stabil (Abschnitt 3.1); bleibt Rückfalloption |
| [BW80] | C. de Boor, R. Weiss: SOLVEBLOK: A Package for Solving Almost Block Diagonal Linear Systems. ACM TOMS 6(1), 80–87, 1980. DOI 10.1145/355873.355880 | Fast blockdiagonale Löser für Kollokation/Schießen | separierte Bedingungen | Struktur ja | wie [Va76] |
| [Bo75] | Z. Bohte: Bounds for Rounding Errors in the Gaussian Elimination for Band Systems. IMA J. Appl. Math. 16(2), 133–142, 1975. DOI 10.1093/imamat/16.2.133 | Das maximale Pivotwachstum von GEPP auf Bandmatrizen ist unabhängig von der Ordnung n und nur durch die Bandbreite bestimmt | Bandmatrix | Ja: Bandbreite strukturell (2,2) | Wachstum durch eine Konstante unabhängig von der Stationszahl beschränkt; gemessen ρ ≤ 1,97 |
| [Fo94] | L. V. Foster: Gaussian Elimination with Partial Pivoting Can Fail in Practice. SIAM J. Matrix Anal. Appl. 15(4), 1354–1362, 1994. DOI 10.1137/S0895479892239755 | Exponentielles Wachstum in praktischen Problemen (Volterra-Integralgleichungen) | dichte untere Dreiecksanteile | Nein (Band) | – |
| [HH89] | N. J. Higham, D. J. Higham: Large Growth Factors in Gaussian Elimination with Pivoting. SIAM J. Matrix Anal. Appl. 10(2), 155–164, 1989. DOI 10.1137/0610012 | Konstruktion großer Wachstumsfaktoren | allgemeine Matrizen | Nein (Band) | – |
| [Sk80] | R. D. Skeel: Iterative Refinement Implies Numerical Stability for Gaussian Elimination. Math. Comp. 35(151), 817–832, 1980. DOI 10.1090/S0025-5718-1980-0572859-4 | Ein Nachiterationsschritt mit Residual in Arbeitsgenauigkeit macht GEPP komponentenweise rückwärtsstabil | Bedingung an cond(A⁻¹)·σ(A,x)·u (nicht zu groß) | Bedingung nicht a priori nachgewiesen; gemessen konvergiert die Nachiteration auch bei κ∞ bis 10³³, weil cond(S, y) ≤ 80 | Mindestens ein Schritt in der Primärlösung |
| [Hi91] | N. J. Higham: Iterative Refinement Enhances the Stability of QR Factorization Methods for Solving Linear Equations. BIT 31, 447–468, 1991. DOI 10.1007/BF01933262 | Auch QR wird durch Nachiteration komponentenweise stabil, unter Konditionsvoraussetzungen | normweise Kondition nicht zu groß | Nein in den kritischen Fällen: u·κ∞ bis 10¹⁷ | QR ist für diese Matrixklasse keine robustere Referenz (Abschnitt 3.1) |
| [Hi97] | N. J. Higham: Iterative Refinement for Linear Systems and LAPACK. IMA J. Numer. Anal. 17(4), 495–509, 1997. DOI 10.1093/imanum/17.4.495 | Analyse der LAPACK-Nachiteration in Arbeitsgenauigkeit, Ziel Rückwärtsfehler | – | Ja | Einordnung der Primärregel |
| [Hi02] | N. J. Higham: Accuracy and Stability of Numerical Algorithms, 2. Aufl., SIAM 2002. DOI 10.1137/1.9780898718027 | Rundungsmodell γ_k, komponentenweise Störungstheorie, Nachiteration, Schätzer | – | Ja | Notation γ(k), Rundungszählung |
| [ADD89] | M. Arioli, J. W. Demmel, I. S. Duff: Solving Sparse Linear Systems with Sparse Backward Error. SIAM J. Matrix Anal. Appl. 10(2), 165–190, 1989. DOI 10.1137/0610013 | Komponentenweiser Rückwärtsfehler als Abbruchkriterium der Nachiteration, Fehlerschranke ‖ \|A⁻¹\|(\|r\| + (n+1)ε(\|A\|\|x\| + \|b\|)) ‖, Behandlung kleiner Nenner | Residual mit bekannter Rundungsschranke | Ja | Grundlage von d; Abbruchregel der adjungierten Lösungen (Regel B) |
| [LAPACK-RFS] | Reference LAPACK `dgerfs.f` (Quelltext, aktuelle Version) | BERR = maxᵢ \|r\|ᵢ/(\|A\|\|x\| + \|b\|)ᵢ mit SAFE1 = NZ·safmin, SAFE2 = SAFE1/ε; Nachiteration solange BERR > ε, BERR ≤ LSTRES/2, höchstens ITMAX = 5; FERR = ‖\|A⁻¹\|(\|r\| + NZ·ε(\|A\|\|x\| + \|b\|) + SAFE1)‖/‖x‖ mit Hager/Higham-Schätzer `dlacn2` | Normweise Schätzung von \|A⁻¹\|; safmin deckt abrupten Unterlauf | Ja, mit Anpassungen (Abschnitt 3.2) | Regel B für adjungierte Lösungen; FERR-Form = d; SAFE1 ↔ η |
| [LAWN165] | J. Demmel, Y. Hida, W. Kahan, X. S. Li, S. Mukherjee, E. J. Riedy: Error Bounds from Extra-Precise Iterative Refinement. ACM TOMS 32(2), 325–351, 2006. DOI 10.1145/1141885.1141894; LAPACK Working Note 165 | Fehlerschätzung aus der geometrischen Abnahme der Korrekturen: ‖e‖ ≈ ‖dx‖/(1 − ρ_max); Zustände arbeitend/konvergiert/kein Fortschritt/instabil, ρ_thresh = 0,5; „vertrauenswürdig“, wenn die geschätzte Kondition < 1/(max(10, √n)·ε) | **Residual in erhöhter Genauigkeit**; Analyse ohne Über- und Unterlauf; keine exakt verschwindenden Lösungskomponenten (komponentenweise) | Die Abbruchlogik ja, die Fehlerschätzung nein: SpanDraft rechnet das Residual in double, und die Schätzung lag in 64 % der Modelle unter dem tatsächlichen Fehler **[state-refine-*]** | Kontraktionslogik als Primärregel (angepasst); keine Fehlerschätzung aus Korrekturen |
| [LAPACK-RFSX] | Reference LAPACK `dgerfsx.f`, `dla_gerfsx_extended.f` | Trust-Flag: normweise bzw. komponentenweise reziproke Kondition von S·A bzw. S·A·diag(x) (`dla_gercond`) gegen √n·ε; Fehlerschranke auf 1 gesetzt, wenn untrustworthy; Schranke ≥ max(10, √n)·ε | wie [LAWN165] | Die geschätzte Größe ist die Kondition der *Lösung* in einer Norm, nicht die Genauigkeit der adjungierten Lösung eines Funktionals; sie setzt erhöhte Residualgenauigkeit voraus | Schwelle nicht übernommen; ersetzt durch Verifikation nach [Ru10] (Abschnitt 3.3) |
| [Ru10] | S. M. Rump: Verification Methods: Rigorous Results Using Floating-Point Arithmetic. Acta Numerica 19, 287–449, 2010. DOI 10.1017/S096249291000005X | Satz 10.2: Ist ‖I − RA‖ < 1 in einer Matrixnorm, so ist A regulär und ‖A⁻¹b − x̃‖ ≤ ‖R(b − Ax̃)‖/(1 − ‖I − RA‖); Satz 10.6 (Krawczyk/Rump) komponentenweise | Rigorose Auswertung (gerichtete Rundung oder Schranken) der Größen | Ja, mit den berechneten adjungierten Lösungen als Zeilen von R und der gewichteten ∞-Norm | Trust-Prüfung μ < 1 für β (Abschnitt 3.3) |
| [OP64] | W. Oettli, W. Prager: Compatibility of Approximate Solution of Linear Equations with Given Error Bounds for Coefficients and Right-Hand Sides. Numer. Math. 6, 405–409, 1964. DOI 10.1007/BF01386090 | Komponentenweiser Rückwärtsfehler ω = maxᵢ \|r\|ᵢ/(E\|x\| + f)ᵢ | – | Ja | ω als Diagnose |
| [Ha84] | W. W. Hager: Condition Estimates. SIAM J. Sci. Stat. Comput. 5(2), 311–316, 1984. DOI 10.1137/0905023 | Schätzer für ‖A⁻¹‖₁ mit wenigen Lösungen | normweise | Nein: komponentenweiser Vertrag | Nicht übernommen (Runde 2) |
| [GS02] | M. B. Giles, E. Süli: Adjoint Methods for PDEs: A Posteriori Error Analysis and Postprocessing by Duality. Acta Numerica 11, 145–236, 2002. DOI 10.1017/S096249290200003X | Fehler eines Funktionals = adjungierte Lösung gewichtet mit dem Residual (dual gewichtetes Residual) | lineares Funktional, exakte Adjungierte | Ja, für cᵀx | β_c = \|z\|ᵀd ist die betragsweise Form; Fehler der Adjungierten → [Ru10] |
| [ORO05] | T. Ogita, S. M. Rump, S. Oishi: Accurate Sum and Dot Product. SIAM J. Sci. Comput. 26(6), 1955–1988, 2005. DOI 10.1137/030601818 | Kompensiertes Skalarprodukt Dot2 | IEEE-Arithmetik | Ja | Untersuchte Residualvariante (Ablation, kein Gewinn) |
| [De84] | J. W. Demmel: Underflow and the Reliability of Numerical Software. SIAM J. Sci. Stat. Comput. 5(4), 887–919, 1984. DOI 10.1137/0905062 | Graduelles Unterlaufen erweitert die Fehleranalyse um einen additiven Term und erleichtert zuverlässige Software | IEEE gradual underflow | Ja, falls FTZ/DAZ aus | η-Modell; Anforderung „graduelles Unterlaufen aktiv“ |
| [Ba68] | E. H. Bareiss: Sylvester’s Identity and Multistep Integer-Preserving Gaussian Elimination. Math. Comp. 22, 565–578, 1968. DOI 10.1090/S0025-5718-1968-0226829-0 | Bruchfreie Elimination für exakten Rang ganzzahliger Matrizen | ganzzahlige bzw. rationale Daten | Ja (doubles sind rational) | Exakter Mechanismusrang (E9) |
| [Ru01] | D. Ruiz: A Scaling Algorithm to Equilibrate Both Rows and Columns Norms in Matrices. Rutherford Appleton Laboratory, RAL-TR-2001-034, 2001 | Iterative Zeilen-/Spaltenskalierung auf ∞-Norm 1, lineare Konvergenz (Rate 1/2) | keine Null-Zeilen/-Spalten | Ja | E8, mit Zweierpotenzen |
| [KRU14] | P. A. Knight, D. Ruiz, B. Uçar: A Symmetry Preserving Algorithm for Matrix Scaling. SIAM J. Matrix Anal. Appl. 35(3), 931–955, 2014. DOI 10.1137/110825753 | Konvergenzanalyse und Varianten des Ruiz-Verfahrens | – | Ja | Einordnung |
| [LAPACK-EQUB] | Reference LAPACK `dgeequb.f` | Skalierungsfaktoren als Potenzen der Basis; „Barring over- and underflow, scaling by these factors introduces no additional rounding errors“ | kein Über-/Unterlauf | Ja | E8, Zweierpotenzen (direkt übernommen) |
| [CS66] | G. T. Cargo, O. Shisha: The Bernstein Form of a Polynomial. J. Res. Nat. Bur. Standards 70B, 79–81, 1966 | min bᵢ ≤ p(t) ≤ max bᵢ auf [0,1]; Endkoeffizienten gleich den Endwerten | exakte Bernstein-Koeffizienten | Ja, als Oracle in exakter Arithmetik | Unabhängige Prüfung des Stück-Gates |
| [FR87] | R. T. Farouki, V. T. Rajan: On the Numerical Condition of Polynomials in Bernstein Form. Comput. Aided Geom. Design 4(3), 191–216, 1987. DOI 10.1016/0167-8396(87)90012-4 | Bernstein-Form ist auf [0,1] besser konditioniert als die Potenzform | – | Ja | Für ein Gleitkomma-Produktverfahren nötig; hier nicht gebraucht |
| [Co67] | G. E. Collins: Subresultants and Reduced Polynomial Remainder Sequences. J. ACM 14(1), 128–142, 1967. DOI 10.1145/321371.321381 | Exakte Restfolgen über ganzen Zahlen | – | Ja | Grundlage der Sturm-Folgen über Q |
| [Yu76] | D. Y. Y. Yun: On Square-Free Decomposition Algorithms. SYMSAC 1976, 26–35. DOI 10.1145/800205.806320 | Quadratfreie Zerlegung f = Π aᵢⁱ über ggT-Folgen | Charakteristik 0 | Ja | Vielfachheiten im Oracle |
| [CA76] | G. E. Collins, A. G. Akritas: Polynomial Real Root Isolation Using Descartes’ Rule of Signs. SYMSAC 1976, 272–275. DOI 10.1145/800205.806346 | Exakte Isolation reeller Wurzeln | quadratfreies Polynom | Ja | Alternative zu Sturm; sympy nutzt ein verwandtes Verfahren (Gegenprüfung) |
| [RZ04] | F. Rouillier, P. Zimmermann: Efficient Isolation of Polynomial’s Real Roots. J. Comput. Appl. Math. 162(1), 33–50, 2004. DOI 10.1016/j.cam.2003.08.015 | Effiziente exakte Isolation | – | Ja | Einordnung |

## 2. Arbeitsmatrix

| SpanDraft-Entscheidung | Problem | etablierter Ansatz | Quelle | Voraussetzungen | trifft auf SpanDraft zu? | Abweichung | Prüfung und Ergebnis | Status |
|---|---|---|---|---|---|---|---|---|
| E1 Zustandsformulierung / Mehrfachschießen | Randwertproblem mit vielen Zwischenbedingungen ohne Auslöschung | Mehrfachschießen, Mehrpunkt-BVP | [AMR95], [HM87b], [Ma85] | Stabilität über Entkopplung wachsender/abklingender Moden | Ja; Fundamentallösungen sind Polynome, Übertragungsmatrizen exakt obere Dreiecksmatrizen mit Einsen auf der Diagonale; keine Exponentialmoden | Keine Entkopplungs- oder Riccati-Transformation, weil keine Moden zu trennen sind | Strukturbeweis (3.1); Ablation früherer Runden | 2 |
| E2 mechanische vs. Ereignisstationen | Wo liegen Schießpunkte? | adaptive Schießpunkte nach Wachstum der Fundamentallösung | [MS84] | exponentielles Wachstum zwischen Knoten | Nein (polynomial) | Knoten nur an mechanischen Stationen; Lasten über geschlossene Integrale | Invariante 1 (226/226 bitgleich) **[state-invariance]** | 3 |
| E3 Elimination vorgeschriebener Komponenten | separierte Randbedingungen | Einbau separierter Randbedingungen durch Elimination bzw. Kompaktifizierung in fast blockdiagonalen Lösern | [Va76], [BW80], [AMR95] | separierte Bedingungen | Ja, alle Bedingungen sind lokal | Elimination ist exakt (bekannter Wert 0), ohne Rundung | Ablation: ohne Elimination bis O(1) Fehler **[state-ablation]**; Bandstruktur bleibt (2,2) | 2 |
| E4/E5 Stations- und Sprungbedingungen, Reaktionen als Sprung | Zwischenbedingungen, Reaktionen ohne Auslöschung | Zwischenbedingungen im Mehrpunkt-BVP | [HM87b] | lineare Bedingungen | Ja | Reaktionen keine Unbekannten (SpanDraft-spezifisch) | Ablation: Reaktionen als Unbekannte bis 1,6·10⁹ **[state-ablation]**; Federidentität (Runde 3) | 2 / 3 |
| E9 exakter Mechanismusrang | Mechanismus ohne Toleranz | bruchfreie Elimination | [Ba68] | exakte Daten | Ja | – | 12.500/12.500 übereinstimmend (Runde 1) | 1 |
| E8 Entdimensionierung | Zahlbereich, Einheitenunabhängigkeit | physikalische Bezugsgrößen; Skalierung mit Basispotenzen ist rundungsfrei | [LAPACK-EQUB], [LAWN165] §1 | kein Über-/Unterlauf | Ja | Bezugsgrößen nur aus Geometrie und Steifigkeit | bitgleiche Zweierpotenz-Invarianz 307/307 **[state-invariance]**; Unterlauf der SI-Rückrechnung gefunden (3.4) | 2 |
| E8 Zweierpotenz-Skalierung | rundungsfreie Skalierung | Faktoren als Basispotenzen | [LAPACK-EQUB] | kein Über-/Unterlauf | Ja; Unterlauf durch Skalierung in 64 gezielten Fällen nie aufgetreten **[state-underflow]** | – | – | 1 |
| E8 Ruiz-Äquilibrierung | Pivotqualität bei ungleich skalierten Zeilen und Spalten | Ruiz-Iteration | [Ru01], [KRU14] | – | Ja | 8 Iterationen, Faktoren gerundet auf Zweierpotenzen | Ablation: schützt Einzelfälle **[state-ablation]**. Grenze: Gradierung *innerhalb* einer Spalte bleibt (bis 10⁻³⁴), normweise κ∞ bis 10³³ **[state-cond-check]** | 2 |
| 5.2 Faktorisierung GEPP | stabile Lösung des Mehrfachschieß-Systems | GEPP; bei gekoppelten Randbedingungen strukturierte QR oder Spaltenelimination | [Wr93], [Wr92], [Va76], [Bo75] | Wrights Fehlerbild braucht Spalten außerhalb des Bandes | Nein, Band (2,2) für jedes Modell (3.1) | – | 2.956 Modelle (gezielt, Zufall, Stressfamilien bis 141 Stationen, adversarielle Suche): ρ ≤ 1,97; von 2.829 verglichenen keine LU-Lösung mehr als 10× schlechter als QR **[state-factor-summary]** | 1 |
| QR als Referenz | robuste Alternative | Householder-QR (+ Nachiteration) | [Hi91] | u·κ∞ ≪ 1 | Nein: κ∞ bis 10³³ bei cond(S, y) ≤ 80 | – | QR liefert in 29 Modellen O(1)-Fehler, GEPP 10⁻¹⁶; β erkennt alle **[state-factor-summary, state-cond-check]** | – (verworfen) |
| 5.2 Nachiteration primär | Rückwärtsstabilität, Abbruch | ≥ 1 Schritt in Arbeitsgenauigkeit [Sk80]; Abbruch nach Kontraktion ρ_thresh = 1/2 und \|dx\| ≤ ε\|x\| [LAWN165]; BERR-Abbruch [LAPACK-RFS] | [Sk80], [LAWN165], [LAPACK-RFS] | Fehlerschätzung aus Kontraktion nur mit erhöhter Residualgenauigkeit | Abbruchlogik ja, Fehlerschätzung nein | Pflichtschritt; keine Fehlerschätzung aus Korrekturen (β übernimmt das) | Regeln A–D treffen identische Entscheidungen; Regel B (BERR) lässt β ohne Reserve (Gate/Fehler bis 1,1) **[state-refine-*]** | 2 |
| 6.3 Nachiteration adjungiert | Genauigkeit von z | BERR-Regel [LAPACK-RFS] oder Kontraktion [LAWN165] | wie oben | – | Ja | β-Änderung > 1/16 statt BERR oder Kontraktion | gleiche Entscheidungen; mit Verifikation bei 10 % gestörter LU 23 statt 69 (B) nicht verifiziert, Kosten 1,002 gegen 0,973 Schritte; C und D teurer **[state-refine-summary]** | 3 |
| 5.2 Residualberechnung | Genauigkeit des Residuals | Arbeitsgenauigkeit für Rückwärtsstabilität [Sk80], [Hi97]; erhöhte Genauigkeit für Vorwärtsgenauigkeit [LAWN165] | wie links | β nutzt das gemessene Residual mit Rundungsschranke | Arbeitsgenauigkeit genügt dem Vertrag; erhöhte Genauigkeit nur für weniger Pessimismus | Ablation: kompensiert/exakt ohne Gewinn **[state-ablation]** | 2 |
| 6.3 komponentenweiser Indikator β | Fehler jeder Größe | \|A⁻¹\|(\|r\| + δr + …) [ADD89], FERR [LAPACK-RFS] | [ADD89], [OP64] | Rundungsschranke des Residuals; \|A⁻¹\| exakt oder geschätzt | Ja | \|A⁻¹\| zeilenweise exakt über adjungierte Lösungen statt Schätzer; Datenrundung mit hergeleiteten Rundungszahlen statt NZ·ε | O2 (Rundungszahlen) bleibt | 2 |
| 6.3 adjungierte Funktionale cᵀx | Reaktionen, korrelierte Größen | dual gewichtetes Residual [GS02]; Verifikation über genäherte Inverse [Ru10] | [GS02], [Ru10] | genaue Adjungierte bzw. ‖I − RA‖ < 1 | Ja | Gewichtete ∞-Norm mit v = β + 2⁻²⁰⁰·D; Residual der Adjungierten mit Dot2 | verifiziert: nie Fehler > β, auch bei 10 % gestörter LU; 2 von 2.811 ungestörten Modellen nicht verifizierbar **[state-refine-summary]** | 2 |
| O15/O16 Vertrauen in β | Wann darf β vertraut werden? | Trust über Konditionsschätzung [LAPACK-RFSX]; Verifikation [Ru10] | [LAPACK-RFSX], [Ru10] | [LAPACK-RFSX]: erhöhte Residualgenauigkeit, Lösungskondition; [Ru10]: Schranken der Residuen | [LAPACK-RFSX] nein, [Ru10] ja | Schwellenwert √n·ε nicht übernommen; μ < 1 als Bedingung | O15, O16 geschlossen (3.3) | 2 |
| 6.3 Unterlauf η | absolute Fehler bei Unterlauf | SAFE1 = NZ·safmin im Residual-/Fehlerterm [LAPACK-RFS]; graduelles Unterlaufen [De84] | [LAPACK-RFS], [De84] | safmin deckt auch abruptes Unterlaufen; η = 2⁻¹⁰⁷⁴ nur graduelles | Ja, bei aktivem graduellem Unterlaufen | η je Operation statt einmal NZ·safmin; zusätzlich für Aufbau und Auswertung | 64 gezielte Fälle: Lücke bei unterlaufender SI-Rückrechnung, sonst gedeckt **[state-underflow]** (3.4) | 2 |
| 7.3 Stück-Gate (Majorante, Untergrenze LB) | β und \|Q\| im ganzen Stück | Bereichseinschließung über Bernstein-Koeffizienten | [CS66], [FR87] | exakte oder gerichtet gerundete Koeffizienten | Ja, als Oracle | Produkt nutzt die geschlossene Untergrenze (Variante C), Bernstein nur als Oracle | 7.894 Stücke: LB ≤ min\|p̂\| zertifiziert ohne Ausnahme, LB ≤ min\|p\| exakt ohne Ausnahme, Bernstein-Gate gleich **[state-piece-*]** | 3 |
| 8 Extremwerte, `PolynomialRoots` | vollständige Kandidaten | exakte Isolation (Sturm/Descartes, quadratfreie Zerlegung) | [Co67], [Yu76], [CA76], [RZ04] | exakte Koeffizienten | Ja, als Oracle | Produkt bleibt Gleitkomma-Verfahren mit Toleranzen; Zusage um den Positionsanteil korrigiert | 1.833 Modelle: keine übersehenen Kandidaten, Extremwertfehler ≤ 3,1·10⁻¹⁴, Isolation stimmt mit sympy überein **[state-piece-*]** | 2 |
| Kondition, Hager/Higham-Schätzer | Normweise Grenze | Konditionsschätzer | [Ha84] | normweiser Vertrag | Nein | nicht verwendet | Runde 2 | 3 |

## 3. Ergebnisse der gezielten Prüfungen

### 3.1 Faktorisierung (Phase 1)

**Struktur, hergeleitet.** Unbekannte und Zeilen sind nach Stationen geordnet, innerhalb einer
Station nach Komponente (w, θ, M, V). Vorgeschriebene Komponenten sind eliminiert, entfallende
Bedingungen ausgelassen. Sei uᵢ die Zahl der Unbekannten und rᵢ die der Zeilen an Station i.

- Innere Station: Zeilen w immer, θ außer am Gelenk, M außer bei starrer Drehbehinderung, V außer
  bei starrer Wegbehinderung. Unbekannte: w außer bei starrer Wegbehinderung, θ außer bei starrer
  Drehbehinderung, M außer am Gelenk, V immer. Also uᵢ = rᵢ.
- Station 0: nur die Zeilen M und V, also u₀ − r₀ = 2. Station N: zwei Zeilen mehr als
  Unbekannte, da M = V = 0 rechts vom Ende eliminiert sind.

Damit beginnt der Zeilenblock von Station i ≥ 1 genau zwei Positionen vor ihrem Spaltenblock.
Die Übertragung ist eine obere Dreiecksmatrix in (w, θ, M, V): Die Zeile der Komponente q
enthält Unbekannte der Station i − 1 mit Komponente ≥ q, die eigene Komponente q und über
Federgesetze nur Komponenten < q der Station i. Abzählen ergibt untere Bandbreite
≤ 2 − (vorgeschriebene Komponenten ≥ q an Station i − 1) ≤ 2 und obere Bandbreite
≤ 2 − [starre Wegbehinderung] − [starre Drehbehinderung, q ∈ {θ, M}] ≤ 2. Das gilt für jedes
Modell, unabhängig von Gelenken, Federn, Abschnittswechseln und Teilpunkten (Teilpunkte sind
Stationen mit Einheitsübertragung). Gemessen auf 2.956 Modellen: untere/obere Bandbreite genau
2/2, jede Zeile koppelt höchstens zwei benachbarte Stationen **[state-factor-summary]**.

**Folgen aus der Literatur.**

- Wrights Fehlerbild [Wr93] braucht Spalten außerhalb des Bandes (gekoppelte Randbedingungen).
  Die gibt es nicht und kann es ohne eine Bedingung zwischen nicht benachbarten Stationen nicht
  geben.
- Für Bandmatrizen hängt das Pivotwachstum von GEPP nur von der Bandbreite ab, nicht von n [Bo75].
  Mit Teilpivotisierung bleiben die Multiplikatoren in der unteren Bandbreite 2, U hat die obere
  Bandbreite ≤ 4.
- Exponentialmoden, gegen die Entkopplung und adaptive Schießpunkte [MS84], [Ma85] schützen, gibt
  es bei Euler-Bernoulli-Biegung und Längskraft nicht.

**Gemessen** **[state-factor-summary]**:

| Menge | Modelle | größtes n | ρ max | Multiplikator-Abstand | Band von U | LU-Fehler > 10× QR-Fehler (> 10⁻¹⁴) | QR-Fehler > 10× LU-Fehler (> 10⁻¹⁴) |
|---|---|---|---|---|---|---|---|
| gezielt | 840 | 36 | 1,97 | 2 | 4 | 0 | 18 |
| Zufall (jedes 5.) | 1.898 | 40 | 1,64 | 2 | 4 | 0 | 10 |
| Stressfamilien bis 10 Felder | 75 | 125 | 1,25 | 2 | 4 | 0 | 0 |
| Stressfamilien bis 40 Felder, nur Struktur und Wachstum | 125 | 500 | 1,25 | 2 | 4 | – | – |
| adversarielle Suche nach maximalem ρ | 18 | 140 | 1,67 | 2 | 4 | 0 | 1 |

Die adversarielle Suche verschiebt über 300 Schritte je Startwert Positionen (bis auf 10⁻¹² L),
Halterungsarten, Steifigkeiten (10⁻¹² … 10¹⁵) und EI-Sprünge (bis 10⁻⁸) und behält jede Änderung,
die ρ nicht verkleinert. Sie kommt nicht über ρ = 1,67 hinaus.

**QR ist hier nicht robuster.** Householder-QR mit derselben Nachiteration und demselben β
liefert in 29 von 2.829 verglichenen Modellen Fehler O(1), wo GEPP 10⁻¹⁶ erreicht. β erkennt jeden dieser Fälle (Gate
bis 5·10⁹, Status abgelehnt), kein QR-Ergebnis mit Fehler > τ wurde geliefert. Ursache
**[state-cond-check]**: Die Matrizen sind trotz Äquilibrierung stark graduiert (kleinster zu
größtem Eintrag einer Spalte bis 2·10⁻³⁴), normweise κ∞ = 10¹⁷ … 10³³, die komponentenweise
Kondition an der Lösung cond(S, y) aber nur 11 … 77. QR ist normweise rückwärtsstabil, die
Nachiteration konvergiert bei u·κ∞ ≫ 1 nicht ([Hi91] setzt das voraus). GEPP mit Nachiteration
erreicht dagegen komponentenweise Rückwärtsstabilität [Sk80]; seine formale Bedingung ist nicht
a priori nachgewiesen, β kontrolliert den Erfolg a posteriori. Eine strukturierte orthogonale
Faktorisierung [Wr92] beruht auf denselben orthogonalen Transformationen und wurde deshalb nicht
zusätzlich umgesetzt.

**Ergebnis: GEPP bestätigt**, mit struktureller Begründung statt nur Stichproben. Neu ist die
Voraussetzung: Die Wahl von GEPP setzt lokale Kopplung voraus (Invariante 2). Eine spätere
Bedingung zwischen nicht benachbarten Stationen (etwa eine Koppelstange) oder Exponentialmoden
(elastische Bettung, Theorie II. Ordnung) heben diese Begründung auf und verlangen eine neue
Prüfung.

### 3.2 Nachiteration (Phase 2.1)

Verglichen auf 2.811 Modellen (840 gezielt, 1.896 Zufall, 75 nahe τ), jedes 5. Modell und alle
Modelle nahe τ zusätzlich mit relativ um 10⁻⁴ bzw. 10⁻¹ gestörten LU-Faktoren **[state-refine-*,
state-refine-summary]**. Regeln:

- A: SpanDraft (primär: ≥ 1 Schritt, Kontraktion ½, ‖dy‖ ≤ 2u‖y‖; adjungiert: ≥ 1 Schritt, weiter
  solange sich β um mehr als 1/16 ändert);
- B: `dgerfs` (BERR mit SAFE1/SAFE2, weiter solange BERR > ε und BERR ≤ LSTRES/2, höchstens 5);
- C: `dla_gerfsx_extended` (Zustände arbeitend/konvergiert/kein Fortschritt/instabil normweise und
  komponentenweise, ρ_thresh = 0,5, dz_ub = 0,25; ohne erhöhte Genauigkeit wird „Präzision
  erhöhen“ zu „kein Fortschritt“; höchstens 5 statt 10 Schritte);
- D: 5 feste Schritte.

| | geliefert/abgelehnt | Gate/Fehler min | Schritte | gestört 10⁻¹: Modelle mit Fehler > β (Werte, schlimmstes Verhältnis) |
|---|---|---|---|---|
| primär A | 2.786/25 | 7,3 | 1,37 | – |
| primär B | 2.786/25 | **1,1** | 0,99 | – |
| primär C | 2.786/25 | 7,3 | 1,81 | – |
| primär D | 2.786/25 | 7,3 | 5 | – |
| adjungiert A | 2.786/25 | 7,3 | 1,002 je Lösung | 14 (176, 1,006) |
| adjungiert B | 2.786/25 | 7,3 | 0,973 | 14 (211, 1,010) |
| adjungiert C | 2.786/25 | 7,3 | 1,114 | 6 (29, 1,000) |
| adjungiert D | 2.786/25 | 7,3 | 5 | 6 (29, 1,000) |
| adjungiert genau 1 | 2.786/25 | 7,3 | 1 | – |

- Keine Regel ändert eine Entscheidung. Kein geliefertes Modell hatte einen Fehler > τ, auch nicht
  gestört.
- Primär B endet ohne Schritt, wenn BERR schon ≤ ε ist. Dann überwiegt das Residual der
  Ausgangslösung in d, β ist scharf und verliert seine Reserve (Gate/Fehler 1,1). Das ist der
  Zustand von O15. [Sk80] begründet den Pflichtschritt.
- Die Fehlerschätzung von C (‖dx‖/(1 − ρ_max)) lag in 1.793 von 2.811 Modellen unter dem
  tatsächlichen Fehler; ohne erhöhte Residualgenauigkeit gilt sie nicht ([LAWN165] §2.4, §7).
- BERR nach `dgerfs` ist in 2.443 von 2.811 Modellen 1: Zeilen mit \|S\|\|y\| + \|b\| = 0 werden über
  SAFE1 zu (0 + SAFE1)/(0 + SAFE1). Für die Abbruchregel ist das harmlos, als Maß wäre es
  irreführend.
- Bei gestörter Faktorisierung liegt β mit A und B in 14 Modellen bis 1 % unter dem Fehler, mit C
  und D in 6. Das löst keine Regel; es ist eine Frage des Vertrauens in z (3.3).

**Ergebnis:** primär Regel A, eingeordnet als normweise Abbruchlogik nach [LAWN165] mit
Pflichtschritt nach [Sk80] (Status 2). Adjungiert siehe 3.3: Erst die Verifikation macht die
Regel zur reinen Kostenfrage.

### 3.3 Vertrauen in β (Phase 2.2, O15/O16)

**Was `xGERFSX` schätzt.** Das Trust-Flag vergleicht die geschätzte reziproke Kondition von S·A
(normweise) bzw. S·A·diag(x) (komponentenweise) mit √n·ε und setzt die Schranke andernfalls auf 1
[LAPACK-RFSX]. Geschätzt wird also die Kondition der *Lösung x* in einer Norm, und die Aussage beruht
auf der geometrischen Konvergenz bei Residuen in erhöhter Genauigkeit [LAWN165] §7. Beides passt
nicht zu β: SpanDraft braucht für jedes Funktional cᵀx die Genauigkeit der *adjungierten* Lösung z,
rechnet das Residual in double und hat Lösungskomponenten, die exakt null sind ([LAWN165] §7.3
schließt sie für die komponentenweise Schranke aus). Der Schwellenwert wird deshalb nicht übernommen.

**Was stattdessen gilt (exakt, ohne erste Ordnung).** Sei y* die exakte Lösung der exakten Daten
(A*, b*), e = ŷ − y*, Â das gespeicherte System, Â − A* =: ΔA mit |ΔA| ≤ Γ|Â| (Rundungszahlen der
Einträge) und ẑᵢ die berechnete adjungierte Lösung für Komponente i mit exaktem Residual
sᵢ = eᵢ − Âᵀẑᵢ. Dann gilt exakt

```text
|e_i| ≤ |ẑ_i|ᵀ d  +  (|s_i|ᵀ + |ẑ_i|ᵀ Γ|Â|) |e|  =  β_i + (M |e|)_i ,   M ≥ 0,
```

mit d aus 6.3 (Schranke des exakten Residuals). Gibt es v > 0 mit μ = maxᵢ (Mv)ᵢ/vᵢ < 1, so folgt
nach [Ru10] Satz 10.2 in der mit v gewichteten ∞-Norm ‖e‖_v ≤ 1/(1 − μ) (wegen β ≤ v) und damit

```text
|e_i| ≤ β_i + (M v)_i / (1 − μ).
```

Für Reaktionen und andere Funktionale gilt dasselbe mit der Zeile ihrer adjungierten Lösung. Es
entsteht keine Rekursion: Die n adjungierten Lösungen sind ohnehin berechnet, M braucht nur ihre
Residuen. |sᵢ| wird mit dem kompensierten Skalarprodukt Dot2 und seiner Schranke [ORO05] Satz 5.3
plus η je Operation gebildet.

**Gewichte.** v = D (Gate-Nenner) braucht einen Durchgang, versagt aber nahe τ (μ_D ≥ 1 in 65 der
2.811 Modelle). v = β versagt bei Komponenten, die exakte Zeilen exakt festlegen (β auf η-Niveau,
dort dominiert die η-Zulage des Residuals). Verwendet wird v = β + 2⁻²⁰⁰·D. Der D-Anteil vergrößert
die normierte Schranke höchstens um 2⁻²⁰⁰·μ_D und braucht einen zweiten Durchgang über die
gespeicherten adjungierten Lösungen.

**Gemessen** **[state-refine-summary]**:

| | nicht verifiziert (μ ≥ 1) | davon mit Fehler > β | verifiziert mit Fehler > β | korrigiertes/unkorrigiertes Gate max |
|---|---|---|---|---|
| ungestört, adjungiert A | 2 von 2.811 | 0 | 0 | 1,036 |
| ungestört, adjungiert B | 2 | 0 | 0 | 1,65 |
| ungestört, ohne Nachiteration | 2 | 0 | 0 | 6,2 |
| LU 10⁻⁴, A / B / ohne | 7 / 9 / 45 von 624 | 0 | 0 | – |
| LU 10⁻¹, A / B / ohne | 23 / 69 / 241 von 624 | 13 / 44 / 105 | 0 | – |

- Ungestört ist μ im Median 4·10⁻¹⁵, die Korrektur ändert das Gate höchstens um 3,6 %.
- In jedem verifizierten Fall, auch bei 10 % gestörter Faktorisierung, war β eine Schranke. Bei 10⁻¹
  lag das unkorrigierte β in 311 von 624 Modellen an mindestens einem Wert unter dem Fehler (14 davon
  geliefert); in 298 davon deckt die korrigierte Schranke den Fehler, die übrigen 13 meldet die
  Verifikation als nicht verifiziert.
- Die 2 ungestörten nicht verifizierten Modelle (s|1e-11|105, s|1e-11|800, Lager bzw. starre Feder
  im Abstand 3·10⁻¹¹ m) haben tatsächlich Fehler 1,9·10⁻¹⁶ bzw. 0. Ursache: Eine Komponente hat
  in skalierten Einheiten einen 10²² kleineren Nenner als ihre Nachbarn; schon die unvermeidliche
  Residualrundung der adjungierten Lösung (u-Niveau) lässt sich dort nicht mehr unter τ
  verifizieren. Auch eine Fixpunktiteration statt der Normschranke ergibt dort ≈ 10⁻⁷.
- Die Nachiterationsregel bestimmt nur noch Kosten und Verifizierbarkeit. A verifiziert bei
  gestörter Faktorisierung deutlich mehr Modelle als B (23 statt 69 nicht verifiziert bei 10⁻¹) bei
  praktisch gleichen Kosten (1,002 gegen 0,973 Schritte). C und D kosten mehr ohne
  Entscheidungsvorteil.

**Ergebnis:** O16 ist durch die Verifikation geschlossen, O15 ebenso: Ein überwiegendes Residual
macht β scharf, aber mit verifizierter Adjungierter nicht falsch. β ist für verifizierte Modelle
eine **Schranke** für die Lösung des gespeicherten Systems. Voraussetzungen sind korrekt
hergeleitete Rundungszahlen (O2), IEEE-Rundung zur nächsten Zahl mit graduellem Unterlaufen, kein
Überlauf und nach oben gerundete Auswertung der Schranken. Ohne Verifikation (μ ≥ 1) wird
abgelehnt. Adjungierte Regel A bleibt als bewusste Abweichung (Status 3): gemessener Vorteil
gegenüber B in der Verifizierbarkeit, gegenüber C und D in den Kosten.

### 3.4 Unterlauf (Phase 2.3)

**Gleiche Größe?** Teilweise. LAPACK addiert SAFE1 = NZ·safmin (safmin = 2⁻¹⁰²², kleinste normale
Zahl) zu jeder Komponente von \|r\| + NZ·ε(\|A\|\|x\| + \|b\|), deren Wert unter SAFE2 = SAFE1/ε liegt,
und behandelt im BERR-Quotienten Zähler und Nenner gleich [LAPACK-RFS]. Das sichert zwei Dinge:
Division durch null bzw. unterlaufene Nenner und den absoluten Unterlauffehler der
Residualberechnung. Letzteres ist dieselbe Größe wie der η-Anteil in d. Unterschiede:

- safmin deckt auch abruptes Unterlaufen (FTZ, Verlust bis safmin je Operation), η = 2⁻¹⁰⁷⁴ nur
  graduelles (Verlust ≤ η/2 je Operation) [De84]. SpanDraft rechnet in .NET mit IEEE-Standardmodus;
  das η-Modell setzt voraus, dass FTZ/DAZ nicht aktiv sind.
- SpanDraft addiert η je Operation und auch für Aufbau und Feldauswertung, LAPACK nur für das
  Residual.
- Der BERR-Quotient mit SAFE1 ergibt 1 für Zeilen mit \|S\|\|y\| + \|b\| = 0 (exakt verschwindende
  Zeilen). Das trat in 86 % der Modelle auf **[state-refine-*]**. Als Abbruchkriterium ist das
  harmlos (die Regel macht einen Schritt und stagniert dann), als Genauigkeitsmaß wäre es
  irreführend; es entspricht dem ω-Befund aus Runde 3.

**Pfade zu β = 0 oder zu kleinem Gate** **[state-underflow]** (64 gezielte Fälle):

| Fall | Ergebnis heute |
|---|---|
| Punktlast 10⁻³¹⁰ N bzw. 10⁻³²⁰ N | Bereichsregel (Anregungsprimitiv entdimensioniert nicht normal) greift |
| Streckenlast auf einem Abschnitt der Länge 2⁻³³⁰ L (q·a⁴ unterläuft) | geliefert, Fehler 2,5·10⁻¹⁷, gedeckt |
| Auswertung bei x = 10⁻³²⁰ m | geliefert, Fehler 8·10⁻¹⁷, gedeckt |
| Abschnitte 2⁻³³⁵ L, EI-Sprünge 10⁸, Federn 10⁻¹² … 10¹⁵ | kein subnormaler Eintrag durch die Äquilibrierung |
| Federn 10⁻³⁰⁰ … 10³⁰⁰ N/m | geliefert, Fehler ≤ 2·10⁻¹⁷ |
| **Kleinstbalken (L = 10⁻⁶⁰ m), w in SI subnormal** | **geliefert mit Fehler 9,7·10⁻⁸ bzw. 9,4·10⁻⁵ > τ bei Gate 2,4·10⁻¹⁵** |

Der letzte Fall ist ein echtes Gegenbeispiel: Entdimensioniert ist alles normal, erst die exakte
Zweierpotenz-Rückrechnung in SI unterläuft (w ≈ 10⁻³¹⁸ m). β in SI wird dabei ebenfalls
unterlaufen, der Rundungsfehler der Rückrechnung (≤ η/2) ist in β nicht enthalten. Abhilfe ohne
neue Konstante: Die Bereichsregel gilt auch für die SI-Bezugsgrößen S_q (normal und > 0), sonst
`NumericalRange`. Mit normalem S_q ist der Rückrechnungsfehler ≤ η/2/S_q ≤ 2⁻⁵³ und damit
unerheblich. Mit dieser Regel wird keines der 64 Modelle mehr falsch geliefert.

**Entscheidung:** η beibehalten (dieselbe Größe wie SAFE1, aber für graduelles Unterlaufen und
alle Operationen); Anforderung „graduelles Unterlaufen aktiv“ mit Selbsttest beim Start
(2⁻¹⁰²²/2 ≠ 0); Bereichsregel um die SI-Bezugsgrößen erweitert. SAFE1/SAFE2 als BERR-Formel nur
für die Abbruchregel (3.2), nicht als Genauigkeitsmaß.

### 3.5 Stück-Gate (Phase 3)

**Was geprüft wird.** Zwei Aussagen sind zu trennen ([CS66] verlangt exakte Koeffizienten, sonst
ist die Einschließung selbst gerundet):

- *Berechnetes Feld:* p̂ ist das Stückpolynom der berechneten Daten (Lösungsvektor,
  Abschnittsdaten, entdimensionierte Lastwerte), in exakter Bruchrechnung ausgewertet. Die
  Gleitkomma-Auswertung weicht davon nur um ihre eigene Auswertungsrundung ab, die β enthält.
- *Exaktes Feld:* p aus der exakten Referenz.

Beide Polynome entstehen durch exakte Interpolation an 6 rationalen inneren Punkten (Grad ≤ 5,
innerhalb eines Stücks hat jede Klammer einen festen Zweig). Die Bernstein-Koeffizienten werden
exakt gebildet und exakt halbiert (de Casteljau), bis eine zertifizierte Einschließung
[lo, hi] von min|p| auf dem Stück vorliegt (`state/piece_oracle.py`).

**Gemessen** **[state-piece-*]** auf 1.833 Modellen (840 gezielt, 956 Zufall, 37 konstruiert:
Wurzel am Stückrand, doppelte und fast doppelte Wurzeln, Extremum neben einer Ereignisstation,
Stücke bis 2·10⁻¹¹ L, Skalen L = 10⁻³ … 10³ m und EI = 10⁻⁶ … 10¹⁴, kleines Feld zwischen großen
Nachbarwerten, Vorzeichenwechsel von q), 18.828 Stücke mit LB_C > 0:

| Prüfung | Ergebnis |
|---|---|
| LB_C > min\|p̂\| (zertifiziert) | 0 |
| LB_C > min\|p\| (exaktes Feld) | 0 |
| kleinster Abstand (min\|p̂\| − LB_C)/β(x₁⁻) | 0,88: der Abzug von β wird höchstens zu 12 % „verbraucht“ |
| Gate mit Bernstein-Untergrenze statt LB_C, Verhältnis | 1,000 … 1,000; keine abweichende Entscheidung |

**Begründung, warum LB_C gilt**, jetzt ohne Stichprobe: Für p̂ gilt exakt |V̂(x) − V̂(x_e)| ≤ h·q_sum
und |M̂(x) − M̂(x_e)| ≤ |V̂(x_e)|·h + q_sum·h²/2. Der Gleitkommawert am Stückende weicht von p̂(x_e)
höchstens um die Auswertungsrundung ab, die in β(x_e) ≤ β(x₁⁻) enthalten ist (Monotonie, 7.3).
Daraus folgt LB_C ≤ min|p̂|; der Faktor (1 − γ(8)) deckt die Auswertung der Formel selbst. Das
Oracle bestätigt die Herleitung unabhängig. Für das exakte Feld gilt die Aussage nur, soweit β
den Fehler deckt (Runde 3, f|1e-06|872).

**Ergebnis:** Variante C bestätigt. Die Bernstein-Einschließung bringt kein schärferes Gate, weil
in den Stücken, in denen LB_C wirkt, |Q| fast konstant ist. Als Produktverfahren bräuchte sie
exakte oder gerichtet gerundete Koeffizienten; sie bleibt Oracle für Tests.

### 3.6 Extremwerte (Phase 4, O5)

**Oracle.** Für jedes Stück und jede Größe w (Wurzeln von θ), M (Wurzeln von V) und V
(Wurzeln von q) werden die Wurzeln der Ableitung von p̂ und von p in [0, 1] exakt isoliert:
quadratfreie Zerlegung nach [Yu76], Sturm-Folgen jedes Faktors über Q, dyadische Bisektion bis
2⁻⁹⁰, Vielfachheit aus dem Faktorindex, Wurzeln exakt bei 0 oder 1 durch exakte Auswertung.
Gegenprüfung mit sympy (`Poly.intervals`, unabhängige Implementierung): eine Abweichung, eine
Wurzel bei t ≈ −2·10⁻³¹, deren sympy-Intervall der Breite 10⁻³⁰ die 0 überdeckt; sie liegt außerhalb
des Stücks, das eigene Oracle ist dort richtig.

**Kandidaten des Produkts.** Portiertes `PolynomialRoots.InUnitInterval` auf dem Stückpolynom aus
dem berechneten Zustand am Stückanfang, Position x = fl(x₀ + t·h), Seite aus dem exakten
Vergleich mit x₀, x₁, Wert über `EvaluateAt` an genau dieser Stelle und Seite.

**Gemessen** **[state-piece-*]** (dieselben 1.833 Modelle):

| Prüfung | Ergebnis |
|---|---|
| exakte Wurzeln der Ableitung in [0,1] | berechnetes Feld 5.795 (davon mehrfach 2), exaktes Feld 6.308 (mehrfach 1) |
| lokale Extrema von p̂ ohne Kandidaten im Abstand ≤ 10⁻⁴ (Stückenden zählen als Kandidaten) | 0 |
| Kandidaten ohne Wurzel von p̂' in der Nähe (harmlos, ihr Wert ist ein Feldwert) | 9 |
| Wert am gemeldeten Ort reproduzierbar (`EvaluateAt` gleich gemeldeter Wert) | ja, alle |
| Positionsanteil \|p̂(t_c) − p̂(t*)\|, normiert | ≤ 8,7·10⁻²⁹ |
| Verhältnis zur Vorhersage zweiter Ordnung \|p̂''\|Δt²/2 (einfache Wurzeln) | ≤ 1,41 |
| Positionsanteil > β am Kandidaten | 147 Fälle, alle mit β = 0: der Kandidat rundet auf ein Lager, an dem w vorgeschrieben ist; Positionsanteil absolut ≤ 2·10⁻³⁵ m |
| Fehler der globalen Extrema (gemischtes Maß, exakte Referenz), gelieferte Modelle | ≤ 3,1·10⁻¹⁴; keiner > τ |
| konstruiert: exakte doppelte Wurzel von V (flaches M-Extremum), 1 ulp und 10⁻⁹ daneben, fast doppelte Wurzeln von θ | gefunden bzw. als ein Kandidat vertreten, Fehler ≤ 1,4·10⁻¹⁶ |

**Korrektur der Aussage „Position wirkt nur zweiter Ordnung“.** Sie gilt für einfache Wurzeln
(gemessen ≤ 1,41 × Vorhersage). Für mehrfache oder fast mehrfache Wurzeln ist die Lage schlecht
bestimmt; dann gilt nur die Schranke |p(t_c) − p(t*)| ≤ |Δt|·max|p'| auf dem Intervall zwischen t_c und
t*, und dort liegt |p'| auf Rundungsniveau. Außerdem ist der Positionsanteil *nicht* in β
enthalten. Die richtige Zusage lautet: Extremwertfehler ≤ β(x_c) + Positionsanteil; der
Positionsanteil war in allen Messungen ≤ 10⁻²⁸ der Skala.

**Ergebnis:** Die Kandidatenvollständigkeit ist experimentell sehr stark validiert (keine
übersehenen Kandidaten, gelieferte Extremwerte ≤ τ), aber nicht mathematisch zertifiziert. Ein
Beweis der Vollständigkeit von `PolynomialRoots` liegt nicht vor (Toleranzen
NearZero = 64ε, Clusterregel bei Grad 3). Das exakte Oracle wird deshalb Testreferenz der
C#-Abnahme (Extremwerte gegen exakte Isolation), nicht Produktcode.

## 4. Offene Punkte aus dem Audit

| Nr. | Punkt | Status nach Runde 4 | nächster Nachweis |
|---|---|---|---|
| O2 | Rundungszahlen je Formel | Runde 5: für die festgelegte Operationsfolge hergeleitet und geprüft ([FLOATING_POINT_CONTRACT.md](FLOATING_POINT_CONTRACT.md)) | C#-Konformitätstests, Plattformtests V1, V2, V5 |
| O3 | Pessimismus von β | 23 korrekte Ablehnungen nahe τ; die 2 nicht verifizierbaren Modelle sind seit Runde 5 verifiziert (5) | schärfere Rundungszahlen; Funktional je kritischer Größe |
| O5 | Extremwerte | experimentell validiert, nicht zertifiziert; Vorschlag Variante B (keine zertifizierte Vollständigkeitszusage) | Oracle als Testreferenz der Abnahme; Vertragsentscheidung |
| O9 | Aufwand | Verifikation braucht gespeicherte adjungierte Lösungen oder eine zweite Lösung | C#-Zeitmessung, Größengrenze |
| O13 | Grenzbereich τ | geschlossen (75 gezielte Fälle) | Übernahme in die Abnahme |
| O15, O16 | Vertrauen in β | geschlossen durch Verifikation | Test mit gestörter Faktorisierung |
| O17 (neu) | Voraussetzungen von GEPP | lokale Kopplung, polynomiale Übertragung | neue Prüfung bei Koppelbedingungen, Bettung, Theorie II. Ordnung |
| O18 (neu) | Graduelles Unterlaufen | Runde 5: keine Voraussetzung mehr (λ = SAFE1-artig, 5) | Plattformdiagnose; Bitgleichheit O12 |
| – | Konstante 1/16 der adjungierten Regel | Vorteil gegen BERR und LAWN 165 gemessen, Konstante selbst nicht variiert | Variation 1/4 … 1/64 in der Abnahme |
| – | Ruiz | keine Konditionsgarantie; Gradierung innerhalb von Spalten bleibt | keine (GEPP + Nachiteration kommen damit zurecht) |

Kein Befund dieser Runde stellt die Grundarchitektur infrage. Die Formulierung (lokale
Zustandsformulierung, Elimination, Band (2,2)) erweist sich als genau die Struktur, unter der die
Literatur GEPP als stabil ausweist. Der Fehlervertrag wird durch die Verifikation stärker, nicht
schwächer.

## 5. Nachtrag Runde 5: Gleitkomma-Vertrag (O2)

Keine breite neue Recherche. Genutzt wurden die vorhandenen Quellen an konkreten Stellen:

- [Hi02] Gleichungen (2.4)/(2.5), Lemma 3.1 und Abschnitt 4.2: Rundungsmodell einer Operation,
  Produktketten und rekursive Summation; daraus die Rundungszahlen je Operationsfolge.
- [Ru10] Satz 10.2 in komponentenweiser Form mit gewichteter ∞-Norm. Neu gegenüber Runde 4: Die
  Gewichte werden mit Neumann-Schritten v ← β + Mv + δD gewählt. Rump selbst iteriert in
  Verifikationsverfahren ähnlich (Krawczyk-Iteration, ε-Inflation). Das ist zulässig, weil der Satz
  für jedes positive v gilt. Es verifiziert die beiden Modelle, an denen Runde 4 scheiterte
  (Spektralradius von M ≈ 10⁻¹⁴).
- [De84] und [LAPACK-RFS]: λ = 2⁻¹⁰²² je Operation entspricht SAFE1 = NZ·safmin und gilt anders als
  η = 2⁻¹⁰⁷⁴ auch bei abruptem Unterlauf. Mit der Normierung der rechten Seite kostet das nichts.
- [ORO05]: Dot2 ist für die Verifikation nicht nötig. Der Rundungsanteil des double-Residuals hat
  dieselbe Struktur wie der Datenanteil von M.

Neu aufgenommen, weil die Plattformfrage (FTZ/DAZ) eine konkrete Aussage verlangt:

| Kürzel | Quelle | Aussage | Konsequenz |
|---|---|---|---|
| [IEEE19] | IEEE Standard for Floating-Point Arithmetic, IEEE Std 754-2019. DOI 10.1109/IEEESTD.2019.8766229 (über Crossref aufgelöst) | Rundung zur nächsten Zahl, Unterlauf, Tininess vor oder nach dem Runden zulässig; FTZ/DAZ sind nicht standardkonform, aber verbreitet | L4 und L5 des Vertrags gelten für beide Tininess-Varianten und für FTZ/DAZ |
| [Intel-SDM] | Intel 64 and IA-32 Architectures Software Developer's Manual, Vol. 1, MXCSR FTZ/DAZ | FTZ setzt subnormale Ergebnisse auf 0, DAZ liest subnormale Operanden als 0 | Software-Flush gespeicherter Vektoren und λ je Operation |
| [Arm-ARM] | Arm Architecture Reference Manual (A-profile), FPCR.FZ | FZ wirkt auf Operanden und Ergebnisse | wie oben |

### 5.1 Ergebnis Runde 5

Der Gleitkomma-Vertrag legt für jede numerisch relevante Formel die Operationsfolge fest. Er leitet
die Schranken daraus ab und prüft sie auf jeder Stufe der Beweiskette gegen exakte Arithmetik, auch
mit emuliertem FTZ/DAZ: 0 Verletzungen in 452 Kettenläufen und 2.295 Zufallsmodellen
**[state-fpc-summary]**. Gefunden wurde eine Lücke im bisherigen Rundungsmodell. Der Datenfehler
abgeschnittener Linearlasten nahe ihrem Nulldurchgang fehlte, mit einer Unterschätzung der
Lastintegrale bis zum 9,4·10¹³-Fachen **[state-fpc-stage-a]**. Status der Regeln: Residual und
Datenrundung (2 → 2, präzisiert), Unterlauf η → λ (2, jetzt plattformunabhängig), Verifikation
(2, Gewichte angepasst), Dot2 (nicht nötig).
