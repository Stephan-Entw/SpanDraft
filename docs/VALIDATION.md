# Unabhängige Verifikation des bestehenden V1-Solvers

Nachweisstand: 2026-10-03. **SpanDraft Euler-Bernoulli solver: validated for the V1 scope described in this report.**

Dieser Bericht dokumentiert die Abnahme vom 03.10.2026. Ausführungsumgebung,
Testzahlen und Abschlussnachweise beziehen sich auf diesen Stand. Anleitungen
für neue Prüfläufe stehen in [validation/README.md](../validation/README.md).

Alle 18 Acceptance-Fälle bestanden. V14 wurde als `PASS` bewertet: Ausschließlich
sechs Positionsvergleiche für IndeterminateBeam 2.4.0 sind ausdrücklich
`NOT APPLICABLE`, weil dessen Rundungsrestfelder keine belastbare Extremortmenge
des physikalischen Nullverlaufs liefern. Alle sechs Extremwerte wurden als
Pflichtvergleiche geprüft und bestanden, ebenso sämtliche übrigen anwendbaren Prüfungen.
Zum Abschluss bestand keine ungeklärte fachliche oder numerische Abweichung im
definierten Scope. Bei der Validierung einschließlich der V14-Untersuchung wurden
Produktcode in `src/`, bestehende Produkttests, `SpanDraft.sln`, Toleranzen und
alle 45 Golden References nicht verändert.

## Zweck, Ausgangsbasis und Grenzen

Die Ausgangsbasis bilden [KONZEPT.md](KONZEPT.md), [DOMAIN.md](DOMAIN.md),
[SOLVER.md](SOLVER.md), README und die bestehende Implementierung samt ihren
damals 200 Tests. Diese Validierung ergänzt Entwicklungsnachweise; sie erweitert keine
Produktfunktion. Die .NET-Prüfungen verwenden ausschließlich öffentliche
Domain-/Solver-APIs. Kein interner Assembly-Schritt, keine Solvermatrix und keine
interne Ableitungsfunktion wird für den unabhängigen Nachweis wiederverwendet.

Der untersuchte Scope ist ein gerader Einzelbalken, linear elastisch, mit kleinen
Verschiebungen und Rotationen, Euler-Bernoulli-Theorie, konstantem E, A und I,
Fixed/Pinned/Roller, PointForce, PointMoment und konstanten UDLs in der untersuchten
linearen Solverformulierung. Überhänge und statisch unbestimmte Lagerungen sind
eingeschlossen. Die Fallauswahl ist bewusst gut konditioniert; numerische
Grenzfälle bleiben in den vorhandenen Produkttests.

Diese Nachweise validieren weder die **reale Bauteilsicherheit** noch lokale Spannungen,
Kerben, Schweißverbindungen, Plastizität, Knicken, Beulen, Ermüdung,
Timoshenko-Schubverformung, geometrische Nichtlinearität, Normnachweise oder
Flächen-/Volumen-FEM. Öffentliche V1-Lasten enthalten keine Axiallasten:
u und N werden hier nur hinsichtlich ihres Nullverlaufs geprüft. Ein belasteter
axialer Solverpfad ist damit nicht unabhängig abgenommen. Auch variierende
Steifigkeiten, elastische Lager und Gelenkfreigaben sind nicht Gegenstand.

## Trennung und versionierte Eingaben

`SpanDraft.sln` bleibt die Haupt-Solution. Die eigenständige
[`validation/SpanDraft.Validation.sln`](../validation/SpanDraft.Validation.sln)
enthält die Validierungsbibliothek, CLI und xUnit-Tests sowie Referenzen auf
Core/Solver. Die Abhängigkeit verläuft nur von Validation zum Produkt. Beide
Solutions lassen sich ohne Python separat restaurieren und bauen. Ein normaler
Haupt-Testlauf prüft alle Produkttests; das vollständige Acceptance-Gate erfordert
zusätzlich die getrennte Validation-Solution.

[`validation/cases/`](../validation/cases/) enthält 18 neutrale JSON-Eingaben mit
Schema-/Fallversion, ID, Beschreibung, L/E/A/I, Lagern, Punktkräften,
Punktmomenten, UDLs und expliziten Auswertungspositionen samt Seite. Derselbe
Katalog speist alle drei Referenzanbieter und die .NET-Prüfungen. Eine geänderte
Eingabe macht die vorhandene Referenz über ihren SHA-256 ungültig. Der
Domain-Adapter setzt lediglich die obligatorischen, in diesen Prüfungen
mechanisch unbenutzten Werte W = 1 m³ und Streckgrenze = 235 MPa.

Basiswerte: **E = 210·10⁹ Pa, A = 0,003 m², I = 8·10⁻⁶ m⁴**.
Alle Längen/Positionen in m, Kräfte in N, Momente in Nm und UDLs in N/m.
P = Pinned, R = Roller, F = Fixed. Negative Kräfte/UDLs wirken nach unten.

