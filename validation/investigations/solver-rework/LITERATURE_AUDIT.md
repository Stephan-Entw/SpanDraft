# Literatur-Audit der Solver-Architektur

Stand: 10. November 2026, nach dem Closure-Pass. Dauerhaftes internes Audit- und Quellendokument zu
[`docs/SOLVER_ARCHITEKTUR.md`](../../../docs/SOLVER_ARCHITEKTUR.md) und zum
[Gleitkomma-Vertrag](FLOATING_POINT_CONTRACT.md). Es ist **keine normative Spezifikation**: Normativ
sind die Solver-Architektur und der Gleitkomma-Vertrag. Dieses Dokument

1. belegt, auf welchen etablierten Verfahren und Quellen die numerischen Entscheidungen beruhen,
2. trennt übernommene Methoden, angepasste Methoden, SpanDraft-spezifische Entscheidungen,
   verworfene Alternativen und eigene Validierungsbefunde,
3. dient als Grundlage für die spätere technische Dokumentation, insbesondere für das öffentliche
   Kapitel zu Validierung und numerischer Zuverlässigkeit.

Die Untersuchungen früherer Runden bleiben absichtlich im Wortlaut ihrer Zeit erhalten. Jede Zahl
verweist auf ein Protokoll **[Name]** in `results/`.

## 0. Lesehilfe

**Zeitliche Ebenen.** Die Datei enthält drei Arten von Aussagen:

| Kennzeichnung | Bedeutung |
|---|---|
| **Historisch (Runde n)** | Befund einer bestimmten Untersuchungsrunde mit den damaligen Begriffen (etwa β). Er bleibt unverändert und begründet die Entwicklung. |
| **Korrektur / Präzisierung** | Spätere Aussage, die einen historischen Befund ersetzt oder schärft. Sie steht im Abschnitt selbst als *Stand*-Absatz und gesammelt in 4.3. |
| **Aktuell (Closure)** | Gültiger Stand nach dem Closure-Pass. Er steht in Abschnitt 4 und 5. Bei Widerspruch gelten Abschnitt 4 und 5 sowie die normativen Dokumente. |

Die Abschnitte 1 bis 3 sind überwiegend historisch (Runden 1 bis 4). Wer den heutigen Stand sucht,
liest Abschnitt 4 (Kernentscheidungen mit Herkunft) und Abschnitt 5.

**Herkunftsklassen.** Jede heutige Entscheidung trägt genau eine Hauptklasse, gegebenenfalls
ergänzt um X:

| Klasse | Bedeutung |
|---|---|
| **L** | Aus der Literatur direkt übernommen. Quelle, Voraussetzungen und Aussage gelten unverändert für SpanDraft. |
| **A** | Etabliertes Verfahren, für SpanDraft angepasst. Die Quelle trägt den Kern, die Anpassung ist eigene Arbeit und steht in der Spalte „Anpassung“. |
| **S** | SpanDraft-spezifische Entscheidung. Quellen liefern Teilaussagen oder Rahmen, aber keine Vorgabe. Der Nachweis ist eigene Herleitung und eigene Prüfung. |
| **V** | Bewusst verworfene Alternative, mit Grund. |
| **X** | Zusatz: Die Aussage stützt sich nur auf die eigene experimentelle Untersuchung, nicht auf einen Beweis oder eine Quelle. |

Die frühere Statusspalte der Arbeitsmatrix (1 bis 4) entspricht: 1 = L, 2 = A, 3 = S oder V
(bewusste Abweichung bzw. Verwerfung). Eine Klasse „offen“ gibt es heute nicht mehr; offene Punkte
stehen in 4.4.

**Herkunft der Verfahren.** Mehrfachschießen, Gaußelimination mit partieller Zeilenpivotisierung (GEPP),
Nachiteration, Ruiz-Skalierung, komponentenweise Rückwärts- und Vorwärtsfehleranalyse,
Bareiss-Elimination und die Verifikation über eine genäherte Inverse sind etablierte Verfahren und
wurden nicht von SpanDraft entwickelt. SpanDraft-spezifisch sind ihre Kombination, die
Formulierung auf dem Balkenmodell, die Wahl der Strategieparameter, die zusammengesetzte
Fehlerkette bis zum veröffentlichten SI-Wert und das Gate. Keine einzelne Quelle belegt diese
Kombination.

**Begriff β.** β bezeichnet in den Runden 3 und 4 den komponentenweisen Fehlerindikator |z|ᵀd.
Es ist keine heutige Sammelbezeichnung. Heute gelten `RawBound`, `VerifiedStateBound` und
`VerifiedOutputBound` (Tabelle in 4.1).

## 1. Quellen

