# Untersuchung Solver-Umbau (nur Entwicklung)

Dieser Ordner enthält Entwicklungswerkzeuge und dauerhafte Evidenz für den
Architektur- und Gleitkomma-Closure. Er liegt außerhalb des Produktcodes
und der normalen Produkt-CI. Den Umbauplan beschreibt [`docs/SOLVER_UMBAU.md`](../../../docs/SOLVER_UMBAU.md).

- **Historische Prototyp- und Variantenuntersuchungen.** Die folgenden
  Abschnitte halten Werkzeuge, Befehle und damalige Ergebnisse fest.
  Die Messungen des frühen C#-Prototyps wurden auf macOS (arm64) erzeugt.
- **Architektur- und Literatur-Evidenz.** [`LITERATURE_AUDIT.md`](LITERATURE_AUDIT.md)
  hält Quellen, Herkunft und Begründung der Entscheidungen dauerhaft als
  interne Auditgrundlage fest. Die [Solver-Architektur](../../../docs/SOLVER_ARCHITEKTUR.md)
  definiert das Zielmodell.
- **Numerischer Vertrag und Closure-Nachweise.** [`FLOATING_POINT_CONTRACT.md`](FLOATING_POINT_CONTRACT.md)
  ist der normative numerische Vertrag des Entwurfs. `state/fpc.py` ist seine
  ausführbare Python-Referenz. Die
  [Closure-Kriterien C1–C13](../../../docs/SOLVER_ARCHITEKTUR.md#closure-kriterien-c1c13)
  verknüpfen den Vertrag mit den abgelegten Nachweisen.
- **C#-Produkt- und Plattformabnahme.** Die Abnahme der Zielarchitektur
  folgt mit ihrer Implementierung. Historische Prototypmessungen, die
  Python-Referenz und die axiale Produktbaseline für den bestehenden Pfad
  belegen keine Plattformkonformität der späteren C#-Implementierung.

## Voraussetzungen

- .NET 10 SDK
- Python 3.9 oder neuer mit `numpy` und `pycba`, zum Beispiel die Umgebung aus
  `validation/python/requirements.txt`

Alle Befehle laufen in diesem Ordner. `compare.py` baut nicht selbst; vorher einmal:

```sh
dotnet build harness -c Release
```

## Bausteine

| Datei | Zweck |
|---|---|
| `harness/` | Liest Modelle als JSON-Zeilen, rechnet sie mit dem C#-Solver und schreibt Felder, Extremwerte und Reaktionen. Der Assemblyname `SpanDraft.Tests` macht die internen Solver-Eingaben (`SolverLoads`) sichtbar. |
| `fuzz.py` | Exakte Referenz in Bruchrechnung (Macaulay-Klammern) für Lager und Lasten, dazu Zufallsgeneratoren. |
| `hinge.py`, `hinge2.py` | Exakte Referenz mit Gelenken, Stabilitätsregel, Gelenk-Generatoren. |
| `spring_ref.py` | Exakte Macaulay-Referenz mit Weg- und Drehfedern. |
| `transfer_ref.py` | Zweite, unabhängige exakte Referenz (Übertragungsverfahren): EI-Wechsel, linear veränderliche Lasten, Federn, Gelenke, eingeprägte Krümmung. |
| `compare.py` | Ruft den Testtreiber auf und bewertet Abweichungen. |
| `limit.py`, `spanproto.py` | Grenzfälle und Python-Prototyp „Lasten ohne Knoten“. |

## Läufe

| Befehl | Prüft |
|---|---|
| `python3 xcheck.py` | Beide exakten Referenzen gegeneinander, bitgenau (Federn, Gelenke). |
| `python3 hcmp.py` | Gelenke: C# gegen exakt, PyCBA gegen exakt. |
| `python3 s_smoke.py` | Federn, einfache Fälle. |
| `python3 s_fuzz.py t`, danach `python3 s_detail.py` | Federn: 270 Extremfälle, Tabelle nach Steifigkeit und Abstand. |
| `python3 s_fuzz.py r 1500` | Federn: Zufallsmodelle je Abstand L/10² bis L/10⁹, Stabilität gegen exakt. |
| `python3 s_ill.py`, `python3 s_probe.py` | Abgelehnte Federmodelle: fast beweglich oder nicht. |
| `python3 f_smoke.py` | EI-Wechsel, Dreiecks- und Trapezlasten, Krümmung, alles gemischt. |
| `python3 f_fuzz.py 1000` | Alle Features gemischt, Zufallsmodelle. |
| `python3 f_pycba.py 400` | Exakte Referenz gegen PyCBA (fremdes Programm). |
| `constraint/*.py` | Skalierte Lagerbedingung (Differenzenquotient), Federn als Knoten, Netzentkopplung. |
| `old-vs-new/Program.cs` | Lagerpaare im alten und neuen Solver; braucht je eine Projektdatei mit Verweis auf den jeweiligen Stand. |

## Eingebaute Fehler

`mutate2.py` (alte Fehlerklassen am neuen Pfad) und `mutate3.py` (neue Features) arbeiten auf einer
Kopie des Repositorys, damit der Arbeitsstand nie verändert wird:

```sh
rsync -a --exclude bin --exclude obj --exclude .git ../../../ base3/
python3 mutate3.py          # Tests und Abnahme je Fehler
python3 mut_fuzz.py S3 L1   # dieselben Fehler gegen die exakten Vergleiche
```

`mutate2.py` erwartet die Kopie in `base2/`.

## Ergebnisse

Die Protokolle in `results/` stammen aus den Läufen der Untersuchung:

| Datei | Inhalt |
|---|---|
| `s_detail2.log` | Federn, gezielte Fälle: höchstens 5·10⁻¹⁰, keine Ablehnung. |
| `s_rand2.log` | Federn, 6.000 Zufallsmodelle: höchstens 2·10⁻⁷, Stabilität in allen Fällen wie exakt. |
| `f_rand.log` | Alle Features gemischt, 4.000 Modelle: eines mit 9·10⁻⁶, sonst höchstens 10⁻⁸. |
| `mutation2.log`, `mutation3b.log` | Eingebaute Fehler: Erkennung durch Tests und Abnahme. |
| `compare-rand.log` | Lasten ohne Knoten, alter gegen neuen Solver. |
| `proto.log`, `fe.log`, `spring.log`, `branch.log` | Skalierte Lagerbedingung und Federn als Prototyp. |

Bekannte Grenzen der Prototypuntersuchung: PyCBA ist selbst nicht exakt (numerische Integration bei Krümmung und Teillasten,
Stellenverlust bei sehr weichen oder sehr steifen Federn). Ablehnungen des C#-Solvers treten bei
fast beweglichen Systemen auf, in seltenen Fällen auch bei lösbaren Modellen mit sehr unterschiedlich
steifen Federn im Nanometerabstand (5 von 6.000 beziehungsweise 13 von 4.000 Zufallsmodellen).

## Zustandsformulierung (Architekturentwurf)

Der Ordner `state/` prüft die Zielformulierung aus
[`docs/SOLVER_ARCHITEKTUR.md`](../../../docs/SOLVER_ARCHITEKTUR.md): lokale Zustände je
mechanischem Abschnitt, Elimination vorgeschriebener Zustandskomponenten, Reaktionen aus Sprüngen.
Es ist eine Gleitkomma-Implementierung in Python (IEEE double wie .NET), kein Produktionscode. Sie
wird gegen die exakte Referenz `transfer_ref.py` verglichen. Es wird nur die Standardbibliothek
gebraucht (kein numpy, kein .NET). Die Bytecode-Caches unterdrückt `PYTHONDONTWRITEBYTECODE=1`.

| Datei | Zweck |
|---|---|
| `state/state.py` | Formulierung, Entdimensionierung mit Zweierpotenzen, Ruiz-Äquilibrierung, LU, adaptive Nachiteration (Residual double, kompensiert oder exakt), komponentenweiser Fehlerindikator über adjungierte Lösungen; adjungierte Nachiteration `none`/`one` (Vorgabe, damit ältere Protokolle reproduzierbar bleiben)/`adaptive`/`beta` (damalige Entwurfsregel); Ablationsvarianten |
| `state/kinematics.py` | Exakte Mechanismusprüfung auf dem kinematischen Modell der Teilkörper (Bareiss) |
| `state/gen.py` | Generatoren aus `s_fuzz.py`/`f_fuzz.py` ohne numpy; gleiche Zufallsfolgen |
| `state/metrics.py` | Fehlermaße (globale Feldskala, gemischtes Maß), Indikator-Verletzungen |
| `state/cases.py`, `state/targeted.py` | Gezielte Grenzfälle: Konfigurationen × Abstände × Steifigkeiten |
| `state/rand.py` | Zufallsmodelle je Abstandsklasse, Ablehnungs- und Verletzungsaudit |
| `state/sens.py` | Eingabesensitivität (1-ulp-Probe), nur Diagnose |
| `state/ablation.py` | Notwendigkeit von Elimination, Nachiteration, Skalierung; Residualvarianten |
| `state/invariance.py` | Historischer Vergleich der Matrixinvariante bei zusätzlichen Lasten, Zweierpotenz-Skalierung und Einheitenwechsel |
| `state/violations.py` | Zerlegung einer Indikator-Verletzung in Daten-, Lösungs- und Auswertungsanteil |
| `state/coverage.py` | Dichte Auswertung je Stück: Fehler und β über x, Monotonie von β, Stück-Gate |
| `state/acceptance.py`, `state/acceptance_summary.py` | Adjungierte Nachiteration (ohne/ein Schritt/adaptiv), Akzeptanzfaktor s, β/Fehler je Größe und Klasse |
| `state/decoupling.py` | Lösung und Status mit und ohne Sensitivitätsauswertung bitgleich |
| `state/springs.py` | Federreaktion aus Sprung und Federgesetz, Konsistenzdiagnose |
| `state/zero.py`, `state/axial.py` | Nullanregung, kompensierte Lasten, Unterlauf; Axialformulierung und exakte Axialreferenz |
| `state/spring_rule.py`, `state/spring_rule_summary.py` | Identität von Sprung und Federgesetz, Konsistenzrelation mit vollem und gekürztem d, adjungierte Linearität, eingebaute Fehler, Lieferregeln (Sprung, Gesetz, kleineres β, Kombination) |
| `state/spring_extremes.py` | Gezielte Extremfälle der Lieferregeln (k·L³/EI bis 10¹⁵, Abstände bis L/10¹¹) |
| `state/gate_study.py`, `state/gate_summary.py` | Stück-Gate-Varianten A/B/C, strenge Monotonie, ω, gestörte Faktorisierung, Regeln der adjungierten Nachiteration (ohne/ein Schritt/adaptiv/β-Regel) |
| `state/perturb_check.py` | Gestörte Faktorisierung: Regeln der adjungierten Nachiteration im Vergleich |
| `state/mono_check.py` | Ort und Größe der strengen Monotonieabfälle von β |
| `state/lb_check.py` | Geschlossene Untergrenze LB_q gegen dicht ausgewertete \|M\|, \|V\| (berechnet und exakt) |
| `state/axial_quality.py` | Indikator und Gate des Axialanteils gegen die exakte Axialreferenz |
| `state/cost.py` | Aufwand: Bandbreiten nach Pivotisierung, Zahl adjungierter Lösungen, Operationsmodell Band/dicht, viele Lasten je Abschnitt |
| `state/factor_study.py`, `state/factor_summary.py` | Runde 4: Struktur der Gleichungsmatrix (Bandbreite, Kopplung), GEPP-Wachstum, LU gegen Householder-QR, Stressfamilien, adversarielle Suche nach Pivotwachstum |
| `state/cond_check.py` | Runde 4: normweise, Skeel- und komponentenweise Kondition der Modelle, an denen QR scheitert |
| `state/near_tau.py` | Runde 4: gezielte Modelle mit Gate nahe τ (stetiger Abstandsparameter, Bisektion) |
| `state/refine_study.py`, `state/refine_summary.py` | Runde 4: Nachiterationsregeln (SpanDraft, `xGERFS`, LAWN 165, 5 Schritte) primär und adjungiert, Verifikation der adjungierten Lösungen (Rump, Satz 10.2), gestörte Faktorisierung |
| `state/underflow_study.py` | Runde 4: Unterlaufpfade (Lastintegrale, Auswertung, Äquilibrierung, SI-Rückrechnung) |
| `state/piece_oracle.py` | Runde 4: exakte Stückpolynome, exakte Bernstein-Einschließung, quadratfreie Zerlegung und Sturm-Isolation, Port von `PolynomialRoots` |
| `state/piece_study.py`, `state/piece_summary.py` | Runde 4: Stück-Gate gegen Bernstein-Oracle, Extremwertkandidaten gegen exakte Wurzelisolation, konstruierte Grenzfälle |
| `state/fpc.py` | Ausführbarer Gleitkomma-Vertrag mit Exponentenskalierung, lokalen Bounds, verifiziertem Residual und gerichtetem SI-Gate |
| `state/fpc_boundary.py` | Deterministische Grenzwerte, unabhängige rationale Erwartungen sowie Primitive- und Mechanikmutanten |
| `state/fpc_counts.py` | mechanische Ableitung der Rundungszahlen aus den Operationsfolgen |
| `state/fpc_stage_a.py` | Stufe A: jede Formel gegen ihre Schranke, strukturierte Grenzwerte, graduell und FTZ/DAZ; Vergleich mit dem Rundungsmodell von `state.py` |
| `state/fpc_chain.py` | Stufen B–F: Daten und Residual, Verifikationsvoraussetzung, Funktional, Feld und Gate, SI-Ausgabe gegen exakte Arithmetik auf schwierigen Referenzmodellen |
| `state/fpc_unverified.py` | Ursache der zwei in Runde 4 nicht verifizierbaren Modelle (μ je Gewicht, Spektralradius von M) |
| `state/fpc_regress.py` | Vertragslöser auf den Zufallsmengen der Runde 4 (Entscheidungen, Schranken) |
| `state/fpc_summary.py` | Zusammenfassung der `state-fpc`-Protokolle |
| `state/o9_sizing.py` | Runde 5: Operations- und Speichermodell des Qualitätsvertrags für n_m = 10 … 5.000 |

Läufe (Protokolle in `results/state-*.log`):

```sh
export PYTHONDONTWRITEBYTECODE=1
python3 state/targeted.py
for r in 1e-2 1e-4 1e-6 1e-9 1e-11; do
  python3 state/rand.py s 1500 $r; python3 state/rand.py f 1000 $r
done
python3 state/ablation.py
python3 state/invariance.py
python3 state/zero.py
python3 state/springs.py
python3 state/coverage.py targeted
for r in 1e-2 1e-4 1e-6 1e-9 1e-11; do
  python3 state/coverage.py s $r 1500; python3 state/coverage.py f $r 1000
done
python3 state/decoupling.py              # liest die REJ/SING-Zeilen der state-rand-Protokolle
python3 state/acceptance.py targeted
for r in 1e-2 1e-4 1e-6 1e-9 1e-11; do
  python3 state/acceptance.py s $r 1500; python3 state/acceptance.py f $r 1000
done
python3 state/acceptance_summary.py      # fasst die state-acceptance-Protokolle zusammen
python3 state/violations.py f 1e-2 638      # Beispiel: einzelnes Modell zerlegen
# Runde 3
for a in "targeted" "s 1e-2 1500" "s 1e-4 1500" "s 1e-6 1500" "s 1e-9 1500" "s 1e-11 1500" \
         "f 1e-2 1000" "f 1e-4 1000" "f 1e-6 1000" "f 1e-9 1000" "f 1e-11 1000"; do
  python3 state/spring_rule.py $a; python3 state/gate_study.py $a; python3 state/lb_check.py $a
done
python3 state/spring_rule_summary.py      # fasst die state-spring-rule-Protokolle zusammen
python3 state/gate_summary.py             # fasst die state-gate-Protokolle zusammen
python3 state/perturb_check.py            # liest die state-gate-Protokolle
python3 state/mono_check.py               # liest die state-gate-Protokolle
python3 state/spring_extremes.py
python3 state/axial_quality.py
python3 state/cost.py
```

Protokollnamen der Runde 3: `state-spring-rule-<Klasse>.log` und `-summary.log`,
`state-gate-<Klasse>.log` und `-summary.log`, `state-lb-<Klasse>.log`, `state-perturb-check.log`,
`state-mono-check.log`, `state-spring-extremes.log`, `state-axial-quality.log`, `state-cost.log`.
Die Zeitwerte in `state-cost.log` stammen aus parallel laufenden Prozessen; aussagekräftig sind
nur Wachstumsexponenten und Operationszahlen.

Die Läufe sind unabhängig und lassen sich parallel starten (je Klasse ein Prozess). Ausnahmen:
`decoupling.py` braucht die `state-rand`-Protokolle, `acceptance_summary.py` die
`state-acceptance`-Protokolle, `spring_rule_summary.py` die `state-spring-rule`-Protokolle und
`gate_summary.py`, `perturb_check.py` und `mono_check.py` die `state-gate`-Protokolle.

# Runde 4 (Literaturabgleich)

Quellen, Audit-Matrix und Ergebnisse: [`LITERATURE_AUDIT.md`](LITERATURE_AUDIT.md). Läufe mit der
Umgebung `~/.venvs/spandraft-validation` (numpy, scipy, sympy):

```sh
export PYTHONDONTWRITEBYTECODE=1
python3 state/factor_study.py targeted
python3 state/factor_study.py random 5
python3 state/factor_study.py stress 10
python3 state/factor_study.py stressgrowth 40
python3 state/factor_study.py adversarial 24 300
python3 state/factor_summary.py           # fasst die state-factor-Protokolle zusammen
python3 state/cond_check.py               # liest die state-factor-Protokolle
python3 state/near_tau.py > results/state-near-tau-models.log 2> results/state-near-tau.log
python3 state/refine_study.py targeted
python3 state/refine_study.py neartau     # liest state-near-tau-models.log
for r in 1e-2 1e-4 1e-6 1e-9 1e-11; do
  python3 state/refine_study.py s $r 1500 5; python3 state/refine_study.py f $r 1000 5
done
python3 state/refine_summary.py           # fasst die state-refine-Protokolle zusammen
python3 state/underflow_study.py
python3 state/piece_study.py constructed
python3 state/piece_study.py targeted
python3 state/piece_study.py random 10
python3 state/piece_summary.py            # fasst die state-piece-Protokolle zusammen
```

Protokollnamen der Runde 4: `state-factor-{targeted,random,stress,stressgrowth,adversarial}.log`
und `-summary.log`, `state-cond-check.log`, `state-near-tau-models.log`, `state-near-tau.log`,
`state-refine-<Klasse>.log` und `-summary.log`, `state-underflow.log`,
`state-piece-{constructed,targeted,random}.log` und `-summary.log`. Jeder Befehl schreibt nach
stdout, die Protokolle entstehen durch Umleitung nach `results/` mit diesen Namen. Der vollständige
LU/QR-Vergleich der Stressfamilien läuft nur bis 10 Felder; für 20 und 40 Felder ist die exakte
Bruchrechnungsreferenz in Python zu langsam, dort messen `stressgrowth` nur Struktur und Wachstum.

# Closure des Gleitkomma-Vertrags (O2)

[`FLOATING_POINT_CONTRACT.md`](FLOATING_POINT_CONTRACT.md) definiert Vertrag
und Beweiskette. Die folgenden Läufe verwenden reines Python ohne numpy.

```sh
export PYTHONDONTWRITEBYTECODE=1
python3 state/fpc_boundary.py > results/state-fpc-boundary.log
python3 state/fpc_counts.py > results/state-fpc-counts.log
python3 state/fpc_stage_a.py 20000 > results/state-fpc-stage-a.log
for S in targeted neartau perturbed underflow constructed unverified spring-extremes; do
  python3 state/fpc_chain.py $S > results/state-fpc-chain-$S.log
done
python3 state/fpc_unverified.py > results/state-fpc-unverified.log
for k in s f; do for r in 1e-2 1e-4 1e-6 1e-9 1e-11; do
  python3 state/fpc_regress.py $k $r 1500 5 > results/state-fpc-regress-$k-$r.log
done; done
python3 state/fpc_summary.py > results/state-fpc-summary.log   # liest alle state-fpc-Protokolle
python3 state/o9_sizing.py > results/state-o9-sizing.log
```

`fpc_regress.py` vergleicht mit den `state-refine`-Protokollen der Runde 4, `fpc_chain.py
neartau` liest `state-near-tau-models.log`. Die Läufe sind unabhängig und lassen sich parallel starten. Für
die deterministische Suite und die Kettenprüfung sind keine externen
Python-Pakete nötig. Die alten Mechanikmutationsprotokolle bleiben
historische Evidenz. B3, L1 und L2 werden zusätzlich im ausführbaren
Biegevertrag gegen die unabhängige Übertragungsreferenz injiziert.

Die ergänzende unabhängige Referenz- und Root-Prüfung verwendet
`python3 xcheck.py` und `python3 state/piece_study.py constructed`
mit der Validierungs-Python-Umgebung. Ergebnis: `state-fpc-oracles.log`.

### Axiale Produktbaseline

Vom Repository-Stamm aus den bestehenden Validierungsrunner bauen und
dessen öffentlichen Ergebnisexport auf exakte Nullwerte prüfen.

```sh
dotnet run --project validation/dotnet/SpanDraft.Validation.Runner -- export \
  --output /tmp/spandraft-axial-baseline.json
python3 validation/investigations/solver-rework/state/fpc_boundary.py \
  --axial-export /tmp/spandraft-axial-baseline.json
```

Der Export prüft u und N an den Validierungspunkten sowie nullwertige
und abwesende axiale Lagerreaktionen. Die bestehenden Klassen
`SolverAnalyticalTests` und `SolverContinuousResultTests` ergänzen
die Baseline um Knoten- und Feldprüfungen.
