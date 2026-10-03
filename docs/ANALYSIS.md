# SpanDraft Analysis

## Zweck und Datenfluss

`SpanDraft.Analysis` ist der synchrone Anwendungseinstieg zwischen Rechenkern und
zukünftiger Desktop-Oberfläche. `BeamAnalysis.Analyze(BeamModel beam)` orchestriert
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

Das unveränderliche, versiegelte `BeamAnalysisOutcome` besitzt einen privaten
Konstruktor und interne, nullgeprüfte Erzeugungsmethoden. Öffentlich sind nur
lesbare Properties verfügbar:

| Zustand | IsSuccess | Result | Failure |
| --- | --- | --- | --- |
| Success | true | vorhanden | null |
| Failure | false | null | vorhanden |

Leere oder doppelte Zustände sind über die API nicht erzeugbar.

`BeamAnalysisResult` enthält die ursprüngliche `BeamSolution Solution` und das
genau daraus erzeugte `BeamEngineeringResult Engineering`. Die Convenience-Property
`Beam => Solution.Beam` speichert kein zweites Modell. Der Input-Beam und
`result.Beam` sind dasselbe Objekt; Engineering greift auf die Extremwerte,
Querschnitts- und Werkstoffdaten dieser Solution zu. Kennwerte bleiben in Solution
beziehungsweise Engineering und werden in Analysis weder dupliziert noch geflattet.

## Fehlervertrag

Ausschließlich `BeamSolverException` aus dem Solveraufruf wird in einen
`BeamAnalysisFailure` übersetzt. Der zentrale, explizite Switch bildet ab:

| SolverErrorCode | BeamAnalysisFailureCode |
| --- | --- |
| InvalidModel | InvalidModel |
| UnstableModel | UnstableModel |
| IllConditionedSystem | IllConditionedSystem |
| NumericalFailure | NumericalFailure |

Ein unbekannter zukünftiger SolverErrorCode löst `ArgumentOutOfRangeException`
aus. Es gibt keine Klassifizierung anhand von Meldungstexten und keinen
Fallback auf einen bestehenden Fehlercode.

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

Engineering-Exceptions werden derzeit nicht normalisiert. Die Engineering-Auswertung
liegt außerhalb des Solver-Catch-Bereichs. Programmierfehler und unbekannte Fehler
bleiben sichtbar. Für spätere erwartbare Engineering-Fehler muss zuerst ein eigener
strukturierter Engineering-Fehlervertrag eingeführt werden.

## Architektur und Verbraucher

Core bleibt für das Domain-Modell und dessen Validierung zuständig, Solver für
Mechanik und Numerik, Engineering für die bestehende fachliche Auswertung einer
Solution. Analysis verbindet diese Schritte ohne zweite Ergebnisinterpretation.
Details stehen in [DOMAIN.md](DOMAIN.md), [SOLVER.md](SOLVER.md) und
[ENGINEERING.md](ENGINEERING.md).

Analysis referenziert direkt Core, Solver und Engineering, ohne eigene NuGet-Pakete
oder Abhängigkeiten auf Avalonia, Desktop oder Reporting. Desktop referenziert
direkt Core und Analysis und startet Berechnungen künftig nur über BeamAnalysis.
Core, Solver und Engineering bleiben unabhängig von Desktop.

Reporting soll später vollständige Analysis-Ergebnisse konsumieren. Es enthält
derzeit keine Implementierung; eine Analysis-Projektreferenz wird erst bei seiner
Implementierung ergänzt. Analysis gehört zur normalen Solution und den
Produkttests, nicht zur separaten unabhängigen Validation-Solution.

UI, ViewModels, Editor-State, Persistenz, Exporte, Async/Cancellation und Caching
sind nicht Bestandteil dieses Meilensteins.