Alle DOIs wurden über Crossref aufgelöst (Runde 4). Satz- und Abschnittsnummern der Quellen stammen aus der damaligen Prüfung und wurden im Closure-Pass nicht erneut gegen die Originale gelesen. Die LAPACK-Aussagen stammen aus dem Quelltext der
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
| [ADD89] | M. Arioli, J. W. Demmel, I. S. Duff: Solving Sparse Linear Systems with Sparse Backward Error. SIAM J. Matrix Anal. Appl. 10(2), 165–190, 1989. DOI 10.1137/0610013 | Komponentenweiser Rückwärtsfehler als Abbruchkriterium der Nachiteration, Fehlerschranke ‖ \|A⁻¹\|(\|r\| + (n+1)ε(\|A\|\|x\| + \|b\|)) ‖, Behandlung kleiner Nenner | Residual mit bekannter Rundungsschranke | Ja | Grundlage von d. Die Abbruchregel der adjungierten Lösungen (Regel B) wurde untersucht und nicht gewählt (3.2) |
| [LAPACK-RFS] | Reference LAPACK `dgerfs.f` (Quelltext, aktuelle Version) | BERR = maxᵢ \|r\|ᵢ/(\|A\|\|x\| + \|b\|)ᵢ mit SAFE1 = NZ·safmin, SAFE2 = SAFE1/ε; Nachiteration solange BERR > ε, BERR ≤ LSTRES/2, höchstens ITMAX = 5; FERR = ‖\|A⁻¹\|(\|r\| + NZ·ε(\|A\|\|x\| + \|b\|) + SAFE1)‖/‖x‖ mit Hager/Higham-Schätzer `dlacn2` | Normweise Schätzung von \|A⁻¹\|; safmin deckt abrupten Unterlauf | Ja, mit Anpassungen (Abschnitt 3.2) | Regel B für adjungierte Lösungen; FERR-Form = d; SAFE1 ↔ η |
| [LAWN165] | J. Demmel, Y. Hida, W. Kahan, X. S. Li, S. Mukherjee, E. J. Riedy: Error Bounds from Extra-Precise Iterative Refinement. ACM TOMS 32(2), 325–351, 2006. DOI 10.1145/1141885.1141894; LAPACK Working Note 165 | Fehlerschätzung aus der geometrischen Abnahme der Korrekturen: ‖e‖ ≈ ‖dx‖/(1 − ρ_max); Zustände arbeitend/konvergiert/kein Fortschritt/instabil, ρ_thresh = 0,5; „vertrauenswürdig“, wenn die geschätzte Kondition < 1/(max(10, √n)·ε) | **Residual in erhöhter Genauigkeit**; Analyse ohne Über- und Unterlauf; keine exakt verschwindenden Lösungskomponenten (komponentenweise) | Die Abbruchlogik ja, die Fehlerschätzung nein: SpanDraft rechnet das Residual in double, und die Schätzung lag in 64 % der Modelle unter dem tatsächlichen Fehler **[state-refine-*]** | Kontraktionslogik als Primärregel (angepasst); keine Fehlerschätzung aus Korrekturen |
| [LAPACK-RFSX] | Reference LAPACK `dgerfsx.f`, `dla_gerfsx_extended.f` | Trust-Flag: normweise bzw. komponentenweise reziproke Kondition von S·A bzw. S·A·diag(x) (`dla_gercond`) gegen √n·ε; Fehlerschranke auf 1 gesetzt, wenn untrustworthy; Schranke ≥ max(10, √n)·ε | wie [LAWN165] | Die geschätzte Größe ist die Kondition der *Lösung* in einer Norm, nicht die Genauigkeit der adjungierten Lösung eines Funktionals; sie setzt erhöhte Residualgenauigkeit voraus | Schwelle nicht übernommen; ersetzt durch Verifikation nach [Ru10] (Abschnitt 3.3) |
| [Ru10] | S. M. Rump: Verification Methods: Rigorous Results Using Floating-Point Arithmetic. Acta Numerica 19, 287–449, 2010. DOI 10.1017/S096249291000005X | Satz 10.2: Ist ‖I − RA‖ < 1 in einer Matrixnorm, so ist A regulär und ‖A⁻¹b − x̃‖ ≤ ‖R(b − Ax̃)‖/(1 − ‖I − RA‖); Satz 10.6 (Krawczyk/Rump) komponentenweise | Rigorose Auswertung (gerichtete Rundung oder Schranken) der Größen | Ja, mit den berechneten adjungierten Lösungen als Zeilen von R und der gewichteten ∞-Norm | Verifikation μ < 1; heute Grundlage des `VerifiedStateBound` (Abschnitt 3.3 historisch, 4.2 Nr. 9) |
| [OP64] | W. Oettli, W. Prager: Compatibility of Approximate Solution of Linear Equations with Given Error Bounds for Coefficients and Right-Hand Sides. Numer. Math. 6, 405–409, 1964. DOI 10.1007/BF01386090 | Komponentenweiser Rückwärtsfehler ω = maxᵢ \|r\|ᵢ/(E\|x\| + f)ᵢ | – | Ja | ω als Diagnose |
| [Ha84] | W. W. Hager: Condition Estimates. SIAM J. Sci. Stat. Comput. 5(2), 311–316, 1984. DOI 10.1137/0905023 | Schätzer für ‖A⁻¹‖₁ mit wenigen Lösungen | normweise | Nein: komponentenweiser Vertrag | Nicht übernommen (Runde 2) |
| [GS02] | M. B. Giles, E. Süli: Adjoint Methods for PDEs: A Posteriori Error Analysis and Postprocessing by Duality. Acta Numerica 11, 145–236, 2002. DOI 10.1017/S096249290200003X | Fehler eines Funktionals = adjungierte Lösung gewichtet mit dem Residual (dual gewichtetes Residual) | lineares Funktional, exakte Adjungierte | Ja, für cᵀx | Runde 4: β_c = \|z\|ᵀd, die betragsweise Form. Heute `RawBound`; Fehler der Adjungierten → [Ru10] |
| [ORO05] | T. Ogita, S. M. Rump, S. Oishi: Accurate Sum and Dot Product. SIAM J. Sci. Comput. 26(6), 1955–1988, 2005. DOI 10.1137/030601818 | Kompensiertes Skalarprodukt Dot2 | IEEE-Arithmetik | Ja | Untersuchte Residualvariante (Ablation, kein Gewinn). Im heutigen Vertrag **nicht** verwendet: Das verifizierende Residual nutzt gewöhnliche Operationen mit Einzelschranken [GV 6]. Runde 4 nutzte Dot2 für das Residual der Adjungierten (3.3) |
| [De84] | J. W. Demmel: Underflow and the Reliability of Numerical Software. SIAM J. Sci. Stat. Comput. 5(4), 887–919, 1984. DOI 10.1137/0905062 | Graduelles Unterlaufen erweitert die Fehleranalyse um einen additiven Term und erleichtert zuverlässige Software | IEEE gradual underflow | Ja für graduelles Unterlaufen; abruptes Unterlaufen (FTZ/DAZ) braucht zusätzlich den größeren Verlust bis λ | Runde 4: η-Modell mit der Anforderung „graduelles Unterlaufen aktiv“. Heute (4.2, Nr. 11): absoluter Term mit λ je Operation, gültig für graduellen und abrupten Unterlauf |
| [Ba68] | E. H. Bareiss: Sylvester’s Identity and Multistep Integer-Preserving Gaussian Elimination. Math. Comp. 22, 565–578, 1968. DOI 10.1090/S0025-5718-1968-0226829-0 | Bruchfreie Elimination für exakten Rang ganzzahliger Matrizen | ganzzahlige bzw. rationale Daten | Ja (doubles sind rational) | Exakter Mechanismusrang (E9) |
| [Ru01] | D. Ruiz: A Scaling Algorithm to Equilibrate Both Rows and Columns Norms in Matrices. Rutherford Appleton Laboratory, RAL-TR-2001-034, 2001 (Technischer Bericht, kein DOI) | Iterative Zeilen-/Spaltenskalierung auf ∞-Norm 1, lineare Konvergenz (Rate 1/2) | keine Null-Zeilen/-Spalten | Ja | E8, mit Zweierpotenzen |
| [KRU14] | P. A. Knight, D. Ruiz, B. Uçar: A Symmetry Preserving Algorithm for Matrix Scaling. SIAM J. Matrix Anal. Appl. 35(3), 931–955, 2014. DOI 10.1137/110825753 | Konvergenzanalyse und Varianten des Ruiz-Verfahrens | – | Ja | Einordnung |
| [LAPACK-EQUB] | Reference LAPACK `dgeequb.f` | Skalierungsfaktoren als Potenzen der Basis; „Barring over- and underflow, scaling by these factors introduces no additional rounding errors“ | kein Über-/Unterlauf | Ja | E8, Zweierpotenzen (direkt übernommen) |
| [CS66] | G. T. Cargo, O. Shisha: The Bernstein Form of a Polynomial. J. Res. Nat. Bur. Standards 70B, 79–81, 1966 | min bᵢ ≤ p(t) ≤ max bᵢ auf [0,1]; Endkoeffizienten gleich den Endwerten | exakte Bernstein-Koeffizienten | Ja, als Oracle in exakter Arithmetik | Unabhängige Prüfung des Stück-Gates; kein Produktverfahren |
| [FR87] | R. T. Farouki, V. T. Rajan: On the Numerical Condition of Polynomials in Bernstein Form. Comput. Aided Geom. Design 4(3), 191–216, 1987. DOI 10.1016/0167-8396(87)90012-4 | Bernstein-Form ist auf [0,1] besser konditioniert als die Potenzform | – | Ja | Für ein Gleitkomma-Produktverfahren nötig; hier nicht gebraucht |
| [Co67] | G. E. Collins: Subresultants and Reduced Polynomial Remainder Sequences. J. ACM 14(1), 128–142, 1967. DOI 10.1145/321371.321381 | Exakte Restfolgen über ganzen Zahlen | – | Ja | Grundlage der Sturm-Folgen über Q |
| [Yu76] | D. Y. Y. Yun: On Square-Free Decomposition Algorithms. SYMSAC 1976, 26–35. DOI 10.1145/800205.806320 | Quadratfreie Zerlegung f = Π aᵢⁱ über ggT-Folgen | Charakteristik 0 | Ja | Vielfachheiten im Oracle |
| [CA76] | G. E. Collins, A. G. Akritas: Polynomial Real Root Isolation Using Descartes’ Rule of Signs. SYMSAC 1976, 272–275. DOI 10.1145/800205.806346 | Exakte Isolation reeller Wurzeln | quadratfreies Polynom | Ja | Alternative zu Sturm; sympy nutzt ein verwandtes Verfahren (Gegenprüfung) |
| [RZ04] | F. Rouillier, P. Zimmermann: Efficient Isolation of Polynomial’s Real Roots. J. Comput. Appl. Math. 162(1), 33–50, 2004. DOI 10.1016/j.cam.2003.08.015 | Effiziente exakte Isolation | – | Ja | Einordnung |

