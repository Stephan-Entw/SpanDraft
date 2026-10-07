# SpanDraft Analysis

## Zweck und Datenfluss

`SpanDraft.Analysis` ist der synchrone Anwendungseinstieg zwischen Rechenkern und
Desktop-Oberfläche. `BeamAnalysis.Analyze(BeamModel beam)` orchestriert
die vorhandenen Komponenten und liefert ein `BeamAnalysisOutcome`:

```text
BeamModel
  → BeamAnalysis
  → EulerBernoulliBeamSolver.Solve(beam)
  → BeamSolution
  → BeamEngineeringAnalysis.Analyze(solution)
  → BeamAnalysisResult
```

Pro Aufruf erfolgt genau ein Solverlauf und bei dessen Erfolg genau eine
Engineering-Auswertung. Analysis führt keine zweite Modellvalidierung durch und
enthält keine Mechanik, Formeln, Sampling- oder Extremwertberechnung.
Ein null-Beam wird mit `ArgumentNullException` (Parameter `beam`) abgelehnt.

## Success und Failure

`BeamAnalysisOutcome` ist unveränderlich und enthält genau ein Ergebnis oder
einen Fehler:

| Zustand | IsSuccess | Result | Failure |
| --- | --- | --- | --- |
| Success | true | vorhanden | null |
| Failure | false | null | vorhanden |

Leere oder doppelte Zustände sind über die API nicht erzeugbar.

`BeamAnalysisResult` enthält die ursprüngliche `BeamSolution Solution` und das
daraus erzeugte `BeamEngineeringResult Engineering`. `result.Beam` verweist auf
das unveränderte Eingabemodell. Kennwerte bleiben in Solution beziehungsweise
Engineering; Analysis interpretiert die Ergebnisse nicht erneut.

## Fehlervertrag

Ausschließlich `BeamSolverException` aus dem Solveraufruf wird in einen
`BeamAnalysisFailure` übersetzt. Die Fehlerkategorien werden wie folgt abgebildet:

| SolverErrorCode | BeamAnalysisFailureCode |
| --- | --- |
| InvalidModel | InvalidModel |
| UnstableModel | UnstableModel |
| IllConditionedSystem | IllConditionedSystem |
| NumericalFailure | NumericalFailure |

Ein unbekannter SolverErrorCode löst `ArgumentOutOfRangeException` aus.
Fehler werden ausschließlich anhand strukturierter Codes klassifiziert.

`BeamAnalysisFailure` enthält:

- `Code`: Analysis-eigene Fehlerkategorie.
- `ValidationErrors`: die ursprüngliche schreibgeschützte
  `IReadOnlyList<ValidationError>` der Solver-Exception. Code, Path, Message und
  Reihenfolge bleiben unverändert. Bei InvalidModel stehen hier die bestehenden
  Core-Validierungsfehler; bei den anderen Solverfehlern ist die Liste derzeit leer.
- `TechnicalMessage`: die unveränderte Exception-Meldung für Diagnose und Logging.
  Sie ist ausdrücklich kein lokalisierter UI-Text. Die UI soll eigene Texte anhand
  der strukturierten Codes und Pfade bereitstellen.

Bei Solverfehlern gibt es keine Partial Results: keine Solution, kein
Engineering-Ergebnis, kein korrigiertes Modell und kein letztes gültiges Resultat.
Eine spätere Anzeige früherer gültiger Ergebnisse gehört zum UI-/Editor-State.

Engineering-Exceptions und unerwartete Fehler werden nicht normalisiert,
sondern an den Aufrufer weitergegeben. Für spätere erwartbare Engineering-Fehler
muss zuerst ein eigener strukturierter Engineering-Fehlervertrag eingeführt werden.

## Architektur und Verbraucher

Core bleibt für das Domain-Modell und dessen Validierung zuständig, Solver für
Mechanik und Numerik, Engineering für die bestehende fachliche Auswertung einer
Solution. Analysis verbindet diese Schritte ohne zweite Ergebnisinterpretation.
Details stehen in [DOMAIN.md](DOMAIN.md), [SOLVER.md](SOLVER.md) und
[ENGINEERING.md](ENGINEERING.md).

Analysis referenziert direkt Core, Solver und Engineering, ohne eigene NuGet-Pakete
oder Abhängigkeiten auf Avalonia, Desktop oder Reporting. Desktop referenziert
direkt Core und Analysis und startet Berechnungen ausschließlich über BeamAnalysis.
Core, Solver und Engineering bleiben unabhängig von Desktop.

Reporting soll später vollständige Analysis-Ergebnisse konsumieren. Es enthält
derzeit keine Implementierung; eine Analysis-Projektreferenz wird erst bei seiner
Implementierung ergänzt. Analysis gehört zur normalen Solution und den
Produkttests, nicht zur separaten unabhängigen Validation-Solution.
