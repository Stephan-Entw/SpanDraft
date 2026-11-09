# Untersuchung Solver-Umbau (nur Entwicklung)

Dieser Ordner sichert die Prüfwerkzeuge, mit denen der Prototyp des neuen Solvers
untersucht wurde. Er gehört nicht zur Abnahme, nicht zur CI und nicht zum Produkt.
Den Plan für den Umbau beschreibt [`docs/SOLVER_UMBAU.md`](../../../docs/SOLVER_UMBAU.md).

Alle Werte wurden mit dem Prototyp auf diesem Branch erzeugt, nur auf macOS (arm64).

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

Bekannte Grenzen: PyCBA ist selbst nicht exakt (numerische Integration bei Krümmung und Teillasten,
Stellenverlust bei sehr weichen oder sehr steifen Federn). Ablehnungen des C#-Solvers treten bei
fast beweglichen Systemen auf, in seltenen Fällen auch bei lösbaren Modellen mit sehr unterschiedlich
steifen Federn im Nanometerabstand (5 von 6.000 beziehungsweise 13 von 4.000 Zufallsmodellen).