| Case | L | Lager | Lasten |
| --- | ---: | --- | --- |
| V01 | 4 | P@0, R@4 | −1200@2 |
| V02 | 3 | F@0 | −800@3 |
| V03 | 3 | F@0 | UDL −500 [0;3] |
| V04 | 4 | P@0, R@4 | UDL −600 [0;4] |
| V05 | 4 | P@0, R@4 | Moment +700@1,5 |
| V06 | 4 | P@0, R@4 | −1300@1,2 |
| V07 | 5 | P@0, R@3,5 | −900@5 |
| V08 | 6 | P@1, R@4,5 | −750@6; UDL −350 [0,5;5,5] |
| V09 | 6 | P@0, R@3, R@6 | −1100@1,25 |
| V10 | 6 | P@0, R@3, R@6 | UDL −450 [0;3] |
| V11 | 8 | P@0, R@2, R@5, R@8 | −900@1; −700@6,5; UDL −250 [1;7] |
| V12 | 4 | F@0, F@4 | UDL −600 [0;4] |
| V13 | 4 | P@0, R@4 | UDL −300 [0,5;2,75]; UDL −450 [1,5;3,5] |
| V14 | 6 | P@0, R@2,5, R@6 | −1250 exakt am inneren Lager@2,5 |
| V15 | 7 | P@0,75, R@3,25, R@6,25 | −950@2; Moment +420@4,75; UDL −280 [1,25;5,75] |

Die drei V04-Varianten sind vollständige Fälle mit beiden Fremdsolvern,
Analytik und allen physikalischen Prüfungen, keine reinen Benchmarks:

| Case | L | E [Pa] | A [m²] | I [m⁴] | q [N/m] |
| --- | ---: | ---: | ---: | ---: | ---: |
| V04-S01 | 0,1 | 210·10⁹ | 3·10⁻⁵ | 8·10⁻¹⁰ | −6 |
| V04-S02 | 1 | 70·10⁹ | 3·10⁻⁴ | 8·10⁻⁸ | −60 |
| V04-S03 | 10 | 210·10⁹ | 3·10⁻² | 8·10⁻⁵ | −600 |

Der Katalog enthält Balkenenden, Lager, Punktlasten und UDL-Grenzen, zusätzlich
x/L = 0,137; 0,333; 0,618; 0,873 und in **jedem** mechanischen Teilintervall
Punkte bei 37 % und 63 %. Somit werden kontinuierliche Felder zwischen Knoten
geprüft. Alle inneren Ereignisse besitzen Left und Right; an den Balkenenden
stehen ausschließlich Right@0 und Left@L. Katalogprüfungen erzwingen diese
Abdeckung und verhindern fehlende oder doppelte Auswertungszeilen.

## Referenzquellen und reproduzierbare Umgebung