## 2. Arbeitsmatrix (Stand Runde 4)

**Historisch (Runde 4).** Die Matrix gibt den Stand der Runde 4 wieder, die Spalte „Status“ ist hier
auf Herkunftsklassen umgestellt (Lesehilfe 0). Die Begriffe β, η und der Wortlaut der Prüfungen
sind die damaligen. Wo spätere Runden Aussagen ersetzt haben, steht in der Zeile ein Verweis „→ 4.3“;
der heutige Stand steht in 4.2.

| SpanDraft-Entscheidung | Problem | etablierter Ansatz | Quelle | Voraussetzungen | trifft auf SpanDraft zu? | Abweichung | Prüfung und Ergebnis | Herkunft |
|---|---|---|---|---|---|---|---|---|
| E1 Zustandsformulierung / Mehrfachschießen | Randwertproblem mit vielen Zwischenbedingungen ohne Auslöschung | Mehrfachschießen, Mehrpunkt-BVP | [AMR95], [HM87b], [Ma85] | Stabilität über Entkopplung wachsender/abklingender Moden | Ja; Fundamentallösungen sind Polynome, Übertragungsmatrizen exakt obere Dreiecksmatrizen mit Einsen auf der Diagonale; keine Exponentialmoden | Keine Entkopplungs- oder Riccati-Transformation, weil keine Moden zu trennen sind | Strukturbeweis (3.1); Ablation früherer Runden | A |
| E2 mechanische vs. Ereignisstationen | Wo liegen Schießpunkte? | adaptive Schießpunkte nach Wachstum der Fundamentallösung | [MS84] | exponentielles Wachstum zwischen Knoten | Nein (polynomial) | Knoten nur an mechanischen Stationen; Lasten über geschlossene Integrale | Invariante 1 (226/226 bitgleich) **[state-invariance]** | S |
| E3 Elimination vorgeschriebener Komponenten | separierte Randbedingungen | Einbau separierter Randbedingungen durch Elimination bzw. Kompaktifizierung in fast blockdiagonalen Lösern | [Va76], [BW80], [AMR95] | separierte Bedingungen | Ja, alle Bedingungen sind lokal | Elimination ist exakt (bekannter Wert 0), ohne Rundung | Ablation: ohne Elimination bis O(1) Fehler **[state-ablation]**; Bandstruktur bleibt (2,2) | A |
| E4/E5 Stations- und Sprungbedingungen, Reaktionen als Sprung | Zwischenbedingungen, Reaktionen ohne Auslöschung | Zwischenbedingungen im Mehrpunkt-BVP | [HM87b] | lineare Bedingungen | Ja | Reaktionen keine Unbekannten (SpanDraft-spezifisch) | Ablation: Reaktionen als Unbekannte bis 1,6·10⁹ **[state-ablation]**; Federidentität (Runde 3) | A / S |
| E9 exakter Mechanismusrang | Mechanismus ohne Toleranz | bruchfreie Elimination | [Ba68] | exakte Daten | Ja | – | 12.500/12.500 übereinstimmend (Runde 1) | L |
| E8 Entdimensionierung | Zahlbereich, Einheitenunabhängigkeit | physikalische Bezugsgrößen; Skalierung mit Basispotenzen ist rundungsfrei | [LAPACK-EQUB], [LAWN165] §1 | kein Über-/Unterlauf | Ja | Bezugsgrößen nur aus Geometrie und Steifigkeit | bitgleiche Zweierpotenz-Invarianz 307/307 **[state-invariance]**; Unterlauf der SI-Rückrechnung gefunden (3.4) | A |
| E8 Zweierpotenz-Skalierung | rundungsfreie Skalierung | Faktoren als Basispotenzen | [LAPACK-EQUB] | kein Über-/Unterlauf | Ja; Unterlauf durch Skalierung in 64 gezielten Fällen nie aufgetreten **[state-underflow]** | – | – | L |
| E8 Ruiz-Äquilibrierung | Pivotqualität bei ungleich skalierten Zeilen und Spalten | Ruiz-Iteration | [Ru01], [KRU14] | – | Ja | 8 Iterationen, Faktoren gerundet auf Zweierpotenzen | Ablation: schützt Einzelfälle **[state-ablation]**. Strategieparameter (Durchgänge, Rundung auf Zweierpotenzen) sind SpanDraft-Wahl, keine Garantie [GV 8]. Grenze: Gradierung *innerhalb* einer Spalte bleibt (bis 10⁻³⁴), normweise κ∞ bis 10³³ **[state-cond-check]** | A |
| 5.2 Faktorisierung GEPP | stabile Lösung des Mehrfachschieß-Systems | GEPP; bei gekoppelten Randbedingungen strukturierte QR oder Spaltenelimination | [Wr93], [Wr92], [Va76], [Bo75] | Wrights Fehlerbild braucht Spalten außerhalb des Bandes | Nein, Band (2,2) für jedes Modell (3.1) | – | 2.956 Modelle (gezielt, Zufall, Stressfamilien bis 141 Stationen, adversarielle Suche): ρ ≤ 1,97; von 2.829 verglichenen keine LU-Lösung mehr als 10× schlechter als QR **[state-factor-summary]**. Heute zusätzlich: GEPP ist eine Effizienz- und Annahmefrage, die Schranke hängt nicht an ihr (4.2, Nr. 7) | L, X |
| QR als Referenz | robuste Alternative | Householder-QR (+ Nachiteration) | [Hi91] | u·κ∞ ≪ 1 | Nein: κ∞ bis 10³³ bei cond(S, y) ≤ 80 | – | QR liefert in 29 Modellen O(1)-Fehler, GEPP 10⁻¹⁶; β erkennt alle **[state-factor-summary, state-cond-check]** | V |
| 5.2 Nachiteration primär | Rückwärtsstabilität, Abbruch | ≥ 1 Schritt in Arbeitsgenauigkeit [Sk80]; Abbruch nach Kontraktion ρ_thresh = 1/2 und \|dx\| ≤ ε\|x\| [LAWN165]; BERR-Abbruch [LAPACK-RFS] | [Sk80], [LAWN165], [LAPACK-RFS] | Fehlerschätzung aus Kontraktion nur mit erhöhter Residualgenauigkeit | Abbruchlogik ja, Fehlerschätzung nein | Pflichtschritt; keine Fehlerschätzung aus Korrekturen (β übernimmt das) | Regeln A–D treffen identische Entscheidungen; Regel B (BERR) lässt β ohne Reserve (Gate/Fehler bis 1,1) **[state-refine-*]**. Heute Strategieparameter [GV 8], → 4.3 | A |
| 6.3 Nachiteration adjungiert | Genauigkeit von z | BERR-Regel [LAPACK-RFS] oder Kontraktion [LAWN165] | wie oben | – | Ja | β-Änderung > 1/16 statt BERR oder Kontraktion | gleiche Entscheidungen; mit Verifikation bei 10 % gestörter LU 23 statt 69 (B) nicht verifiziert, Kosten 1,002 gegen 0,973 Schritte; C und D teurer **[state-refine-summary]**. Eigene Wahl auf Messbasis, keine Literaturvorgabe | S, X |
| 5.2 Residualberechnung | Genauigkeit des Residuals | Arbeitsgenauigkeit für Rückwärtsstabilität [Sk80], [Hi97]; erhöhte Genauigkeit für Vorwärtsgenauigkeit [LAWN165] | [Sk80], [Hi97], [LAWN165] | β nutzt das gemessene Residual mit Rundungsschranke | Ja: Arbeitsgenauigkeit genügt dem Vertrag; erhöhte Genauigkeit nur für weniger Pessimismus | Residual in Arbeitsgenauigkeit mit Rundungsschranke (heute: Einzelschranken je Operation [GV 6]) | Ablation: kompensiert/exakt ohne Gewinn **[state-ablation]** | A |
| 6.3 komponentenweiser Indikator β | Fehler jeder Größe | \|A⁻¹\|(\|r\| + δr + …) [ADD89], FERR [LAPACK-RFS] | [ADD89], [OP64] | Rundungsschranke des Residuals; \|A⁻¹\| exakt oder geschätzt | Ja | \|A⁻¹\| zeilenweise exakt über adjungierte Lösungen statt Schätzer; Datenrundung mit hergeleiteten Rundungszahlen statt NZ·ε | Runde 4: O2 (Rundungszahlen) offen. → 4.3: Heute `RawBound`; Rundungszahlen sind im Gleitkomma-Vertrag hergeleitet, C#-Konformität offen (4.4) | A |
| 6.3 adjungierte Funktionale cᵀx | Reaktionen, korrelierte Größen | dual gewichtetes Residual [GS02]; Verifikation über genäherte Inverse [Ru10] | [GS02], [Ru10] | genaue Adjungierte bzw. ‖I − RA‖ < 1 | Ja | Gewichtete ∞-Norm mit v = β + 2⁻²⁰⁰·D; Residual der Adjungierten mit Dot2 (→ 4.3: heute v ≥ `RawBound`, Residual ohne Dot2) | verifiziert: nie Fehler > β, auch bei 10 % gestörter LU; 2 von 2.811 ungestörten Modellen nicht verifizierbar **[state-refine-summary]** | A |
| O15/O16 Vertrauen in β | Wann darf β vertraut werden? | Trust über Konditionsschätzung [LAPACK-RFSX]; Verifikation [Ru10] | [LAPACK-RFSX], [Ru10] | [LAPACK-RFSX]: erhöhte Residualgenauigkeit, Lösungskondition; [Ru10]: Schranken der Residuen | [LAPACK-RFSX] nein, [Ru10] ja | Schwellenwert √n·ε nicht übernommen; μ < 1 als Bedingung | O15, O16 geschlossen (3.3) | A |
| 6.3 Unterlauf η | absolute Fehler bei Unterlauf | SAFE1 = NZ·safmin im Residual-/Fehlerterm [LAPACK-RFS]; graduelles Unterlaufen [De84] | [LAPACK-RFS], [De84] | safmin deckt auch abruptes Unterlaufen; η = 2⁻¹⁰⁷⁴ nur graduelles | Ja, bei aktivem graduellem Unterlaufen | η je Operation statt einmal NZ·safmin; zusätzlich für Aufbau und Auswertung | 64 gezielte Fälle: Lücke bei unterlaufender SI-Rückrechnung, sonst gedeckt **[state-underflow]** (3.4). → 4.3: heute λ statt η, gültig auch für abruptes Unterlaufen | A |
| 7.3 Stück-Gate (Majorante, Untergrenze LB) | β und \|Q\| im ganzen Stück | Bereichseinschließung über Bernstein-Koeffizienten | [CS66], [FR87] | exakte oder gerichtet gerundete Koeffizienten | Ja, als Oracle | Produkt nutzt die geschlossene Untergrenze (Variante C), Bernstein nur als Oracle | 1.833 Modelle, 18.828 Stücke mit LB > 0 (3.5): LB ≤ min\|p̂\| zertifiziert ohne Ausnahme, LB ≤ min\|p\| exakt ohne Ausnahme, Bernstein-Gate gleich **[state-piece-*]** | S |
| 8 Extremwerte, `PolynomialRoots` | vollständige Kandidaten | exakte Isolation (Sturm/Descartes, quadratfreie Zerlegung) | [Co67], [Yu76], [CA76], [RZ04] | exakte Koeffizienten | Ja, als Oracle | Produkt bleibt Gleitkomma-Verfahren mit Toleranzen; Zusage um den Positionsanteil korrigiert (→ 4.3) | 1.833 Modelle: keine übersehenen Kandidaten, Extremwertfehler ≤ 3,1·10⁻¹⁴, Isolation stimmt mit sympy überein **[state-piece-*]** | A, X |
| Kondition, Hager/Higham-Schätzer | Normweise Grenze | Konditionsschätzer | [Ha84] | normweiser Vertrag | Nein | nicht verwendet | Runde 2. Verworfen, weil der Vertrag komponentenweise ist | V |

