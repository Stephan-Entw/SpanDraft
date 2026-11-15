# SpanDraft – Solvervalidierung

Die Nachweise sind nach dem jeweils geprüften Solverstand getrennt:

1. [Historische externe V1-Abnahme](#1-historische-externe-v1-abnahme) vom 03.10.2026.
2. [M1/M2-Kernelnachweise](#2-m1m2-nachweise-des-neuen-kernels).
3. [M3-Produktintegration](#3-m3-produktintegration).
4. [Finale M4-Produktabnahme](#4-finale-m4-produktabnahme).
5. [Linear veränderliche Streckenlasten](#5-linear-veränderliche-streckenlasten-dev-6).

Der aktuelle Produktvertrag steht in [SOLVER.md](SOLVER.md), die Befehle zur
Reproduktion in [validation/README.md](../validation/README.md). Historische
Reports werden bei neuen Abnahmen nicht überschrieben. Testzahlen und
Plattformangaben gehören ausschließlich zu den jeweils ausgewiesenen Läufen.

## 1. Historische externe V1-Abnahme

Der gesamte folgende V1-Teil beschreibt den damaligen FE-Produktpfad und seine
externen Referenzen. Er ist Herkunftsevidenz, keine Abnahme des späteren Kernels.
Die damaligen Reports und Golden References bleiben unverändert erhalten.

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

### Zweck, Ausgangsbasis und Grenzen

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

### Trennung und versionierte Eingaben

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

### Referenzquellen und reproduzierbare Umgebung

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

### Vorzeichen und externe Adapter

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

### Sprungstellen, Extrema und PyCBA-Konvergenz

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

### Analytische Referenzen

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

### Toleranzmodell und physikalische Prüfungen

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

### Gefundene Abweichungen und Untersuchung

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

#### V14: Prüfung der nativen IndeterminateBeam-Felder

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

### Acceptance-Matrix

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

### Reproduktion, Golden Files und Abschlussprüfung

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


## 2. M1/M2-Nachweise des neuen Kernels

M1 implementierte und prüfte die numerische Kette des internen Kernels:
Binary64-Primitives, analytische Lastintegrale, reduzierte Mechanik, Daten- und
Residualschranken, verifizierte Zustandsfehler sowie Felder, Reaktionen und
SI-Ausgabe-Gate. Unabhängige rationale Oracles prüfen die Stufen einzeln;
ein Vergleich mit dem alten FE-Rechner allein wäre dafür nicht ausreichend.

Die [M2-Abnahmeakte](../validation/investigations/solver-rework/M2_ACCEPTANCE.md)
ordnet die festen Tests den Closure-Kriterien und Fehlerursachen zu. Sie enthält
die damalige Eingangsbaseline, Mutantenzuordnung und Referenzherkunft. Zu M2
gehörten auch der bestehende Katalog mit 18 Fällen und 45 Golden References über
einen damals internen Kerneleinstieg. Der öffentliche Produkteinstieg blieb
während dieser Abnahme noch der alte Solver. Diese zeitliche Aussage der Akte
bleibt erhalten; der interne Einstieg und der damalige CLI-Schalter sind seit M3
entfernt.

Die M2-Grenz-/Exakttests laufen heute in der vollständigen Product-Suite, nicht
im Acceptance-CLI: unter anderem mathematisches τ, konservative Ablehnung,
SI-Unterlauf, Zweierpotenz-Umskalierung, CAD-/6-nm-Modelle, benachbarte belastete
Ereignisse und UDL-Überlagerungen. Der separate `compare` prüft den festen
Validation-Katalog mit externen/analytischen, physikalischen und metamorphischen
Vergleichen. Beide Nachweisarten sind für die Gesamtfreigabe erforderlich.

Die [Investigations](../validation/investigations/solver-rework/README.md)
enthalten historische Architektur-/Prototypmessungen und Closure-Herkunft.
Sie sind keine aktuellen C#-Plattformläufe und werden nicht rückwirkend dazu
erklärt. Exakte Sollwerte und bestehende Toleranzen werden durch M4 nicht verändert.

## 3. M3-Produktintegration

Seit `ae6cc2b` läuft ausschließlich
`EulerBernoulliBeamSolver.Solve(BeamModel) → SolverKernel → BeamSolution`.
`b1a9b03` stellt zusätzlich die gemeinsame Stationskinematik sicher: w und θ
stimmen auf beiden Seiten bitgleich mit dem veröffentlichten Knotenergebnis
überein, einschließlich exakt null gesperrter Komponenten.

Strukturtests sichern die unveränderte öffentliche API, den einzigen
Produktpfad, die Core-/Kernelgrenze und den Ausschluss von Future-Feature-
Primitiven ab. Der eingefrorene FE-Rechner und der Machbarkeitsprototyp samt
Math.NET liegen ausschließlich im Testprojekt. Die öffentliche axiale
Nullsemantik und vorhandene gegenüber abwesenden Reaktionen werden ausdrücklich
geprüft. Produktketten-, Desktop- und Rendering-Tests prüfen die Weitergabe an
Analysis, Engineering, Reaktionsübersicht, Diagramme, Probe und Bericht.
Diese Integrationsnachweise ersetzen die unabhängige numerische Abnahme nicht.

## 4. Finale M4-Produktabnahme

Abnahme vom **11.11.2026**, nativ auf **macOS 27.2.1 ARM64**, .NET SDK
**10.0.403**, Runtime **10.0.14**. Der öffentliche Solverquellstand ist
[`b1a9b037661b7afd34ae14da6597cea512d1ab84`](https://github.com/Stephan-Entw/SpanDraft/commit/b1a9b037661b7afd34ae14da6597cea512d1ab84);
M4 verändert keine Core-/Solverquelle und keine numerische Assertion.

Der finale Report
[`acceptance-m4.json`](../validation/results/acceptance-m4.json) wurde am
**2026-11-11T21:16:59.3629130+00:00** durch einen frischen `compare` ohne
`--actual` erzeugt. Sein `entryPoint` ist `EulerBernoulliBeamSolver`;
sämtliche Lösungen laufen über den öffentlichen Produktpfad aus Abschnitt 3.
[`acceptance.json`](../validation/results/acceptance.json) bleibt unverändert die
historische V1-Abnahme. Der M4-Report wird nach dieser Abnahme nicht überschrieben.

**SolverSourceSha256:**

```text
cc92d41a80ecefdcc9b811870d7698b5f015abd00e89242a4418fa01fe59f4c6
```

Maßgeblich bleibt `Acceptance.SolverHash()`: Die `.cs`- und `.csproj`-Dateien
unter Core und Solver werden je Projekt ordinal sortiert, ohne bin/obj, und
byteweise in derselben Reihenfolge gehasht. Die Definition wurde nicht verändert.
Damit derselbe Commit auf jeder Plattform dieselben Bytes liefert, checkt
`.gitattributes` diese Dateien mit LF aus. Die Festlegung wurde nach M4 ergänzt
und ändert weder die eingecheckten Bytes noch den Hash.
Die CI-Baseline ist separat als `expected_hash` in
[ci.yml](../.github/workflows/ci.yml) festgeschrieben und stimmt bei M4 exakt mit
dem Report überein. CI erzeugt einen eigenen aktuellen Report und vergleicht
dessen Hash mit dieser Baseline; die temporäre Branch-Ausnahme ist entfernt.
Der gespeicherte M4-Report ist keine dauerhafte CI-Hashquelle.

Spätere Änderungen gehashter Quellen müssen erneut abgenommen und durch eine
explizite CI-Baselineänderung freigegeben werden. Die M4-Evidenz bleibt dabei
historisch erhalten. Eine Änderung gehashter Quellen während M4 erfordert vor
Abschluss erneut die vollständige Acceptance und die dazugehörige Hashfestlegung.

### Lokale Plattform- und Acceptance-Ergebnisse

Beide Solutions wurden separat restauriert und in Release mit **0 Warnungen
und 0 Fehlern** gebaut. Die vollständige Haupt-Suite lief mit den CI-Optionen
`--max-parallel-test-modules 2 --report-xunit-xml`; Avalonia-/Rendering-Isolation
blieb unverändert. Alle folgenden Pflichtbefehle endeten mit Exitcode 0:

| Prüfung | Ergebnis |
| --- | --- |
| Product einschließlich M2-Grenz-/Exakttests | 3916 / 3916 PASS |
| Desktop | 330 / 330 PASS |
| Rendering | 389 / 389 PASS |
| Tooling | 175 PASS, 1 plattformbedingt übersprungen |
| Separate Validation-Suite | 195 / 195 PASS, 0 übersprungen |
| Acceptance-`compare` | 18 / 18 Fälle PASS |
| Externe Vergleiche | 2837 PASS |
| Analytische Vergleiche | 639 PASS |
| Physikalische Vergleiche | 1163 PASS |
| Metamorphische Prüfungen | 96 Tests, 8833 Vergleiche PASS |
| Acceptance-Gesamtsumme | 13472 PASS, separat genau 6 V14-IB-Ortsvergleiche N/A |
| Generierte Icons / Third-party Notices | beide `--check` PASS; 25 Runtimepakete |
| Help mit eingecheckten Assets / mit Regeneration | beide PASS, DE/EN vollständig validiert |
| Release-Publish der Help | 72 Dateien vollständig und bytegleich kopiert |

Der einzige übersprungene Test ist
`ScreenshotTests.DemoRecentsRoundTripWithoutReadingTheExampleFilesOnWindows`:
Seine native Windows-Fixture benötigt absolute Windows-Pfade. Es ist kein
numerischer Test; auf Windows ist er Bestandteil der Tooling-Suite.
Die M2-Grenz-/Exakttests gehören zum Product-Lauf und nicht zu `compare`.

Das CI-Hashskript wurde lokal mit dem echten M4-Hash erfolgreich ausgeführt;
ein absichtlich abweichender Hash wurde mit Exitcode 1 abgewiesen. Dieser
negative Guard-Lauf ist ein erwarteter Fehlernachweis, keine fehlgeschlagene
Produktabnahme. Der nach dem Push ausgeführte CI-Lauf wird im Abschlussbericht
anhand des exakten Commit-SHA und seiner Job-URLs ausgewiesen.

Der erste Windows-CI-Lauf fand einen zeitabhängigen Desktop-Test außerhalb der
Numerik: `RestoredSetupDraftTakesPrecedenceOverStartupFileWithOrWithoutRestoredSession`
prüfte unveränderte Recovery-Bytes, während der reale Ein-Sekunden-Debounce einer
wiederhergestellten schmutzigen Sitzung bereits einen neuen UTC-Zeitstempel
schreiben konnte. Die Fixture verwendet jetzt den bestehenden steuerbaren
Delay und eine feste Uhr. Alle bisherigen Assertions bleiben bestehen;
zusätzlich wird die geplante Speicherung nach expliziter Freigabe geprüft.
Es wurden weder Produktcode noch Wartezeiten, Toleranzen oder N/A-Regeln geändert.
Die vollständigen Haupt- und Validation-Tests wurden danach erneut ausgeführt.

Die 45 Golden References, Eingabefälle, Vergleichsregeln und historischen
Reports wurden weder regeneriert noch verändert. Die beiden regenerierten
DE-/EN-Help-Biegemomentgrafiken zeigen im bestehenden Beispiel nun das exakte
Minimum 0 N·m statt des früheren FE-Rundungsrests. Der
[Investigations-Audit](../validation/investigations/solver-rework/README.md#m4-prüfung-der-abgelegten-evidenz)
erhält die verknüpften Rohdaten und Herkunftsnachweise.

## 5. Linear veränderliche Streckenlasten (DEV-6)

Die Streckenlast hat zwei Randintensitäten; gleiche Werte laufen über denselben
Pfad wie die frühere konstante Last. Der Nachweis ist vierfach getrennt:

- **Exakte rationale Referenz in der Product-Suite.** Das Orakel der Ausgabetests
  (`SolverOutputOracle`) enthält Rampenterme, Resultierende `l(q₁+q₂)/2` und erstes
  Moment als Brüche ohne Gleitkomma. Für Dreieck, Trapez über mehrere Lager,
  Vorzeichenwechsel, antisymmetrischen Kragarm, Lasten an Lager und Balkenende,
  Überlagerung, sehr kurze und sehr steile Lasten, Nulldurchgang genau auf und
  ein ulp neben einer Station sowie ungleiche Randwerte mit Rundung beim
  Zuschneiden werden alle Feldwerte, Reaktionen, Stückgrenzen und
  `VerifiedOutputBound` gegen die exakte Mechanik geprüft.
- **Exakte Wurzelisolation.** Die Kandidatenpositionen jedes Stücks werden mit einer
  unabhängigen Sturm-Folge über exakt interpolierten Feldpolynomen verglichen: kein
  übersehener und kein überzähliger innerer Kandidat für w (Grad 4 von θ), M und V.
  Wurzeln näher als 10⁻¹³ am Stückrand sind Randkandidaten. Bei fast vollständig
  auslöschender Interpolation am Zuschneiden verschiebt der Datenfehler eine Wurzel;
  der Wert am Kandidaten bleibt verifiziert.
- **Analytik und eingefrorene Referenz.** Geschlossene Formeln für Dreieck (`M_max`
  bei `L/√3`, `w_min` bei ξ* ≈ 0,5193, `V` an den Enden), Vorzeichenwechsel (Scheitel
  `V = −q₀L/12` in Feldmitte, `M` bei `(3 ∓ √3)L/6`) und antisymmetrischen
  Kragarm (Resultierende null, Einspannmoment `q l²/6`), dazu ein Quintik mit vier
  stationären Punkten, sowie der Vergleich mit dem eingefrorenen Prototyp und
  metamorphe Relationen (Superposition, Teilung, Spiegelung, Zweierpotenz-Einheiten
  bitgleich).
- **Unabhängige Validierung V16–V19** gegen exakte rationale Statik (neu in
  `analytical.py`), PyCBA-Trapezglieder und IndeterminateBeam. Die analytische Referenz
  stimmt in der Kalibrierung mit beiden externen Lösern überein. Zum Eingabeformat
  gehört das optionale Feld `lineLoads`; Eingaben und Hashes der älteren Fälle bleiben
  unverändert. Da sich der Quellhash der Python-Adapter in jeder Referenz
  niederschlägt, wurden alle Referenzen neu erzeugt; die numerischen Inhalte der 45
  älteren Dateien sind bis auf Zeitstempel und Adapterhash identisch.

Die Konstantlastfälle bleiben bitgleich: Der Export der 18 älteren Fälle vor und nach der
Umstellung unterscheidet sich nur in Zeitstempel und Solverquellhash.

**Nahe der Schwelle τ.** Zwei starre Lager im Abstand 2⁻²² (symmetrische
Dreiecksfolge ohne Netzmoment) liefern ein Gate von etwa 0,8 τ, im Abstand 2⁻²³ etwa
1,6 τ und werden abgelehnt. Das entspricht dem Verhalten der konstanten Last und ist
konservative Ablehnung, kein ausgelieferter Fehler über τ.

**Fehlerinjektion.** `validation/investigations/linear-load-mutants/mutate.py` baut je
Mutante einen Fehler in die Lastterme, baut neu und führt die Product-Suite aus.
Alle Mutanten werden von mindestens einem Test erkannt:

| Mutante | Fehler | Ergebnis |
| --- | --- | --- |
| R1 | ramp divisor inside the load, p = 4: 121 instead of 120 | killed (50 Tests) |
| R2 | ramp divisor beyond the load, p = 2: 7 instead of 6 | killed (35 Tests) |
| R3 | ramp divisor beyond the load, p = 3: 25 instead of 24 | killed (23 Tests) |
| R4 | ramp divisor beyond the load, p = 4: 121 instead of 120 | killed (29 Tests) |
| R5 | sign of the rise reversed | killed (67 Tests) |
| R6 | near and far swapped in the outer ramp polynomial, p = 2 | killed (33 Tests) |
| R7 | clip: interpolated intensity replaced by the start intensity | killed (34 Tests) |
| R8 | clip: data error of the interpolated intensity dropped | killed (2 Tests) |
| R9 | clip: load end intensity interpolated instead of taken exactly | killed (1 Tests) |
| R10 | ramp term omitted from the shear integral (reactions and V) | killed (56 Tests) |
| R11 | ramp term omitted from the moment integral | killed (65 Tests) |
| N1 | excitation norm: maximum of the end intensities instead of their mean | killed (15 Tests) |
| N2 | excitation: unloaded when only the start intensity is zero | killed (18 Tests) |
| N3 | exact excitation exponent: doubled norm not halved | killed (7 Tests) |
| N4 | exact excitation exponent: end intensity omitted | killed (5 Tests) |
| Q1 | piece majorant uses only the intensity at the piece start | killed (25 Tests) |
| Q2 | piece majorant without the data error of the intensity | killed (1 Tests) |
| E1 | shear polynomial: factor 1/2 of the intensity change missing | killed (22 Tests) |
| E2 | rotation polynomial: 12 instead of 24 for the intensity change | killed (15 Tests) |
| E3 | shear candidates ignore the intensity change | killed (13 Tests) |
| E4 | piece intensity change from a difference of end values with the wrong piece length | killed (4 Tests) |
| I1 | input order ignores the end intensity | killed (1 Tests) |

**Abnahme der Linearlasten.** Nach dem letzten Core-/Solver-Commit erzeugte ein frischer
`compare` ohne `--actual` den Report
[`acceptance-linear-loads.json`](../validation/results/acceptance-linear-loads.json)
(2026-11-14T11:32:10Z, macOS ARM64, `entryPoint` `EulerBernoulliBeamSolver`): 22 Fälle,
3540 externe und 1090 analytische Skalarvergleiche, 120 metamorphe Prüfungen,
`validated = true`. Die separate Validierungs-Suite bestand vollständig.

**SolverSourceSha256:**

```text
e65af778b7c5a42522a8bbaa86e6bbdb49d1a8d1d8202be000490f2ff39ea050
```

Dieser Hash ist die neue CI-Baseline in [ci.yml](../.github/workflows/ci.yml). Der Report
der M4-Abnahme bleibt als historische Evidenz unverändert und gehört zum Stand davor.

## 6. Federlager (DEV-19)

Ein Federlager hält axial starr und trägt eine Weg- und/oder Drehfeder, mit Drehfeder auch
mit starrer Querverschiebung (Teileinspannung). Der Nachweis ist fünffach getrennt:

- **Exakte rationale Referenz in der Product-Suite.** Das Orakel der Ausgabetests löst die
  globale Kraftgrößenmethode mit dem Federgesetz als weiterer exakter Verträglichkeitsbedingung
  (`w + R/k = 0`, `θ + C/k_θ = 0`). Die Abnahmematrix `SolverSpringAcceptanceTests` rechnet
  fünfzehn Konfigurationen (steife und weiche Feder zwischen und neben starren Lagern, nur Federn,
  Dreier- und Achter-Cluster, Last auf der Feder, Drehfeder neben der Einspannung, Weg- und
  Drehfeder zwischen starren Lagern, Teileinspannung, Drehfederpaare, gemischte Cluster) über die
  Abstände L/10 … L/10¹¹ und k·L³/EI beziehungsweise k_θ·L/EI von 10⁻³ bis 10¹⁵. Jedes dieser
  Modelle wird geliefert, und jeder Ausgabewert (Reaktionen, Felder an allen Stationsseiten und im
  Stück, Extrema) liegt innerhalb seiner verifizierten Schranke; es gibt keine Ablehnung. Dazu der
  eingefrorene Wert zweier steifer Federn im Abstand 6 nm.
- **Reaktionsidentität.** Sprung und Federgesetz umschließen für Weg- und Drehfedern in vierzehn
  Modellen die exakte Reaktion mit ihrer eigenen Schranke; geliefert wird der Wert mit der
  kleineren Schranke, bei Gleichheit der Sprung. Die Differenz beider Werte darf die Summe der
  Schranken nicht übersteigen, sonst `InternalInconsistency`.
- **Analytik.** Mittige Feder auf dem Einfeldträger (`w = −P f / (1 + k f)`, `f = L³/(48 EI)`),
  Starrkörperanteil bei zwei Endfedern, Zweifeldträger mit Mittelfeder und die Grenze k → ∞ gegen
  das starre Mittellager (relativer Abstand `1/(1 + k δ)` für k·L³/EI = 10⁻³ … 10¹²), Kragarm mit
  Teileinspannung (`w = −(P L³/(3 EI) + P L²/k_θ)`), zwei Endmomentenfedern unter Gleichlast
  (`M = q L²/12 / (1 + 2 EI/(k_θ L))`), Drehfeder im Feld und Balken nur auf Federn.
- **Unabhängige Validierung V20–V24.** Die analytische Referenz ist exakte rationale Statik
  (`analytical.py` mit Federgesetz) und stimmt in der Kalibrierung mit PyCBA und IndeterminateBeam überein.
  V20 (mittige Wegfeder), V21 (nur Endfedern) vergleichen beide externen Löser, V22 (Teileinspannung)
  und V23 (Weg- und Drehfeder) nur PyCBA, weil IndeterminateBeam keine Drehfeder besitzt. V24 hat bei
  k·L³/EI = 10¹² nur die analytische Referenz: PyCBA und IndeterminateBeam verlieren dort die für die
  Vergleichstoleranz 10⁻⁵ nötigen Stellen (dokumentierte Grenze nach A7). Zusätzlich gelten das
  Federgesetz `Ry = −k w`, `Rm = −k_θ θ` als physikalische Prüfung (für k·L³/EI ≤ 10⁸), skalierte
  Steifigkeiten in den Relationen E2/I2 und die metamorphe Relation `rigidLimit` (V24 gegen das gelenkige
  Lager, innerhalb des doppelten Ausgabevertrags 10⁻⁸ gegen die Feldskala, weil der physikalische
  Abstand `1/(1 + k f)` ≈ 5·10⁻¹¹ darunter liegt). Das Eingabeformat erhält optionale Felder
  (`translationalStiffness`, `rotationalStiffness`, `rigidTranslation`); Eingaben und Hashes der älteren
  Fälle bleiben unverändert. Weil sich der Quellhash der Python-Adapter in jeder Referenz niederschlägt,
  wurden alle Referenzen neu erzeugt; die numerischen Inhalte der 57 älteren Dateien sind bis auf
  Zeitstempel und Adapterhash identisch.
- **Gezielte Fehlerfälle.** Eine einzelne Feder ist für jede Steifigkeit ein Mechanismus
  (`UnstableModel`); Feder auf einem anderen Lager oder einer weiteren Feder ist `InvalidModel`
  (Core und Kernel); ein nicht normaler skalierter Koeffizient, auch durch eine subnormale Eingabe,
  ist `NumericalFailure` (`NumericalRange`). Federn weit weicher als der Nachweisbereich
  (k·L³/EI ≈ 2·10⁻⁸ bei zwei Endfedern) lehnt das Gate ab (`IllConditionedSystem`), statt sie falsch zu liefern.

**Fehlerinjektion.** `validation/investigations/spring-support-mutants/mutate.py` baut je Mutante
einen Fehler in die Federterme, baut neu und führt die Product-Suite aus. Alle 48 Mutanten werden
von mindestens einem Test erkannt. Zwei Mutanten überlebten zunächst (Bereichsprüfung der
subnormalen Steifigkeitseingabe, Koinzidenzmarke bei mehreren Lagern nach einer Feder); sie
führten zu je einem zusätzlichen Test. Eine weitere Prüfung des Core (starre Translation ohne
Drehfeder) war nicht erreichbar und wurde entfernt.

| Mutante | Fehler | Ergebnis |
| --- | --- | --- |
| S1 | spring term of the shear row: sign reversed | killed (78 Tests) |
| S2 | spring term of the shear row omitted | killed (79 Tests) |
| S3 | spring term placed in the rotation column | killed (77 Tests) |
| S4 | stiffness exponent 3 e_L + e_EI instead of 3 e_L - e_EI | killed (44 Tests) |
| S5 | stiffness exponent 2 e_L - e_EI | killed (54 Tests) |
| S6 | scaled stiffness not required to be normal | killed (2 Tests) |
| S7 | spring input not range-checked | killed (1 Tests) |
| R1 | reaction choice reversed: the larger bound is delivered | killed (20 Tests) |
| R2 | reaction choice: the law wins a tie | killed (2 Tests) |
| R3 | reaction choice: always the jump | killed (14 Tests) |
| R4 | reaction choice: always the spring law | killed (9 Tests) |
| R5 | identity check removed | killed (3 Tests) |
| R6 | identity check against the jump bound only | killed (2 Tests) |
| R7 | identity check against the law bound only | killed (26 Tests) |
| R8 | identity check with a rounded sum of the bounds | killed (1 Tests) |
| R9 | spring law without the minus sign | killed (95 Tests) |
| R10 | spring law without the state bound | killed (31 Tests) |
| R11 | spring law reads the rotation instead of the deflection | killed (64 Tests) |
| R12 | spring law with the unscaled stiffness exponent | killed (65 Tests) |
| R13 | spring reactions not selected between jump and law | killed (9 Tests) |
| R14 | no reaction reported for a spring | killed (54 Tests) |
| Q1 | rotational stiffness exponent e_L + e_EI | killed (31 Tests) |
| Q2 | rotational stiffness exponent 3 e_L - e_EI | killed (39 Tests) |
| Q3 | rotational spring term of the moment row: sign reversed | killed (50 Tests) |
| Q4 | rotational spring term of the moment row omitted | killed (51 Tests) |
| Q5 | rotational spring term placed in the deflection column | killed (51 Tests) |
| Q6 | rotational spring law reads the deflection | killed (42 Tests) |
| Q7 | rotational reaction not selected between jump and law | killed (5 Tests) |
| Q8 | no moment reaction reported for a rotational spring | killed (35 Tests) |
| Q9 | rotational spring station not marked as a moment jump | killed (1 Tests) |
| Q10 | rotational spring input not range-checked | killed (1 Tests) |
| Q11 | adapter: rotational stiffness dropped | killed (29 Tests) |
| Q12 | adapter: rigid translation dropped | killed (16 Tests) |
| Q13 | rigid translation of a spring station does not restrain w | killed (24 Tests) |
| K1 | spring station not marked as a reaction jump | killed (1 Tests) |
| T1 | rank: a translational spring does not count | killed (36 Tests) |
| T2 | rank: a rotational spring does not count | killed (19 Tests) |
| T3 | spring support is not axially rigid | killed (70 Tests) |
| T4 | spring coincident with a rigid support accepted by the kernel | killed (4 Tests) |
| T5 | spring stiffness lost at the mechanical station | killed (104 Tests) |
| T6 | rigid translation accepted together with a translational spring | killed (1 Tests) |
| A1 | adapter: translational stiffness dropped | killed (41 Tests) |
| A2 | adapter: springs treated as rollers | killed (61 Tests) |
| V1 | validator: a spring after a rigid support is accepted | killed (5 Tests) |
| V2 | validator: duplicate check also applies to springs | killed (2 Tests) |
| V3 | validator: coincidence flag of a position forgotten | killed (1 Tests) |
| C1 | Core: rigid translation accepted together with a translational spring | killed (4 Tests) |
| C2 | Core: no spring at all accepted | killed (4 Tests) |

**Abnahme der Federlager.** Nach dem letzten Core-/Solver-Commit erzeugte ein frischer
`compare` ohne `--actual` den Report
[`acceptance-spring-supports.json`](../validation/results/acceptance-spring-supports.json)
(2026-11-14T13:09:12Z, macOS ARM64, `entryPoint` `EulerBernoulliBeamSolver`): 27 Fälle,
3979 externe und 1555 analytische Skalarvergleiche, 151 metamorphe Prüfungen,
`validated = true`. Die Product-Suite (5473 Tests, einer übersprungen) und die separate
Validierungs-Suite (304 Tests) bestanden vollständig.

**SolverSourceSha256:**

```text
4ef3168745739779f580ccee60998e85e1dbe37f678c7368a8e7e44aeb39d48e
```

Dieser Hash war die CI-Baseline nach den Federlagern. Der Report der
Linearlast-Abnahme und der M4-Abnahme bleiben als historische Evidenz unverändert.

## 7. Mehrsegment-Balken (DEV-11)

Ein Balken aus mehreren Abschnitten hat je Abschnitt ein eigenes EI; jede Grenze ist im Kernel eine
Station ohne Sperren. Der Nachweis ist fünffach getrennt:

- **Exakte rationale Referenz in der Product-Suite.** Das Orakel der Ausgabetests integriert die
  Verträglichkeit abschnittsweise (`θ = θ₀ + Σ ∫ M/EI`, `w` entsprechend) und löst weiter die
  globale Kraftgrößenmethode. `SolverMultiSectionExactTests` rechnet neun Konfigurationen (Stufenkragarm,
  Grenze auf dem Mittellager, drei Abschnitte mit Punktkraft, Punktmoment und Linienlast auf den Grenzen,
  Grenze auf Wegfeder, Grenze auf Teileinspannung, Grenze auf Lager und Last, ein 1 mm breiter
  Abschnitt, EI-Kontraste 10³ und 10⁶). Jedes Modell wird geliefert, und jeder Ausgabewert
  (Reaktionen, Felder an allen Stationsseiten und im Stück, Extrema, Skalen) liegt innerhalb seiner
  verifizierten Schranke; das Gate und τ = 10⁻⁸ sind unverändert.
- **Analytik über die öffentliche Schnittstelle.** Stufenkragarm (Einheitslastintegral
  `w = −P ∫ (L−x)²/EI dx`), Zweifeldträger mit Steifigkeitswechsel auf dem Mittellager
  (Dreimomentengleichung), Stufenwelle unter Mittellast, Kragarme beidseits einer Einspannung auf der
  Grenze und Federlager auf der Grenze (`w = −P f / (1 + k f)` mit der gestuften Nachgiebigkeit `f`).
- **Invarianten.** Teilung bei gleichen Eigenschaften an acht Positionen (auch auf Lager, Feder und
  Last) ändert die Ergebnisse nur innerhalb der Schranken; gleiche Produkte `E·I` aus verschiedenem
  E und I sind bitgleich; M, V, w und θ sind an einer lastfreien Grenze stetig; Spiegelung; Skalierung
  von EI skaliert nur w und θ; jede Grenze fügt genau vier Unbekannte hinzu; Vergleich mit dem
  eingefrorenen Prototyp (EI-Liste) in vier Modellen.
- **Unabhängige Validierung V25–V29.** Das Eingabeformat erhält die optionale Liste `segments` (Schema
  2); Fälle mit Schema 1 und ihre Hashes bleiben unverändert. Die analytische Referenz integriert die
  Krümmung mit dem EI jedes Intervalls (exakte rationale Statik); PyCBA nimmt je Intervall ein Element mit
  eigenem EI. IndeterminateBeam besitzt ein E und ein I für den ganzen Balken und fehlt für diese Fälle.
  V25 (Stufenkragarm, drei Abschnitte), V26 (Zweifeldträger, E und I wechseln auf dem Mittellager),
  V27 (Stufenwelle mit Grenzen auf zwei Punktkräften), V28 (Grenzen auf Wegfeder und Teileinspannung),
  V29 (EI-Verhältnis 10⁵ zwischen zwei Abschnitten). Die Grenzen sind Ereignisse: Sprung- und
  Stetigkeitsprüfungen laufen dort automatisch. Weil der Quellhash der Python-Adapter in jeder Referenz
  steht, wurden alle Referenzen neu erzeugt; die numerischen Inhalte der 68 älteren Dateien sind bis auf
  Zeitstempel und Adapterhash identisch.
- **Gezielte Fehlerfälle.** Abschnitte, die den Balken nicht exakt überdecken, sind `InvalidModel`
  (Validator `SegmentsNotContiguous`, Kernel `InvalidMechanicalModelException`); eine Grenze macht
  keinen Starrkörpermodus stabil (`UnstableModel`); ein normiertes EI, das nicht mehr normal ist
  (Verhältnis über etwa 10³⁰⁰), ist `NumericalFailure` (`NumericalRange`).

**Fehlerinjektion.** `validation/investigations/multi-segment-mutants/mutate.py` baut je Mutante einen
Fehler in die Abschnittslogik, baut neu und führt die Product-Suite aus.
Von 38 Mutanten werden 36 von mindestens einem Test erkannt. Beim ersten Lauf überlebten sieben (E5, E8, E9, E10,
S6, D6 und eine Tie-Break-Mutante des maßgebenden Abschnitts); E5, E8, E9 und E10 führten zu zusätzlichen Tests
(Stückpolynom mit dem EI des falschen Abschnitts, Skala mit dem weichsten EI, Gewichte der Verifikation, Normierung am
weichsten statt am steifsten Abschnitt). Die Tie-Break-Mutante war äquivalent, weil spätere Abschnitte rechts liegen:
die tote Positionsprüfung wurde entfernt und die Mutante gestrichen. S2 baute zunächst nicht und wurde korrigiert.
Zwei Mutanten bleiben äquivalent: S6 entfernt eine Schutzprüfung, die Stationen- und Überdeckungsregeln bereits
garantieren, D6 den Vergleich der Abschnittsanzahl, die sich immer auch in einem Abschnittsbereich zeigt.

| Mutante | Fehler | Ergebnis |
| --- | --- | --- |
| E1 | transfer coefficients of an interval use the stiffness of the previous interval | killed (28 Tests) |
| E2 | load integrals are divided by the stiffness of the first interval | killed (16 Tests) |
| E3 | field evaluation uses the stiffness of the first interval | killed (16 Tests) |
| E4 | field evaluation uses the stiffness of the next interval | killed (16 Tests) |
| E5 | piece polynomials use the stiffness of the first interval | killed (2 Tests) |
| E6 | piece quality uses the stiffness of the first interval | killed (4 Tests) |
| E7 | upper stiffness reference scale from the first interval only | killed (3 Tests) |
| E8 | lower stiffness reference scale from the first interval only | killed (7 Tests) |
| E9 | verification weights from the greatest instead of the least stiffness | killed (1 Tests) |
| E10 | normalization follows the softest instead of the stiffest section | killed (1 Tests) |
| S1 | an interval starting on a boundary is attributed to the section on its left | killed (60 Tests) |
| S2 | boundaries are not mechanical stations | killed (44 Tests) |
| S3 | the tiling of the sections is not checked | killed (1 Tests) |
| S4 | a boundary counts as an occupied position, so a spring cannot stand on it | killed (8 Tests) |
| S5 | a section of zero or negative length is accepted | killed (1 Tests) |
| S6 | the straddle check of an interval is dropped (defensive check; expected equivalent) | survived, equivalent (defensive check that the tiling and station rules already guarantee) |
| X1 | section extrema are found on all pieces from the section start on | killed (1 Tests) |
| X2 | every section reports the global extrema | killed (4 Tests) |
| A1 | adapter: the stiffness of the first segment for every section | killed (16 Tests) |
| A2 | adapter: the modulus of the first segment only | killed (2 Tests) |
| G1 | engineering: the segment with the largest safety factor governs | killed (3 Tests) |
| G3 | engineering: every segment uses the strength of the first one | killed (2 Tests) |
| G4 | engineering: every segment uses the section modulus of the first one | killed (1 Tests) |
| G5 | engineering: the segment moment is the global one | killed (2 Tests) |
| V1 | validator: gaps and overlaps between segments are accepted | killed (3 Tests) |
| V2 | validator: a last segment that ends elsewhere than the beam is accepted | killed (2 Tests) |
| C1 | self-weight of a segment starts at the beam start | killed (2 Tests) |
| D1 | document: the end of a segment is its own start boundary | killed (12 Tests) |
| D2 | document: boundaries need not ascend or lie inside the beam | killed (2 Tests) |
| D3 | document: self-weight needs the density of the first segment only | killed (2 Tests) |
| D4 | document: a boundary belongs to the segment that ends there | killed (1 Tests) |
| D5 | document: splitting does not copy the segment | killed (1 Tests) |
| D6 | comparer: the segment count is not compared | survived, equivalent (a changed count always changes a segment range, which is compared) |
| D7 | comparer: segment ranges are not compared | killed (1 Tests) |
| D8 | content equality ignores the boundaries | killed (1 Tests) |
| P1 | format: a further segment is written with the first boundary | killed (3 Tests) |
| P2 | format: further segments are not read | killed (11 Tests) |
| P3 | format: strength confirmation is checked for the first segment only | killed (1 Tests) |

**Abnahme der Mehrsegment-Balken.** Nach dem letzten Core-/Solver-Commit erzeugte ein frischer
`compare` ohne `--actual` den Report
[`acceptance-multi-segment.json`](../validation/results/acceptance-multi-segment.json)
(2026-11-14T18:03:52Z, macOS ARM64, `entryPoint` `EulerBernoulliBeamSolver`): 32 Fälle,
4390 externe und 2141 analytische Skalarvergleiche, 180 metamorphe Prüfungen,
`validated = true`. Die Product-Suite (5614 Tests, einer übersprungen) und die separate
Validierungs-Suite (353 Tests) bestanden vollständig. Die Abnahme auf Windows steht für die CI aus.

**SolverSourceSha256:**

```text
2fba8f23fe250bc359083be10150f312efaf644b00e5b0ec04719215275e15a2
```

Dieser Hash ist die neue CI-Baseline in [ci.yml](../.github/workflows/ci.yml). Die Reports der
Federlager-, Linearlast- und M4-Abnahme bleiben als historische Evidenz unverändert.

## 8. Innere Gelenke (DEV-20)

Ein inneres Gelenk (`MomentRelease`) setzt M = 0 auf beiden Seiten und lässt θ springen. Der Nachweis ist fünffach getrennt:

- **Exakte rationale Referenz in der Product-Suite.** Das Orakel der Ausgabetests erhält je Gelenk die
  Rotationssprung-Unbekannte und die Bedingung M(h⁻) = 0 (`θ` springt um Δθ, `w` bleibt stetig). Die
  Konfigurationen von `SolverMomentReleaseKernelTests` (Gerber-Träger, Gelenk über Fest- und Loslager,
  Einspannung–Gelenk–Loslager, Zwischenfeld zwischen zwei Einspannungen, Gelenk auf Wegfeder, auf und neben
  Abschnittsgrenzen, Trapezlast über das Gelenk, Kraft auf dem Gelenk, zwei Gelenke bei drei Lagern) und die
  Nahfälle (Gelenk 10⁻³ … 10⁻¹¹ neben dem Lager, ein Bit neben dem Lager, Kraft 6 nm neben dem Gelenk, zwei
  Gelenke 6 nm auseinander) werden geliefert, und jeder Ausgabewert (Reaktionen, Felder an allen Stationsseiten
  und im Stück, Extrema) liegt innerhalb seiner verifizierten Schranke; Gate und τ = 10⁻⁸ sind unverändert.
- **Analytik und eingefrorener Prototyp.** Gerber-Träger (R_C = 5 kN, V_G = 5 kN, R_B = 7,5 kN, R_A = −2,5 kN,
  M_B = −10 kNm, Rotationen und Durchbiegung am Gelenk aus den Einzelfeldern), Gelenk über einem Lager als zwei
  Einfeldträger, Einspannung–Gelenk–Loslager, Zwischenfeld zwischen zwei Einspannungen, Gelenk an einer
  Nullstelle von M (ändert M, V, w nicht, θ springt dort nicht). `SolverMomentReleaseTests` vergleicht
  Knoten, Felder an beiden Seiten und Extrema in zwölf Modellen mit dem eingefrorenen Prototyp
  (`ReferenceBeamSolver.Prototype` mit `SolverLoads.Hinges`).
- **Mechanismen exakt.** Die Teilkörper-Rangprüfung wird in einem Differentialtest über 4000 Zufallskonfigurationen
  (seed-fest, gitterartige und freie Positionen, Fest-, Los- und Einspannungen, Weg-, Dreh- und Teileinspannungsfedern)
  gegen die unabhängige rationale Gauß-Elimination `DofMap.IsKinematicallyStable` des eingefrorenen Prototyps
  verglichen (je mehr als 200 stabile und instabile Fälle), dazu eine Tabelle benannter Fälle (Kragarm mit Gelenk,
  Gelenk zwischen zwei Festlagern, Pinned–Roller–Roller mit einem und zwei Gelenken, Gelenk ein Bit neben dem Lager).
  Ein Mechanismus ist `UnstableModel` und hat Vorrang vor numerischen Bereichsfehlern des EI.
- **Unabhängige Validierung V30–V37.** Das Eingabeformat erhält die optionale Liste `momentReleases`; Fälle
  mit Schema 1 und 2 und ihre Hashes bleiben unverändert. Die analytische Referenz fügt je Gelenk die
  Rotationssprung-Unbekannte und die Bedingung M(h⁻) = 0 zur exakten rationalen Statik hinzu; PyCBA bildet ein
  Gelenk als gelenkiges rechtes Ende (Elementtyp 2) des linken Elements und liest die linke Rotation aus dem
  Elementfeld. IndeterminateBeam kennt keine inneren Gelenke und fehlt für diese Fälle. V30 (Gerber-Träger),
  V31 (Gelenk auf dem Mittellager), V32 (Einspannung, Gelenk, Trapezlast), V33 (Zwischenfeld zwischen zwei
  Einspannungen), V34 (Gelenk auf einer Wegfeder), V35 (Gelenk auf einem Querschnittswechsel mit
  Einzelkraft auf dem Gelenk), V36 (Gelenk 4 mm neben dem Mittellager mit Lasten nahe am Gelenk), V37
  (statisch unbestimmt). Die physikalische Prüfung ersetzt am Gelenk die θ-Stetigkeit durch M = 0 auf
  beiden Seiten und die Übereinstimmung von `RotationRadians` und `RotationLeftRadians` mit den Seiten von
  `EvaluateAt`. Weil der Quellhash der Python-Adapter in jeder Referenz steht, wurden alle Referenzen neu
  erzeugt; die numerischen Inhalte der 78 älteren Dateien sind bis auf Zeitstempel und Adapterhash identisch.
- **Gezielte Fehlerfälle.** Gelenk an den Balkenenden, doppelt, an Einspannung, Drehfeder und Punktmoment
  ist `InvalidModel` (Validator und Kernel); ein Mechanismus ist `UnstableModel` mit eigener Meldung; ein
  berechnetes Moment links des Gelenks außerhalb seiner Schranke um null ist `InternalInconsistency`.

**Fehlerinjektion.** `validation/investigations/moment-release-mutants/mutate.py` baut je Mutante einen Fehler
in die Gelenklogik (Validator, Stationsmodell, Rang, Gleichungssystem, Felder, Adapter, Fassade, Editorzustand,
Format, Übersicht), baut neu und führt zuerst die Test-Klassen des Features, bei Überleben die gesamte
Product-Suite aus. Von 69 Mutanten werden 68 von mindestens einem Test erkannt. Beim ersten Lauf überlebten
fünf (R8, R9, F1, E5, E6); R8 (Gelenk auf der einzigen Axiallagerung), F1 (exakte Null des linken Moments bei
nicht verschwindendem Rechenwert), E5 (Bearbeiten ohne Positionsänderung) und E6 (Punktkraft am Gelenk) führten zu
zusätzlichen Tests. R9 ist äquivalent: Die Teilkörper bilden eine Kette, ein Vorzeichen der Stetigkeitszeile
lässt sich durch eine Spaltenvorzeichen-Umkehr der Teilkörper aufnehmen, der Rang hängt nicht davon ab.

| Mutante | Fehler | Ergebnis |
| --- | --- | --- |
| V1 | validator: a release at the beam start is accepted | killed (6 Tests) |
| V2 | validator: a release at the beam end is accepted | killed (5 Tests) |
| V3 | validator: duplicate releases are accepted | killed (4 Tests) |
| V4 | validator: a release on a fixed support is accepted | killed (6 Tests) |
| V5 | validator: a release on a rotational spring is accepted | killed (4 Tests) |
| V6 | validator: a release on a point moment is accepted | killed (6 Tests) |
| V7 | validator: a release on a point force is rejected as a point moment | killed (12 Tests) |
| V8 | validator: only the first spring counts for the rotational spring rule | killed (4 Tests) |
| M1 | model: releases are dropped by the segment constructor | killed (84 Tests) |
| S1 | station: a release outside the interior is accepted | killed (6 Tests) |
| S2 | station: duplicate releases are accepted | killed (87 Tests) |
| S3 | station: a release on an applied moment is accepted | killed (2 Tests) |
| S4 | station: a release on a rigid rotation is accepted | killed (2 Tests) |
| S5 | station: a release on a rotational spring is accepted | killed (3 Tests) |
| S6 | station: a release is no mechanical station of its own | killed (102 Tests) |
| S7 | station: mechanical stations never carry the release flag | killed (82 Tests) |
| S8 | station: event stations never carry the release flag | killed (26 Tests) |
| R1 | rank: the continuity row of a release is missing | killed (71 Tests) |
| R2 | rank: a support at a release constrains only the left body | killed (94 Tests) |
| R3 | rank: a support at a release constrains only the right body | killed (115 Tests) |
| R4 | rank: the bending modes stay two whatever the number of bodies | killed (85 Tests) |
| R5 | rank: the continuity row compares the wrong bodies | killed (6 Tests) |
| R6 | rank: a rotational restraint is ignored for the rank | killed (47 Tests) |
| R7 | rank: a translational restraint is ignored for the rank | killed (134 Tests) |
| R8 | rank: the release changes the axial mode | killed (2 Tests) |
| R9 | rank: the continuity row has the wrong sign (the two bodies become the same line) | survived, equivalent (the continuity sign can be absorbed by a column sign flip of every second body; the bodies form a chain, so the rank cannot depend on it) |
| B1 | system: the moment right of a release stays an unknown | killed (68 Tests) |
| B2 | system: the rotation continuity row stays at a release | killed (68 Tests) |
| F1 | fields: the left moment of a release is the computed value, not zero | killed (1 Tests) |
| F2 | fields: the rotation is shared across a release | killed (51 Tests) |
| F3 | fields: the self-test of the exact zero is dropped | killed (1 Tests) |
| F4 | fields: the self-test of the exact zero accepts any value | killed (67 Tests) |
| F5 | fields: the exact zero carries no error bound | killed (31 Tests) |
| K1 | kernel: a release station reports no release jump flag | killed (25 Tests) |
| A1 | adapter: releases are not mapped | killed (51 Tests) |
| A2 | adapter: the mechanism check of an unrepresentable stiffness ignores the releases | killed (1 Tests) |
| A3 | facade: no node carries the left rotation | killed (23 Tests) |
| A4 | facade: the left rotation is the right one | killed (18 Tests) |
| A5 | facade: every node with a left limit carries a left rotation | killed (16 Tests) |
| A6 | facade: the mechanism message ignores the releases | killed (1 Tests) |
| D1 | document: releases are no named entities | killed (8 Tests) |
| D2 | document: releases do not reach the beam model | killed (36 Tests) |
| D3 | comparer: releases are not compared | killed (6 Tests) |
| D4 | content equality ignores the releases | killed (2 Tests) |
| D5 | constraints: a release at the new end is allowed | killed (2 Tests) |
| D6 | constraints: the length error ignores the releases | killed (2 Tests) |
| D7 | document: a release name may repeat a support name (names are not shared) | killed (8 Tests) |
| N1 | naming: the release counter is not consumed | killed (3 Tests) |
| N2 | naming: the release name is formatted like a force | killed (5 Tests) |
| N3 | naming: the release counter may move backwards | killed (1 Tests) |
| E1 | rules: a duplicate release is no conflict | killed (3 Tests) |
| E2 | rules: a fixed support is no conflict | killed (2 Tests) |
| E3 | rules: a rotational spring is no conflict | killed (1 Tests) |
| E4 | rules: a point moment is no conflict | killed (2 Tests) |
| E5 | rules: the edited release conflicts with itself | killed (1 Tests) |
| E6 | rules: a point force counts as a point moment | killed (1 Tests) |
| E7 | rules: a support at a release is never refused | killed (2 Tests) |
| E8 | rules: a pinned support at a release is refused too | killed (1 Tests) |
| E9 | editor: the support draft ignores the release rule | killed (1 Tests) |
| E10 | editor: the moment draft ignores the release rule | killed (1 Tests) |
| E11 | editor: a support placement ignores the release rule | killed (2 Tests) |
| P1 | format: releases are not written | killed (3 Tests) |
| P2 | format: releases are not read | killed (9 Tests) |
| P3 | format: the release counter is not written | killed (4 Tests) |
| P4 | format: the release counter is not read | killed (5 Tests) |
| P5 | format: the release counter is written even at its start value | killed (1 Tests) |
| P6 | format: a release id is not registered as an entity | killed (8 Tests) |
| O1 | overview: the left rotation shows the right one | killed (1 Tests) |
| O2 | overview: the shear cell shows the left value only | killed (1 Tests) |

**Abnahme der inneren Gelenke.** Nach dem letzten Core-/Solver-Commit erzeugte ein frischer
`compare` ohne `--actual` den Report
[`acceptance-moment-releases.json`](../validation/results/acceptance-moment-releases.json)
(2026-11-14T21:26:00Z, macOS ARM64, `entryPoint` `EulerBernoulliBeamSolver`): 40 Fälle,
5088 externe und 3137 analytische Skalarvergleiche, 228 metamorphe Prüfungen,
`validated = true`. Die Product-Suite (5843 Tests, einer übersprungen) und die separate
Validierungs-Suite (433 Tests) bestanden vollständig. Die Abnahme auf Windows steht für die CI aus.

**SolverSourceSha256:**

```text
c22a5e106b68b71d71cabd3078de4230d09b67a587220d72df43526b688df33c
```

Dieser Hash ist die neue CI-Baseline in [ci.yml](../.github/workflows/ci.yml). Die Reports der
Mehrsegment-, Federlager-, Linearlast- und M4-Abnahme bleiben als historische Evidenz unverändert.

**Abnahme nach den Review-Korrekturen.** Vier Refactorings und Korrekturen an Core und Solver aus
dem automatisierten Review (gemeinsame Lastinterpolation, einmalige Extremwertkandidaten je Stück,
eine Extremwertmenge je Segment, vorberechnete Gelenk-Konfliktpositionen) änderten den Quell-Hash.
Ein frischer `compare` ohne `--actual` erzeugte den Report
[`acceptance-review-fixes.json`](../validation/results/acceptance-review-fixes.json)
(2026-11-15T15:30:56Z, macOS ARM64, `entryPoint` `EulerBernoulliBeamSolver`): 40 Fälle,
5088 externe und 3137 analytische Skalarvergleiche, 228 metamorphe Prüfungen,
`validated = true`. Die Product-Suite (6005 Tests, einer übersprungen) und die separate
Validierungs-Suite (469 Tests) bestanden vollständig. Die Abnahme auf Windows steht für die CI aus.

**SolverSourceSha256:**

```text
a3d403dee40da845700c885a9f189bba8c1b1a27255058752d05941d90bb158b
```

Dieser Hash ist die neue CI-Baseline in [ci.yml](../.github/workflows/ci.yml). Die Reports der
Gelenk-, Mehrsegment-, Federlager-, Linearlast- und M4-Abnahme bleiben als historische Evidenz unverändert.