| Referenz | Festgelegte Version | Quelle / Methode |
| --- | --- | --- |
| IndeterminateBeam | **2.4.0** | [PyPI-Version](https://pypi.org/project/indeterminatebeam/2.4.0/), [Repository](https://github.com/JesseBonanno/IndeterminateBeam); symbolische Gleichgewichts-/Kompatibilitätslösung |
| PyCBA | **1.0.2** | [PyPI-Version](https://pypi.org/project/PyCBA/1.0.2/), [Repository](https://github.com/ccaprani/pycba), [Dokumentation](https://ccaprani.github.io/pycba/); unabhängige direkte Steifigkeitsmethode |
| Analytical | Anbieter **1** | [analytical.py](../validation/python/analytical.py); unabhängig integrierte geschlossene Lösungen, unten hergeleitet |

Ausgeführt mit **CPython 3.9.6**, macOS arm64, .NET SDK **10.0.401**.
[`requirements.txt`](../validation/python/requirements.txt) pinnt alle
23 direkten/transitiven Pakete exakt und enthält SHA-256-Hashes der publizierten
Artefakte. Dazu gehören NumPy 1.26.4, SciPy 1.13.1 und SymPy 1.13.3. Keine
Notebook-/Test-Extras sind installiert. Die vorgeschriebenen Fremdsolver bringen
Plotbibliotheken mit; im Adapter wird nichts geplottet. Pandas 2.2.3 ist wegen
eines unbedingten Imports von IndeterminateBeam notwendig, obwohl dessen Wheel
diese Abhängigkeit nicht deklariert. Die lokale `.venv` und temporäre Ergebnisse
sind ignoriert. Die Skripte prüfen Interpreter und sämtliche Paketversionen.

Keine Pakete werden automatisch aktualisiert. Die Fremdsolver sind weder NuGet-
noch Runtime-/Build-Abhängigkeiten; kein Fremdsolver-Code wird in SpanDraft
kopiert. .NET liest für Provenienzprüfungen lediglich die eingecheckten
Python-Quelltexte und Lockdatei als Bytes und startet keinen Interpreter.

## Vorzeichen und externe Adapter

Verbindlich: x nach rechts, w nach oben, θ gegen den Uhrzeigersinn; aufgebrachte
Kräfte nach oben und Momente gegen den Uhrzeigersinn positiv; N > 0 Zug,
M > 0 sagend, dM/dx = V. Alle Golden Files enthalten bereits SI und diese
Konvention. Die gemeinsame Zahlenvergleichslogik enthält keine solverabhängigen
Vorzeichenregeln. Die Abbildung wurde an allen neun analytischen Fällen direkt
zwischen Analytik und beiden Fremdsolvern kalibriert: **909 Zahlenvergleiche,
alle PASS**. Extremorte werden zusätzlich durch die .NET-Vergleiche geprüft.

| Größe | IndeterminateBeam → SpanDraft | PyCBA → SpanDraft |
| --- | --- | --- |
| Eingabe PointForce / UDL | Identität | Vorzeichen negieren (native Lastbeträge positiv nach unten) |
| Eingabe PointMoment | Identität | Identität (gegen Uhrzeigersinn positiv) |
| Rx / Ry / Rm | Identität | Ry / Rm Identität; Rx nicht verfügbar |
| w / θ / V / M | Identität | Identität |
| N | Identität | Nicht verfügbar |
| u | Nicht verfügbar | Nicht verfügbar |

**IndeterminateBeam:** `Beam(E,A,I,G=∞)` erzwingt Euler-Bernoulli; endliches G
würde Schubverformungen aktivieren. SI-Vorgaben werden ausdrücklich gegen die
gepinnten Einheiten geprüft. Lagerflags [u,w,θ]: F=[1,1,1], P=[1,1,0],
R=[0,1,0]. Public `analyse()` und `get_reaction()` liefern die Lösung bzw.
Reaktionen. Read-only gelesen werden `_units`, `_normal_forces`,
`_shear_forces`, `_bending_moments`, `_deflection_equation`. θ wird durch
Differenzieren des **vom Fremdsolver erzeugten** w-Ausdrucks bestimmt.
Symbol-/Polynomrepräsentation und Einheiten werden geprüft; Paketversion und
Quelltexthash sind festgelegt. Die öffentlichen Punktabfragen mit interner
Betragsauswahl und die Rasterextrema werden nicht als präzise Referenz genutzt.
Native Reaktionsausgabe rundet auf zehn Nachkommastellen.

**PyCBA:** Teilung an jedem mechanischen Ereignis, `elementType=1`, konstantes
EI pro Element, keine Freigaben, `GAv=None`, `kf=None`,
`check_stability=True`. UDL-Überlappungen werden als getrennte Lasten addiert.
Eine innere Punktlast wird genau einmal am rechten Ende des vorhergehenden
Elements aufgebracht. Keine künstlichen zusätzlichen Kräfte werden angesetzt.
Read-only gelesen werden `beam_results.D`, `.R` und `.vRes` mit `x,D,R,V,M`.
Layoutprüfungen sichern die gepinnte Repräsentation. `vRes[1:-1]` entfernt
Diagramm-Schließpunkte; die verbleibenden Punkte schließen die tatsächlichen
Elementendwerte ein. An Knoten stammen w und θ aus den nativen Freiheitsgraden D;
V/M aus dem jeweiligen Elementende. Innere Werte entstehen durch kubische
Interpolation der nativen Arrays, ohne ergänzende Balkengleichungen.

Nichtöffentliche Darstellungen sind versionsgebundene Validierungszugriffe,
keine Produkt-APIs. Eine neue Fremdsolverversion verlangt bewusste Adapterprüfung
und Referenzregeneration. Fehlende axiale Größen werden niemals durch erfundene
Fremdsolver-Nullwerte ersetzt.

## Sprungstellen, Extrema und PyCBA-Konvergenz

IndeterminateBeam: Die passende Ausdrucksbranche wird im offenen Teilintervall
gewählt und bis zum **exakten** Randpunkt fortgesetzt. PyCBA: Die getrennten
nativen Elementergebnisse bestimmen Left/Right. Es gibt keine Mittelwertbildung
und kein x ± ε. Bei Punktkräften und Lagerreaktionen werden V⁻/V⁺, bei Momenten
M⁻/M⁺ getrennt geprüft; auch w/θ-Kontinuität wird kontrolliert.

Extremwerte stammen bei den symbolischen Anbietern aus Polynomendpunkten und
inneren Ableitungsnullstellen. Für PyCBA dienen native θ-Nullstellen zur
Bestimmung von w-Extrema; für M/V werden die interpolierten Felder numerisch
differenziert. Mehrere Extremorte und konstante Intervalle bleiben in den
Referenzen erhalten. Die numerische Gleichheitsgrenze für die Kandidatenauswahl
ist 64·Maschinengenauigkeit·max|Kandidat|; keine Vergleichstoleranz wurde erhöht.

SpanDrafts öffentlicher Extremumrepräsentant muss zur externen Ortsmenge gehören.
Ein Plateau schließt seinen inneren Randgrenzwert ein (Right am Anfang, Left am
Ende). Eine andere Seite am selben Rand wird nur akzeptiert, wenn ihr explizit
gespeicherter Referenzwert innerhalb der bestehenden Größentoleranz ebenfalls
das Extremum annimmt. Das schützt kontinuierliche UDL-Grenzen und akzeptiert
keine gegenüberliegende Seite eines Kraftsprungs. Ein eigener Guard-Test prüft
diesen Unterschied. Externe Ortsmengen werden nicht auf SpanDrafts
Repräsentantenregel umgeschrieben.

PyCBA beginnt mit 1024 Rasterunterteilungen je Element und verdoppelt bis maximal
131072. Sämtliche Samples, Extremwerte, Extremorte und Plateaugrenzen müssen bei
**zwei aufeinanderfolgenden** Verfeinerungen um höchstens 0,1 ihrer jeweiligen
Vergleichstoleranz abweichen. Die Ortsmengentopologie muss stabil sein. Zusätzlich
wird die Drift der rasterintegrierten w/θ-Endwerte gegenüber den nativen
Freiheitsgraden mit derselben 0,1-Grenze kontrolliert. Alle 18 Fälle konvergieren.
Die Golden Files speichern jedes Raster, maximale Änderungen, Toleranzquotienten,
beobachtete Konvergenzordnung und Endwertdrift. .NET prüft diese Evidenz; ein
behauptetes PASS ohne Konvergenz wird abgewiesen.

| Case | Endraster je Element | Case | Endraster je Element |
| --- | ---: | --- | ---: |
| V01 | 32768 | V10 | 32768 |
| V02 | 16384 | V11 | 65536 |
| V03 | 16384 | V12 | 65536 |
| V04 | 65536 | V13 | 8192 |
| V05 | 16384 | V14 | 4096 |
| V06 | 16384 | V15 | 8192 |
| V07 | 32768 | V04-S01 | 16384 |
| V08 | 32768 | V04-S02 | 32768 |
| V09 | 16384 | V04-S03 | 131072 |

## Analytische Referenzen

Die Referenzrechnung ruft SpanDraft nicht auf und verwendet keine dessen
Ergebnisse. Sie beginnt mit rationalen SI-Eingaben, statischem Gleichgewicht und
EI·w″ = M. Erst für die Speicherung werden Zahlen in double umgewandelt.
Die folgenden Formeln definieren die gesamte Referenz, einschließlich θ=w′,
V=M′ auf offenen Intervallen und u=N=0. Randwerte werden einseitig ausgewertet.
Mit f als vorzeichenbehafteter Kraft, q als UDL, C als Moment und
z=max(x−a,0) gilt:

- V01/V06, einfach gelagert, Kraft f@a:
  Ra=−f(L−a)/L, Rb=−fa/L;
  M=Ra·x+f·z, V=Ra links und Ra+f rechts von a;
  EI·w=Ra·x³/6+f·z³/6+c₁x,
  c₁=−[Ra·L³/6+f(L−a)³/6]/L. w(0)=w(L)=0.
- V02, Kragarm mit Endkraft:
  Ry=−f, Rm=−fL, M=f(L−x), V=−f;
  w=f·x²(3L−x)/(6EI). w(0)=θ(0)=0.
- V03, Kragarm mit UDL:
  Ry=−qL, Rm=−qL²/2, M=q(L−x)²/2, V=q(x−L);
  w=q·x²(6L²−4Lx+x²)/(24EI). w(0)=θ(0)=0.
- V04 und Skalierungen, einfach gelagert mit UDL:
  Ra=Rb=−qL/2, M=Ra·x+qx²/2, V=Ra+qx;
  EI·w=Ra·x³/6+qx⁴/24+c₁x,
  c₁=−[Ra·L³/6+qL⁴/24]/L. w(0)=w(L)=0.
- V05, einfach gelagert mit innerem Moment:
  Ra=C/L, Rb=−C/L, M=Ra·x−C·H(x−a), V=Ra;
  EI·w=Ra·x³/6−C·z²/2+c₁x,
  c₁=−[Ra·L³/6−C(L−a)²/2]/L. w(0)=w(L)=0;
  M(a⁺)−M(a⁻)=−C, w und θ stetig.

Wichtige Sollwerte aus diesen Formeln (gerundet dargestellt, volle Präzision in
den Golden Files):

| Case / System | Reaktionen | Relevante Durchbiegung [m] / Ort [m] | Moment [Nm] | Verglichen / Ergebnis |
| --- | --- | --- | --- | --- |
| V01, mittige Kraft | Ry=600;600 | wMin=−0,000952380952381 @2 | MMax=1200 | Alle verfügbaren Reaktionen, u/w/θ/N/V/M, sechs Extrema samt Ortsmengen: PASS |
| V02, Endkraft | Ry=800; Rm=2400 | wMin=−0,00428571428571 @3 | MMin=−2400 | Wie V01: PASS |
| V03, Kragarm-UDL | Ry=1500; Rm=2250 | wMin=−0,00301339285714 @3 | MMin=−2250 | Wie V01: PASS |
| V04, einfache UDL | Ry=1200;1200 | wMin=−0,00119047619048 @2 | MMax=1200 | Wie V01: PASS |
| V05, inneres Moment | Ry=175;−175 | wMax=0,000187991506971 @2,244057707858 | M⁻=262,5; M⁺=−437,5 @1,5 | Wie V01 einschließlich Momentsprung: PASS |
| V06, unsymmetrische Kraft | Ry=910;390 | wMin=−0,000827359486659 @1,796971781086 | MMax=1092 @1,2 | Wie V01: PASS |
| V04-S01 | Ry=0,3;0,3 | wMin=−4,65029761905·10⁻⁸ @0,05 | MMax=0,0075 | Wie V01: PASS |
| V04-S02 | Ry=30;30 | wMin=−0,000139508928571 @0,5 | MMax=7,5 | Wie V01: PASS |
| V04-S03 | Ry=3000;3000 | wMin=−0,00465029761905 @5 | MMax=7500 | Wie V01: PASS |

## Toleranzmodell und physikalische Prüfungen

Für jeden Zahlenvergleich gilt |actual−reference| ≤ absTol+relTol·|reference|.
Die vorgeschlagenen externen Starttoleranzen wurden **unverändert** verwendet:

| Größe | absTol | relTol |
| --- | --- | --- |
| Kraft / Reaktion / N / V | 10⁻⁵ N | 10⁻⁶ |
| Moment / Reaktionsmoment | 10⁻⁵ Nm | 10⁻⁶ |
| u / w | 10⁻¹⁰ m | 10⁻⁶ |
| θ | 10⁻¹⁰ rad | 10⁻⁶ |
| Extremposition | 10⁻⁸ m | 10⁻⁷ |

Gleichgewicht, Metamorphie und physikalische Feldprüfungen verwenden strenger
10⁻⁷ absolut für Kräfte/Momente bzw. 10⁻¹² für Verschiebungen/Rotationen, jeweils
relTol=10⁻⁹. Für dV/dx gelten 10⁻⁷ N/m absolut. Referenzgleichgewichte verwenden
die externen Toleranzen. Axiale Größen werden separat als Nullgrößen geprüft.

Für alle 18 Fälle: Lasten ×2, Lasten ×−1, E×2, I×2 sowie eine Nullkraft bei
41 % des längsten mechanischen Teilintervalls. Das Domain Model erlaubt diese
Last; geprüft wird auch, dass tatsächlich ein zusätzlicher Knoten entsteht.
Keine produktive Test-Sonderfunktion ist notwendig. Lastskalierung und Umkehr
prüfen alle Reaktionen und u/w/θ/N/V/M. Bei E×2 halbieren sich u/w/θ, bei I×2
w/θ; Reaktionen und Schnittgrößen bleiben unverändert. Eine Aussage über
A-Skalierung axial belasteter Systeme wird daraus nicht abgeleitet.

Sechs Superpositionstests ergänzen die 90 Transformationen: V08/V11/V15 trennen
Punktlasten/Momente von UDLs; V13 trennt die überlappenden UDLs; V04 ergänzt
−150 N bei 0,41L, V09 ergänzt −100 N/m auf [0;3]. Geprüft werden Reaktionen
und alle sechs Felder an den katalogisierten Positionen. **96 Tests: PASS.**

Globales Gleichgewicht wird direkt aus Eingaben und veröffentlichten Reaktionen
berechnet: ΣFx=0, ΣFy=0 und ΣM₀=0. UDL-Resultierende q(b−a) am Schwerpunkt
(a+b)/2, Punktmomente und Reaktionsmomente werden berücksichtigt. Alle
18 SpanDraft-Fälle sowie alle 45 Referenzen bestanden ihre jeweils unterstützten
Gleichgewichtsprüfungen. Für PyCBA ist horizontales Gleichgewicht nicht extern
prüfbar, weil Rx fehlt; SpanDraft prüft es für jeden Fall.

Sprungprüfungen verwenden V⁺−V⁻=ΣF+Ry und M⁺−M⁻=−ΣC−Rm an inneren Ereignissen.
Die Differentialbeziehungen werden unabhängig durch zentrale Differenzen an
37 % und 63 % jedes offenen Teilintervalls geprüft, jeweils mit
h=0,01·Intervalllänge und h/2. Beide Schrittweiten müssen dM/dx=V und dV/dx=q
erfüllen. Kein Stencil überschreitet eine Sprungstelle. Bei konstanten UDLs sind
M quadratisch und V linear, sodass die zentrale Differenz theoretisch keinen
Trunkationsfehler dieser Ableitungen besitzt; die zweite Schrittweite prüft die
numerische Stabilität. Zusammen mit Nullgrößen, Kontinuität und Gleichgewicht:
**1163 physikalische Zahlenvergleiche, alle PASS** (Metamorphiezahlen separat).

## Gefundene Abweichungen und Untersuchung

Jede Abweichung wird in der Reihenfolge Modell/Lagerung, Theorie, Einheiten,
Lastdefinition, Vorzeichen, Seite, numerische Toleranz und Ursache untersucht.
Es gibt keine Mehrheitsentscheidung. Die historischen Zahlen-/Seitenfehler
stehen vollständig strukturiert in
[`initial-report.json`](../validation/results/initial-report.json); der abschließende
Abnahmestand steht in [`acceptance.json`](../validation/results/acceptance.json).
Der initiale Bericht bezieht sich auf frühere Adapterstände und ist kein
abschließender Acceptance-Nachweis.

| Fall | Befund und Ursache | Maßnahme / Status |
| --- | --- | --- |
| V12 / PyCBA | Differenzierung der rasterintegrierten w-Interpolation erzeugte nahe der Einspannung eine zusätzliche künstliche stationäre Stelle. Die Ortsmengentopologie verhinderte den geforderten Konvergenznachweis. Randbedingungen, Lasten, SI und Theorie stimmten überein. | w-Stationarität aus dem bereits vorhandenen nativen θ-Feld, Randwerte aus nativen Freiheitsgraden; unabhängige Raster-Endwertdriftprüfung beibehalten. Konvergenz bei 65536, alle Vergleiche PASS. |
| V13 / beide | VMin@3,5 Left wurde beim Entfernen redundanter Plateaugrenzpunkte im Adapter verloren; der Ort stimmte exakt überein, die zulässige Seite fehlte. Bei PyCBA betrug der verbleibende Unterschied zum Plateauwert nur ungefähr 2·10⁻¹¹ N. Kein Kraftsprung an dieser UDL-Grenze. | Nur tatsächlich plateauinnere Seiten entfernen; gegenüberliegende Seite mit ihrem expliziten nativen Grenzwert nach vorhandener Krafttoleranz prüfen. Guard gegen echte Kraftsprünge ergänzt. PASS. |
| V14 / IndeterminateBeam | Die Kraft sitzt genau auf einem gesperrten Verschiebungsfreiheitsgrad. Physikalisch Ry=1250 N dort, alle übrigen Reaktionen und inneren Felder null. Die native floating-point `linsolve`-/`float(ans)`-Substitution hinterlässt winzige Restpolynome; deren stationäre Orte repräsentieren kein belastbares Nullplateau. | Native Zahlen und Orte unverändert behalten; ausschließlich die sechs Positionsvergleiche als NOT APPLICABLE bewerten. Alle Extremwerte verpflichtend vergleichen. V14 PASS; kein erfundenes Nullplateau, keine Toleranzänderung und kein Solver-Fix. |

### V14: Prüfung der nativen IndeterminateBeam-Felder

Die reproduzierbare, ausschließlich auf V14 begrenzte Untersuchung
[`v14_native_fields.py`](../validation/investigations/v14_native_fields.py)
ruft die unveränderten nativen APIs `Beam`, `Support`, `PointLoadV` und
`analyse()` mit den katalogisierten Eingaben auf. Sie liest die vier vom
Referenzsolver erzeugten Ausdrücke und wählt deren offene Intervallbranchen.
Symbolische Expansion und Differentiation prüfen exakte Konstanz; es werden
keine eigenen Balkengleichungen, Ersatzreaktionen, Nullsetzungen oder
Präzisionsänderungen verwendet. Der native Quelltexthash, die vollständigen
Ausdrücke, Ableitungen, Reaktionen und unveränderten Roh-Extrema stehen in
[`v14-native-fields.json`](../validation/results/v14-native-fields.json).
Die V14-Referenz wurde dabei bis auf den Zeitstempel exakt reproduziert.

| Größe | Native Darstellung auf (0;2,5) und (2,5;6) | Belastbare physikalische Null-Extremortmenge aus IB? |
| --- | --- | --- |
| N | Exakt 0 auf beiden Intervallen | Ja; N hat im definierten Katalog keinen Extremortvergleich |
| w | Zwei nichtkonstante kubische Restpolynome | Nein |
| V | Konstante −3,3306690738754716·10⁻¹³ N bzw. +3,49054118942149·10⁻¹³ N | Nein; zwei unterschiedliche Restplateaus, kein gemeinsames Nullplateau |
| M | Zwei nichtkonstante lineare Restpolynome | Nein |

Die Querkraft-Rohplateaus **sind** aus IB bestimmbar: VMin auf [0;2,5],
VMax auf [2,5;6], jeweils mit den gespeicherten inneren Randseiten. Sie bleiben
im Golden File erhalten. Sie beschreiben jedoch die unterschiedlichen
Rundungsrestwerte, nicht die Extremortmenge des physikalisch identischen
Nullverlaufs. Ihr Zusammenlegen zu [0;6] oder das Nullsetzen der Restpolynome
wäre eine zusätzliche, von IB nicht belegte Referenzannahme und wird nicht
vorgenommen. Auch die w-/M-Rohorte bleiben unverändert erhalten.

Physikalisch ist die Last am gesperrten Verschiebungsfreiheitsgrad direkt durch
dessen Lagerreaktion ausgeglichen. Für den Nullverlauf nimmt jeder Ort denselben
Minimal- und Maximalwert an; es existiert daher kein eindeutiger Extremort.
Die gerundeten nativen Reaktionen sind Ry=(0;1250;0) N. In den gespeicherten
Samples liegen maximal |w|=2,412·10⁻¹⁸ m, |θ|=8,121·10⁻¹⁹ rad,
|V|=3,491·10⁻¹³ N und |M|=8,327·10⁻¹³ Nm vor. Der native Quelltext verwendet
floating-point `linsolve` und substituiert `float(ans)`; die Reaktionsausgabe
rundet dagegen auf zehn Nachkommastellen. Dieser nachvollziehbare numerische
Rest erklärt die künstlichen Ortsunterschiede: wMin@6 m, wMax@1,443375673 m,
SpanDrafts Nullverlauf-Repräsentant@0 m. Eine numerisch kleine Funktion ist
kein symbolisch nachgewiesenes Nullplateau.

**NOT APPLICABLE sind ausschließlich** `wMin.position`, `wMax.position`,
`VMin.position`, `VMax.position`, `MMin.position`, `MMax.position` von V14
gegen IndeterminateBeam 2.4.0. Alle sechs Extremwerte werden unverändert
gegen die nativen Referenzwerte mit den bestehenden Toleranzen geprüft.
Die IB-Abnahme umfasst **70 Pflichtvergleiche**: 60 Feldwerte, vier Reaktionen
und sechs Extremwerte; alle PASS. PyCBA bleibt mit **63 Vergleichen**,
einschließlich sechs Positionsprüfungen gegen seine nativen Nullplateaus,
vollständig anwendbar und PASS. Es wird keine Entscheidung durch
Solvermehrheit getroffen.

Die .NET-Validierung speichert N/A separat von Fehlern; N/A erhöht weder den
Zahlenvergleichszähler noch erzeugt es PARTIAL. Die Ausnahme ist ausdrücklich
an V14, dessen Eingabe-SHA-256, IB 2.4.0 und die untersuchte Einschränkungsnotiz
gebunden. Unbekannte PARTIAL-Ursachen bleiben offen; fehlende Werte,
Provenienzfehler oder nicht bestandene Extremwertvergleiche bleiben FAIL.
Neun zusätzliche Guard-Tests sichern insbesondere alle sechs weiterhin
verpflichtenden Extremwerte und die Begrenzung der Ausnahme ab.

Der unveränderte historische Golden-/Adapterstatus `PARTIAL` bezeichnet bei
V14 ausschließlich diese sechs Ortsgrenzen. Er bleibt als ursprüngliche
Extraktionsevidenz erhalten; der abschließende Vergleichs- und Acceptance-Status
bewertet alle tatsächlich anwendbaren Pflichtvergleiche und ist **PASS**.
Es gibt keinen offenen fachlichen oder numerischen Abnahmepunkt mehr.

Fehlerberichte enthalten Case-ID, Größe, Position, Seite, SpanDraft- und
Referenzwert, absolute/relative Abweichung, absTol/relTol und Referenzsolver.
Relative Abweichung bei Referenz null ist JSON null. N/A-Berichte enthalten
separat Größe, Status und ausdrückliche Begründung; keine erfundene numerische
Ortsabweichung wird dafür angegeben.

## Acceptance-Matrix

`NOT APPLICABLE` bei Analytical heißt: kein geplanter geschlossener
Referenzanbieter für diesen Fall. Verfügbarkeitsgrenzen gelten pro Größe gemäß
obiger Tabelle. V14 besitzt zusätzlich genau sechs ausdrücklich begründete
N/A-Positionsvergleiche gegen IB; alle anwendbaren Pflichtvergleiche bestehen.
`SpanDraft tests` umfasst die separaten Gleichgewichts-, Feld-
und Metamorphieprüfungen des Falles; die Referenzabnahme steht in den
Referenzspalten. Die damaligen 200 Produkttests bestanden insgesamt.

| Case | Analytical | IndeterminateBeam | PyCBA | Equilibrium | SpanDraft tests | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V01 | PASS | PASS | PASS | PASS | PASS | PASS | Mittige Kraft; beidseitiger V-Sprung |
| V02 | PASS | PASS | PASS | PASS | PASS | PASS | Kragarm-Endkraft |
| V03 | PASS | PASS | PASS | PASS | PASS | PASS | Kragarm-UDL |
| V04 | PASS | PASS | PASS | PASS | PASS | PASS | UDL; nichtnodale Extremstelle |
| V05 | PASS | PASS | PASS | PASS | PASS | PASS | Einseitiger Momentsprung |
| V06 | PASS | PASS | PASS | PASS | PASS | PASS | Unsymmetrische Kraft |
| V07 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Überhang-Endkraft |
| V08 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Zwei freie Enden, kombinierte Lasten |
| V09 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Drei Lager, Einzelkraft |
| V10 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Nur erstes Feld belastet |
| V11 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Vier Lager, mehrere Lasten |
| V12 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Zwei Einspannungen; Adapterbefund behoben |
| V13 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Überlappende UDLs; Plateaugrenzseite geprüft |
| V14 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Ausschließlich sechs IB-Positionsvergleiche N/A; alle Extremwerte Pflicht und PASS |
| V15 | NOT APPLICABLE | PASS | PASS | PASS | PASS | PASS | Asymmetrisches allgemeines System |
| V04-S01 | PASS | PASS | PASS | PASS | PASS | PASS | L=0,1 m |
| V04-S02 | PASS | PASS | PASS | PASS | PASS | PASS | L=1 m, E=70 GPa |
| V04-S03 | PASS | PASS | PASS | PASS | PASS | PASS | L=10 m |

## Reproduktion, Golden Files und Abschlussprüfung

Die Befehle für Builds, Tests, Referenzregeneration und Vergleich stehen in
[validation/README.md](../validation/README.md). Neue Prüfläufe schreiben in
eigene, ignorierte Ausgabeverzeichnisse; die eingecheckten Berichte bleiben als
Evidenz der dokumentierten Abnahme erhalten.

Jedes Generierungsverzeichnis muss neu sein. Die Generierung schreibt niemals
direkt nach `references/` und überschreibt keine Datei. Referenzen können nur
durch den gesonderten, ausdrücklich mit `--reviewed` aufgerufenen
`promote_references.py --from …` übernommen werden. Änderungen sind im Git-Diff
sichtbar; Tests und CI nehmen keine automatische Aktualisierung vor. Vor einer
Übernahme sind Modell, Vorzeichen, Grenzen, Versionen, Konvergenz und Reports zu
prüfen. Auch historische PARTIAL-Extraktionsreferenzen bleiben als Evidenz erhalten;
dokumentiertes N/A wird erst in der Vergleichsschicht bewertet.

Die **45** Golden Files (36 extern, 9 analytisch) enthalten Provenienz mit
Case-ID, Version, CPython-Version, UTC-Erzeugungszeit, Theorie, Optionen,
Vorzeichenabbildung, Adapterversion, Eingabe-, Lock- und Adapterquelltexthash.
Eine spätere Änderung eines Referenzskripts in `validation/python/` erfordert
eine bewusste Regeneration; fehlende/veraltete Dateien scheitern an .NET-Provenienzprüfungen.
Bei der dokumentierten Reproduktion stimmten alle 45 Dateien nach Entfernung
ausschließlich des Erzeugungszeitpunkts **exakt** mit den Golden Files überein.
[`reproduction.json`](../validation/results/reproduction.json) enthält
die 45 semantischen Hashes und die direkte analytische Kalibrierung.

Der Referenzgenerator endete mit Exitcode **1**, weil er die historische
V14-Extraktionsmarkierung PARTIAL reproduzierte. Alle 45 Dateien wurden vollständig
erzeugt; der Reproduktionsaudit endete mit Exitcode **0** und ohne Datenunterschiede
außer Zeitstempeln. CLI-Vergleich und beide vollständigen Testläufe endeten ebenfalls
mit Exitcode **0**. Beide Solutions wurden mit **0 Warnungen und 0 Fehlern** gebaut.
Die Builds verwendeten `-m:1 -p:UseSharedCompilation=false`; die dokumentierten
Testläufe verwendeten die daraus erzeugten Artefakte.

| Abschlussnachweis vom 03.10.2026 | Tatsächliches Ergebnis |
| --- | --- |
| Haupt-Solution Restore / Build | PASS |
| Validation-Solution Restore / Build | PASS |
| Bestehende .NET-Tests | 200 / 200 PASS, 0 übersprungen |
| Separate .NET-Validation-Tests | 195 / 195 PASS, 0 übersprungen |
| Gesamte .NET-Testanzahl | 395 / 395 PASS, 0 FAIL, 0 übersprungen |
| Analytische Fallvergleiche | 9 Fälle, 639 einzelne Zahlen-/Ortsvergleiche PASS |
| SpanDraft gegen externe Referenzen | 2837 anwendbare Zahlen-/Ortsvergleiche PASS; separat 6 IB-Positionsvergleiche NOT APPLICABLE |
| Direkte externe analytische Kalibrierung | 18 Anbieter-/Fallpaare, 909 Zahlenvergleiche PASS |
| Metamorphe Tests | 96 / 96 PASS, 8833 einzelne Vergleiche |
| Physikalische Vergleiche | 1163 / 1163 PASS |
| Externe Regeneration | 45 / 45 reproduziert; 0 Datenunterschiede außer Zeitstempeln |
| Acceptance-Gate | **PASS: alle 18 Fälle PASS** |
| Gesamtvergleiche im Acceptance-Lauf | **13472 / 13472 PASS**, separat 6 N/A |
| Gesamtvergleiche einschließlich direkter Referenzkalibrierung | **14381 / 14381 PASS**, separat 6 N/A |

Die Vergleichssummen sind disjunkt: 2837 externe + 639 analytische +
8833 metamorphe + 1163 physikalische Vergleiche = **13472** im Acceptance-Lauf.
Die zusätzliche direkte Kalibrierung umfasst **909** Vergleiche, insgesamt
also **14381**. Guard-Tests werden als Tests gezählt, nicht nochmals als
Acceptance-Zahlenvergleiche. Die sechs N/A-Positionen sind separat ausgewiesen.
Produktquellen und Golden Files wurden zusätzlich per SHA-256 gegen den Stand
vor dieser V14-Untersuchung geprüft: **keine Änderungen**.