## 3. Ergebnisse der gezielten Prüfungen

### 3.1 Faktorisierung (Phase 1)

*Stand: Historisch (Runde 4), Struktur- und Wachstumsbefund. Er gilt unverändert. Ergänzung nach dem
Closure-Pass: Die Schranke hängt nicht an der Faktorisierungswahl (4.2, Nr. 7); `RawBound` und
`VerifiedStateBound` bleiben auch bei schlechter LU gültig. Die Wahl betrifft Effizienz und
Annahmequote. Die Voraussetzung lokaler Kopplung ist O17.*

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

*Stand: Historisch (Runde 4). Die Regeln A–D und der Indikator β sind der damalige Vergleich. Heute
sind Pflichtschritt, Abbruch mit 1/2, 2u und 1/16 sowie höchstens 5 Schritte Strategieparameter
[GV 8]: Sie bestimmen, welche Kandidaten entstehen, nicht die Korrektheit der Schranke. Die
Aussage „Regel B lässt β ohne Reserve“ betrifft den damaligen Indikator (4.3).*

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
Pflichtschritt nach [Sk80] (Herkunft A). Adjungiert siehe 3.3: Erst die Verifikation macht die
Regel zur reinen Kostenfrage.

### 3.3 Vertrauen in β (Phase 2.2, O15/O16)

*Stand: Historisch (Runde 4), in Runde 5 präzisiert. Der Kern, die Verifikation mit μ < 1 nach
[Ru10] Satz 10.2, gilt weiter. Geändert haben sich: (a) die Bezeichnung (β_i entspricht dem
`RawBound`, β_i + (M v)_i/(1 − μ) dem `VerifiedStateBound`), (b) das Residual der Adjungierten
(Runde 4 mit Dot2 [ORO05]; heute gewöhnliche Operationen mit Einzelschranken, [GV 6]), (c) die
Gewichte (heute v ≥ `RawBound` mit Start `RawBound` + 2⁻²⁰⁰·D, [GV 10]) und (d) die
Plattformvoraussetzung (heute auch abruptes Unterlaufen, 3.4). Die Zahlen unten sind die der Runde 4.*

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
abgelehnt. Adjungierte Regel A bleibt als bewusste Abweichung (Herkunft S): gemessener Vorteil
gegenüber B in der Verifizierbarkeit, gegenüber C und D in den Kosten.

### 3.4 Unterlauf (Phase 2.3)

*Stand: Historisch (Runde 4). Die Entscheidung am Ende des Abschnitts („Anforderung graduelles
Unterlaufen aktiv“, Selbsttest beim Start, η) ist durch den Closure-Pass ersetzt: Der Vertrag
gilt für graduellen und abrupten Unterlauf (FTZ/DAZ), der absolute Term je Operation verwendet λ,
und die gerichteten Primitives lesen gespeicherte Werte unabhängig von DAZ [GV 2, 3, 6]. Der
Befund zur unterlaufenden SI-Rückrechnung und die Bereichsregel dafür bleiben gültig.*

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

**Entscheidung (Runde 4, ersetzt, siehe Stand oben und 4.3):** η beibehalten (dieselbe Größe wie SAFE1, aber für graduelles Unterlaufen und
alle Operationen); Anforderung „graduelles Unterlaufen aktiv“ mit Selbsttest beim Start
(2⁻¹⁰²²/2 ≠ 0); Bereichsregel um die SI-Bezugsgrößen erweitert. SAFE1/SAFE2 als BERR-Formel nur
für die Abbruchregel (3.2), nicht als Genauigkeitsmaß.

### 3.5 Stück-Gate (Phase 3)

*Stand: Historisch (Runde 4). Die Aussage LB_C ≤ min|p̂| und die Oracle-Befunde gelten weiter. Die
Herleitung mit β(x₁⁻) und γ(8) ist die damalige Form; heute stehen die Fehlermajorante je Stück
und die gerichteten Untergrenzen in [GV 7].*

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

*Stand: Historisch (Runde 4). Die Zusage „Extremwertfehler ≤ β(x_c) + Positionsanteil“ ist ersetzt:
Heute gilt am ausgegebenen Ort und auf der ausgegebenen Seite dasselbe `VerifiedOutputBound` wie für
jeden Feldwert; Ortsfehler und Vollständigkeit der Kandidaten sind getrennte Validierungsgrößen
(O5, Variante B, [GV 11]). Die Messbefunde bleiben gültig.*

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

## 4. Aktueller Stand nach dem Closure-Pass

Dieser Abschnitt ist der aktuelle Teil des Dokuments. Er ersetzt bei Widersprüchen die historischen
Aussagen der Abschnitte 1 bis 3. Normativ bleiben die
[Solver-Architektur](../../../docs/SOLVER_ARCHITEKTUR.md) und der
[Gleitkomma-Vertrag](FLOATING_POINT_CONTRACT.md) (im Folgenden [GV]).

### 4.1 Begriffe

| Begriff | Bedeutung heute | Historische Entsprechung |
|---|---|---|
| `RawBound` | unmittelbare adjungierte Schranke \|R\|·d für den Residualanteil des Zustandsfehlers, gültig nur als Rohanteil [GV 1, 6] | β_i = \|ẑ_i\|ᵀd (Runden 3 und 4) |
| `VerifiedStateBound` | `RawBound` + Mv/(1 − μ), nur nach gerichtet bewiesenem Mv ≤ μv mit v > 0 und μ < 1 [GV 6] | β_i + (Mv)_i/(1 − μ) (3.3) |
| `VerifiedOutputBound` | Schranke für den veröffentlichten SI-Wert, einschließlich Zustands-, Funktional-, Daten-, Auswertungs- und Skalierungsfehlern [GV 1, 6] | Gate-Zähler „β in SI“ |
| τ | exakter Produktwert 1/100_000_000 (O1); Akzeptanz bei `VerifiedOutputBound` ≤ τ·Bezugsskala über den gerichteten Vergleich `ThresholdDown` [GV 7] | τ als gerundete Zahl 10⁻⁸ im Quotienten Gate = β/Skala |
| λ | kleinste positive Normalzahl 2⁻¹⁰²², Teil des absoluten Terms je Operation, deckt graduellen und abrupten Unterlauf [GV 3, 6] | η = 2⁻¹⁰⁷⁴ (nur graduell), SAFE1 in LAPACK |
| `NotVerified`, `Gate`, `NumericalRange`, `ZeroPivot` | Ablehnungsursachen: Kontraktion nicht nachgewiesen, Schwelle überschritten, Bereichsregel verletzt, Nullpivot [GV 7] | „abgelehnt“ ohne feinere Ursache |
| Regeln A–D, Varianten A–C | Untersuchte Alternativen (Nachiteration bzw. Stück-Gate). Gewählt sind Regel A (Strategie) und Variante C | – |

β, η und Dot2 dürfen in historischen Studien stehen bleiben, sind aber keine heutigen Begriffe des
Vertrags.

### 4.2 Heutige Kernentscheidungen mit Herkunft

Spalten: Herkunft (Lesehilfe 0), Quelle mit ihrer relevanten Aussage, Voraussetzungen und
Übertragbarkeit, SpanDraft-Anpassung, Nachweis. Verweise [GV n] sind Abschnitte des
Gleitkomma-Vertrags, E n Entscheidungen der Architektur.

| Nr. | Entscheidung | Herkunft | Quelle und Aussage | Voraussetzungen und Übertragbarkeit | SpanDraft-Anpassung | Nachweis |
|---|---|---|---|---|---|---|
| 1 | Zustandsformulierung als Mehrfachschießen über mechanische Stationen (E1, E2) | A | [AMR95], [HM87b], [Ma85]: Mehrfachschießen mit Zwischenbedingungen; Stabilität hängt an der Entkopplung wachsender und abklingender Moden | Übertragbar. Die Dichotomie ist trivial: Fundamentallösungen sind Polynome, die Übertragung ist eine obere Dreiecksmatrix mit Einsen auf der Diagonale. [MS84] (adaptive Schießpunkte) ist nicht anwendbar | Knoten nur an mechanischen Stationen, Lasten über geschlossene Integrale, Stationsregeln als Zwischenbedingungen; keine Entkopplung | Strukturableitung (3.1); **[state-invariance]**; exakte Bruchrechnungsreferenz `transfer_ref.py`, Vergleich in allen **[state-fpc-chain-*]** (0 Abweichungen) |
| 2 | Vorgeschriebene Komponenten werden eliminiert, nicht gelöst (E3) | A | [Va76], [BW80], [AMR95]: Einbau separierter Randbedingungen in fast blockdiagonalen Systemen | Alle Bedingungen sind lokal | Elimination ist exakt (bekannter Wert null); Reaktionen sind keine Unbekannten (siehe Nr. 3) | **[state-ablation]** (ohne Elimination Fehler bis O(1)); Bandstruktur (2,2) **[state-factor-summary]** |
| 3 | Reaktionen als Sprung mit eigenem adjungierten Funktional; bei Federn zusätzlich Federgesetz, Wahl nach kleinerem `VerifiedOutputBound` (E4) | S | [HM87b] liefert nur den Rahmen (Zwischenbedingungen), [GS02] die Fehlerdarstellung eines Funktionals über die Adjungierte | Teilübertragung | Auswahlregel und Identität beider Wege als Selbsttest sind eigene Konstruktion | **[state-ablation]** (Reaktionen als Unbekannte bis 1,6·10⁹); **[state-fpc-chain-spring-extremes]** (144 Modelle, 0 Verletzungen) |
| 4 | Exakter Mechanismusrang auf dem kinematischen Modell der Teilkörper (E9) | A | [Ba68]: bruchfreie Elimination liefert den exakten Rang rationaler Matrizen | Doubles sind rational | Das kinematische Modell der Teilkörper und die Unabhängigkeit von der Lösungsmatrix sind SpanDraft-Konstruktion | 12.500 von 12.500 Modellen stimmen überein (Runde 1) |
| 5 | Entdimensionierung und Normierung mit Zweierpotenzen (E8) | A | [LAPACK-EQUB]: Skalierung mit Potenzen der Basis ist rundungsfrei, solange kein Über- oder Unterlauf auftritt | Gilt nur im Normalbereich | Bezugsgrößen aus Geometrie und Steifigkeit, ganzzahlige Exponenten werden kombiniert und in einem Schritt angewandt; Range-Policy für Grenzbereiche [GV 5, 8] | **[state-invariance]** (307/307 bitgleich); Stufe A9 (SI-Rückrechnung) in **[state-fpc-stage-a]**; Unterlauffälle **[state-underflow]**, **[state-fpc-chain-underflow]** |
| 6 | Ruiz-Äquilibrierung mit Zweierpotenzen (E8) | A | [Ru01], [KRU14]: iterative Zeilen- und Spaltenskalierung, lineare Konvergenz | Keine Null-Zeilen oder -Spalten | 8 Durchgänge, Faktoren als ganzzahlige Exponenten, Wurzelkorrektur gerundet, ohne die Wurzel zu materialisieren. Strategieparameter [GV 8]: keine Konditionsgarantie | **[state-ablation]**; Grenze der Wirkung **[state-cond-check]** |
| 7 | LU mit Teilpivotisierung (GEPP) auf dem gebänderten System (E8) | L, X | [Bo75]: Pivotwachstum von GEPP auf Bandmatrizen hängt nur von der Bandbreite ab. [Wr93]: Gegenbeispiel, braucht Spalten außerhalb des Bandes | Übertragbar, weil Bandbreite strukturell (2,2) ist und keine Kopplung außerhalb des Bandes besteht | Die Bandbreitenableitung ist eigene Herleitung (3.1). Die Schranke des Closure-Passes hängt nicht an GEPP (3.1, Stand). Voraussetzung lokale Kopplung: Nr. 18 | ρ ≤ 1,97 auf 2.956 Modellen, adversarielle Suche **[state-factor-summary]**. Das Wachstum ist experimentell belegt, nicht bewiesen |
| 8 | Nachiteration: Pflichtschritt, Abbruch nach Kontraktion, höchstens 5 Schritte, adjungiert mit 1/16 | A | [Sk80]: ein Schritt in Arbeitsgenauigkeit macht GEPP komponentenweise rückwärtsstabil. [LAWN165]: Abbruchlogik mit ρ_thresh = 1/2 | [Sk80]: Bedingung nicht a priori nachgewiesen. [LAWN165]: Fehlerschätzung setzt erhöhte Residualgenauigkeit voraus | Pflichtschritt, keine Fehlerschätzung aus Korrekturen, Regel A für Adjungierte (S). Parameter nur Strategie [GV 8] | **[state-refine-*]** (2.811 Modelle): identische Entscheidungen A–D, A verifiziert bei gestörter LU mehr Modelle als B; Fehlerschätzung aus Korrekturen lag in 64 % unter dem Fehler |
| 9 | Verifizierte Zustandsschranke: `RawBound` und `VerifiedStateBound` über adjungierte Lösungen und Kontraktion μ < 1 | A | [Ru10] Satz 10.2: ist ‖I − RA‖ < 1, so ist A regulär und der Fehler durch ‖R(b − Ax̃)‖/(1 − ‖I − RA‖) beschränkt. [GS02]: dual gewichtetes Residual. [ADD89], [OP64]: komponentenweise Fehlerdarstellung | Rigorose gerichtete Auswertung. Übertragbar mit den berechneten Adjungierten als Zeilen von R | Gewichtete komponentenweise ∞-Norm; Startgewicht `RawBound` + 2⁻²⁰⁰·D, 3 Neumann-Schritte; Matrix-, RHS- und Residualfehler getrennt (δS, δsb, d) [GV 6, 10]. Die Wahl von Gewichten und Schritten ist Strategie, bei Misserfolg Ablehnung `NotVerified` | Stufen B bis D in **[state-fpc-summary]** und allen **[state-fpc-chain-*]**: 0 Verletzungen. Bei 10 % gestörter LU werden alle 89 Modelle abgelehnt, keines mit Fehler über der Schranke geliefert. Die zwei in Runde 4 nicht verifizierbaren Modelle sind **[state-fpc-unverified]** nach Neumann-Schritten verifiziert |
| 10 | `VerifiedOutputBound` für den veröffentlichten SI-Wert (Funktional, Daten, Auswertung, Rückskalierung) | S | [Hi02] liefert Rundungsmodell und Fortpflanzung, [GS02] den Funktionalfehler. Keine Quelle beschreibt diese Gesamtkette | Teilübertragung | Die Kette vom Zustand zum veröffentlichten Wert, die zwei Skalierungsfehler beim Reaktionsfunktional und die Behandlung abgeschnittener Linearlasten sind eigene Herleitung [GV 3, 6] | Stufe E in **[state-fpc-summary]**: Fehler des SI-Werts und des Extremwertkandidaten nie über der Schranke; größtes Verhältnis Fehler/Schranke ≤ 1 |
| 11 | Rundungsmodell, gerichtete Schranken, Unterlauf und FTZ/DAZ | A | [Hi02]: γ_k = ku/(1 − ku) für normale Koeffizientenketten. [De84]: graduelles Unterlaufen als additiver Term. [LAPACK-RFS]: SAFE1 = NZ·safmin | [Hi02] setzt normale, auslöschungsfreie Ketten voraus. [De84] betrachtet graduelles Unterlaufen | Absoluter Term `u·\|Ergebnis\| + λ` je Operation deckt graduellen und abrupten Unterlauf; `GammaUpper` und gerichtete Nachbarn; Primitives lesen gespeicherte Werte unabhängig von DAZ; Range-Policy mit Untergrenzen für L und SI-Skalen [GV 2, 3, 6, 8] | Stufe A (A1 bis A9) **[state-fpc-stage-a]**; **[state-fpc-boundary]** mit Mutantenerkennung; **[state-fpc-chain-underflow]** und **[state-underflow]** in allen vier Modi. Stufe A5 belegt, dass die Last-Datenschranke von Runde 4 unterschätzte (208 Verletzungen), A4 prüft den aktuellen Vertrag ohne Verletzung |
| 12 | Kontinuierliches Stück-Gate, Variante C | S | [CS66], [FR87]: Bernstein-Einschließung als exaktes Oracle, nicht als Produktverfahren | Bernstein braucht exakte Koeffizienten | Majorante B je Stück und geschlossene gerichtete Untergrenze LB für M und V sind eigene Konstruktion; Ereignisseiten separat [GV 7] | **[state-piece-*]**: 1.833 Modelle, 18.828 Stücke mit LB > 0, keine Verletzung gegen das Oracle; Stufe F |
| 13 | Akzeptanzschwelle τ und `ThresholdDown` | S | Keine Quelle | – | Exakter Produktwert, Vergleich der eingeschlossenen SI-Schranke mit der nach unten gerundeten Schwelle, Ganzzahlvergleich der Signifikanden [GV 7] | `products_and_gate` in **[state-fpc-boundary]**; Stufe F |
| 14 | Extremwerte: Variante B, `PolynomialRoots` als Produkt, Yun/Sturm als Oracle (O5) | A, X | [Co67], [Yu76], [CA76], [RZ04]: exakte Isolation reeller Wurzeln | Exakte Koeffizienten, daher nur als Oracle | Produkt bleibt ein Gleitkomma-Verfahren mit Toleranzen. Das `VerifiedOutputBound` gilt am ausgegebenen Ort und auf der ausgegebenen Seite; Ortsfehler und Vollständigkeit sind getrennte Größen [GV 11] | **[state-piece-*]**, **[state-fpc-oracles]**. Vollständigkeit ist experimentell sehr stark validiert, nicht bewiesen |
| 15 | Verworfene Alternativen | V | siehe Tabelle unten | | | |
| 16 | Plattformkonformität der C#-Implementierung (O2, O12, O19) | S | Keine Quelle | Vertrag verlangt gerichtete Primitives, Skalieren und Zwischenwerte mit modellierter Semantik, unabhängig von Fusion und Unterlaufmodus [GV 2] | Konformität wird je Plattform (insbesondere x64 und ARM64) durch die Stufen A bis F mit exakten Sollwerten nachgewiesen. Bitvergleiche sind nur Diagnose [GV 13] | **Noch nicht erbracht.** Die heutige Evidenz stammt aus der Python-Referenz auf Host-Binary64 mit emuliertem FTZ/DAZ. Sie belegt den Vertrag, nicht den C#-Code |
| 17 | Aufwand und Modellgröße (O9) | X | Keine Quelle | – | Verifikation ist bei fester Bandbreite quadratisch in der Zahl der Unbekannten [GV 12] | **[state-o9-sizing]**: nur Größenordnung; Grenze folgt aus C#-Messung |
| 18 | Voraussetzung lokale Kopplung und polynomiale Übertragung (O17) | A | [Wr93], [Bo75], [MS84], [Ma85], siehe Nr. 1 und 7 | Gilt für Biegung und Längskraft im heutigen Umfang | Bedingungen zwischen nicht benachbarten Stationen oder Exponentialmoden heben die Begründung auf. Neubewertung im betroffenen Feature-Track | Strukturableitung (3.1) |

**Verworfene Alternativen (Nr. 15).**

| Alternative | Quelle | Grund für die Verwerfung | Nachweis |
|---|---|---|---|
| Householder-QR als robustere Referenz | [Hi91], [Wr92] | Nachiteration setzt u·κ∞ ≪ 1 voraus. Die Matrizen sind trotz Äquilibrierung stark graduiert (κ∞ bis 10³³), komponentenweise aber mäßig konditioniert (cond(S, y) ≤ 80). Nur diese Matrixklasse, keine allgemeine Aussage über QR | 29 Modelle mit O(1)-Fehler, GEPP 10⁻¹⁶ **[state-factor-summary, state-cond-check]** |
| Trust-Flag und Schwelle von `xGERFSX` | [LAPACK-RFSX], [LAWN165] | Schätzt die Lösungskondition in einer Norm bei erhöhter Residualgenauigkeit, nicht die Genauigkeit der Adjungierten eines Funktionals | Gedankliche Begründung (3.3), Ersatz durch Nr. 9 |
| Fehlerschätzung aus der Korrekturfolge | [LAWN165] | Setzt Residual in erhöhter Genauigkeit voraus | Schätzung lag in 64 % der Modelle unter dem Fehler **[state-refine-*]** |
| Hager/Higham-Schätzer, normweise Kondition als Qualitätsmaß | [Ha84] | Vertrag ist komponentenweise. Kondition und Rückwärtsfehler dienen nur der Diagnose | Runde 2 |
| Kompensiertes Skalarprodukt Dot2 im Residual | [ORO05] | Kein Gewinn in der Ablation. Im heutigen Vertrag nicht verwendet | **[state-ablation]** |
| Entkopplung, Riccati-Transformation, adaptive Schießpunkte | [MS84], [Ma85] | Keine Exponentialmoden | Strukturargument (3.1) |
| Bernstein-Einschließung als Produkt-Gate | [CS66], [FR87] | Braucht exakte Koeffizienten, bringt kein schärferes Gate | **[state-piece-*]**: keine abweichende Entscheidung |
| Federgruppierung, Newton-Multipole | – | Keine geometrische Gruppierung nötig. Starke Evidenz, kein Beweis (E6, O10) | Architektur 5.4 |

### 4.3 Überholte Aussagen früherer Runden

Die Aussagen bleiben in den Abschnitten 1 bis 3 als Historie stehen. Heute gilt die rechte Spalte.

| Thema | Historische Aussage (Runde) | Heute |
|---|---|---|
| Fehlerindikator | β als Fehlerindikator und als „Schranke“ für das gespeicherte System (3, 4) | `RawBound`, `VerifiedStateBound`, `VerifiedOutputBound` (4.1) |
| Unterlauf | η = 2⁻¹⁰⁷⁴, Voraussetzung „graduelles Unterlaufen aktiv“, Selbsttest beim Start (4) | Absoluter Term mit λ je Operation, gültig für graduellen und abrupten Unterlauf. Gerichtete Primitives unabhängig von DAZ [GV 2, 3, 6] |
| Plattform | „.NET rechnet im IEEE-Standardmodus“ als Annahme (4) | Plattformunabhängiger Vertrag, nachzuweisen je Plattform (O2, O12, O19). Bitvergleiche sind Diagnose |
| Residual der Adjungierten | Dot2 mit Schranke aus [ORO05] (4) | Gewöhnliche Operationen mit Einzelschranken je Produkt und Subtraktion [GV 6] |
| Gewichte der Verifikation | v = β + 2⁻²⁰⁰·D (4) | v ≥ `RawBound`, Start `RawBound` + 2⁻²⁰⁰·D, 3 Neumann-Schritte; Strategie, Misserfolg ergibt `NotVerified` [GV 10] |
| Datenfehler abgeschnittener Linearlasten | Schranke von `state.py` (4) | Absolute Fehlerfortpflanzung mit `Bounded` [GV 3]; die pauschalen Faktoren wie 64 entfallen. Stufe A5 hält den alten Befund als Vergleich fest |
| Extremwertzusage | Fehler ≤ β(x_c) + Positionsanteil (4) | `VerifiedOutputBound` am ausgegebenen Ort und auf der ausgegebenen Seite; Ortsfehler und Vollständigkeit getrennt [GV 11] |
| Stück-Gate | Herleitung mit β(x₁⁻) und γ(8) (4) | Fehlermajorante je Stück und gerichtete Untergrenzen [GV 7] |
| Nachiterationsregel | „Regel B lässt β ohne Reserve“, Regel als Frage des Vertrauens (4) | Regelwahl betrifft nur Kosten und Verifizierbarkeit; Parameter in [GV 8] |
| O2 | „Rundungszahlen offen“ (4) | Im Vertrag hergeleitet. Offen ist die C#-Konformität (4.4) |
| O15, O16 | Geschlossen in Runde 4 mit β (4) | Nachweis mit den getrennten Schranken seit Runde 5 (Architektur O15, O16) |

### 4.4 Stand der Punkte aus Architektur und Vertrag

| Punkt | Entscheidung | Literatur- und Nachweisbezug |
|---|---|---|
| O1 (τ) | Geschlossen: exakter Produktwert 1/100_000_000, gerichteter SI-Vergleich | Produktentscheidung (Nr. 13), keine Quelle |
| O2 | Mathematischer Vertrag steht. C#-Konformität je Plattform ist Abnahme, nicht erbracht | [Hi02], [Ru10], [De84], [LAPACK-RFS]; Evidenz Python-Referenz (Nr. 9 bis 11, 16) |
| O3 | Pessimistische Ablehnungen bleiben möglich. Akzeptiertes Risiko | Folge verifizierter Schranken. Zahlen zu Ablehnungen in der Architektur 7.6 |
| O4 | Vollständiger belasteter Axialvertrag, zweite unabhängige Referenz und Axialfedergesetz sind Teil von DEV-12. Die heutige axiale Nullsemantik gehört zur Migration | **[state-fpc-axial-baseline]**; keine Literaturgrundlage nötig |
| O5 | Geschlossen mit Variante B | Nr. 14 |
| O6 | Fachliche Regeln für neue Feature-Koinzidenzen, etwa Gelenk/Lager, Feder/starres Lager und Abschnittswechsel/Halterung. Unterschiedliche starre Lagertypen am selben Ort gehören mit vereinigten Sperren und gemeinsamer Stationsreaktion zur Migration. | [Architektur §4.4](../../../docs/SOLVER_ARCHITEKTUR.md#44-gleiche-position-und-teilpunkte), [Migrationsabnahme §12](../../../docs/SOLVER_ARCHITEKTUR.md#12-migration-a10) |
| O9 | Aufwandsschätzung. Modellgröße und Latenz nach C#-Messung | Nr. 17 |
| O12, O19 | Fachliche Entscheidungen, Schranken, τ und Fehlerklassen gelten je Plattform. Primitives auf x64 und ARM64 prüfen | Nr. 16 |
| O17 | Lokale Kopplung und polynomiale Übertragung. Neubewertung im betroffenen Track | Nr. 18 |
| O18 | Gradueller und abrupter Unterlauf: explizites Residuallemma, lokale Fehlerfortpflanzung | Nr. 11 |

### 4.5 Closure-Evidenz in Kürze

Die folgenden numerischen Befunde stammen aus der Python-Referenz gegen
exakte Bruchrechnung. **[state-fpc-summary]** indiziert die Protokolle.
Die kanonischen [Closure-Kriterien C1–C13](../../../docs/SOLVER_ARCHITEKTUR.md#closure-kriterien-c1c13)
verknüpfen sie mit den weiteren Nachweisen.

- Stufe A: Primitives und lokale Formeln A1 bis A4 und A6 bis A9 ohne Verletzung in beiden Unterlaufmodi.
- Konstruierte Fälle (15), nahe τ (75, davon 71 geliefert, größter Fehler 1,99·10⁻⁹ bei Gate 8,57·10⁻⁹),
  gestörte LU 10⁻⁴ (89), Federextreme (144), gezielte Fälle (148) und Unterlauf (27 je Modus): in
  den Prüfungen B bis F keine Verletzung. Bei 10 % gestörter LU werden alle 89 Modelle abgelehnt.
- Unterlauf: In jedem der Modi ftz, daz, ftz_daz und gradual werden 17 von 27 Modellen geliefert, 10
  abgelehnt (`NumericalRange`). Kein gelieferter Wert verletzt seine Schranke.
- Regression gegen die Zufallsmengen der Runde 4: keine Werte oder Reaktionen über der Vertragsschranke,
  keine gelieferten Werte mit Schranke über τ·Bezugsskala (**[state-fpc-regress-*]**).

## 5. Gleitkomma-Vertrag und Closure-Nachweis (aktuell)

Dieser Abschnitt fasst die Literaturgrundlage des Closure-Passes zusammen. Der Vertrag selbst steht
im [Gleitkomma-Vertrag](FLOATING_POINT_CONTRACT.md).

[Hi02] trägt das Modell normaler Koeffizienten und das allgemeine
γ_k. Dynamische Rundungszahlen benötigen die gerichtete Auswertung
von γ_k oder eine direkt fortgepflanzte absolute Schranke.
Die frühere lineare Näherung unterschätzt γ_k bereits bei k = 2²⁷.

[Ru10] Satz 10.2 trägt die gewichtete komponentenweise Verifikation.
Die Rohschranke |R|d gilt für den unmittelbaren Residualanteil.
Die Ungleichung Mv ≤ μv mit μ < 1 liefert die Korrektur zur
verifizierten Zustandsschranke. Positive Gewichte und Neumann-Schritte
sind eine Strategie zur Verifikation.

[De84], [LAPACK-RFS] und der in Runde 5 herangezogene
Binary64-/FTZ-/DAZ-Abgleich begründen die Trennung relativer
Rundung und absoluter Verluste. Der Residualpfad weist die
Verluste seiner RHS, Produkte und Akkumulatoren einzeln nach.
Normale relative Lemmata setzen normale oder exakt nullwertige
Operanden voraus. Bei gemischten lokalen Folgen werden absolute
Fehler einschließlich der Datenfehler abgeschnittener Linearlasten
durch die tatsächlich verwendeten Operationen fortgepflanzt.

Die Abschnitte 1–3 halten Befunde zu früheren Prototypen fest.
Der aktuelle Vertrag, die Primitives, die Prüfung A–F und die
reproduzierbaren Protokolle stehen im
[Gleitkomma-Vertrag](FLOATING_POINT_CONTRACT.md). Die vorhandenen
Quellen reichen für die Herleitungen dieses Passes aus.

## 6. Hinweise für die spätere öffentliche Dokumentation

Aussagen aus diesem Dokument, die ohne Einordnung missverstanden werden könnten:

1. **Keine Quelle trägt das Gesamtverfahren.** Die Literatur belegt einzelne Bausteine (Nr. 1, 2, 4
   bis 9, 11). Die Kombination, die Fehlerkette bis zum SI-Wert (Nr. 10), das Stück-Gate (Nr. 12) und
   die Schwelle (Nr. 13) sind SpanDraft-spezifisch und nur durch eigene Herleitung und eigene Prüfung
   gestützt. Verfahren wie Mehrfachschießen, GEPP, Nachiteration, Ruiz, Bareiss und die
   Verifikation über eine genäherte Inverse sind nicht von SpanDraft entwickelt.
2. **„Schranke“ gilt unter Voraussetzungen.** `VerifiedOutputBound` gilt für Modelle, die Eingabe-,
   Mechanismus- und Bereichsprüfung bestehen und verifiziert sind, unter den Plattformvoraussetzungen
   des Vertrags (Architektur 7.4). Es ist kein Fehlerbeweis für beliebige Eingaben.
3. **Plattformkonformität ist noch nicht belegt.** Die Python-Referenz belegt
   den numerischen Vertrag. Die C#-Abnahme der Zielarchitektur steht aus
   (O2, O12, O19).
4. **Zahlen aus den Abschnitten 1 bis 3 gehören zu Prototypen.** Modellzahlen (2.956, 2.811, 1.833),
   ρ ≤ 1,97 und das Verhältnis Gate/Fehler beziehen sich auf bestimmte Modellmengen und teils auf
   die Indikatoren der Runde 4. Sie belegen die Entwurfsentscheidung, sind aber keine Produktzusage
   zu Annahmequoten (Architektur 7.4).
5. **Das QR-Ergebnis gilt für diese Matrixklasse.** Es ist keine allgemeine Aussage, QR sei weniger
   robust als GEPP.
6. **Vollständigkeit der Extremwertkandidaten ist validiert, nicht bewiesen** (Nr. 14).
7. **GEPP-Wachstum ist gemessen und strukturell begründet, nicht bewiesen.** Die Schranke hängt nicht
   daran (Nr. 7).
8. **Literaturangaben in Satz- und Abschnittsnummern** ([Ru10] Satz 10.2 und 10.6, [ORO05] Satz 5.3,
   [LAWN165] §2.4 und §7) wurden im Closure-Pass nicht erneut gegen die Originale gelesen. Vor
   öffentlicher Verwendung gegen die Quellen prüfen.
9. **Begriffe β, η, Dot2** sind historisch und sollen in öffentlichen Texten nicht auftreten (4.1).
