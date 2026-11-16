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
direkt Core, Analysis und Reporting und startet Berechnungen ausschließlich über BeamAnalysis.
Core, Solver und Engineering bleiben unabhängig von Desktop.

Analysis gehört zur normalen Solution und den Produkttests, nicht zur separaten
unabhängigen Validation-Solution.

## Lokale Interaktionsmessung im Desktop

Bestätigte Änderungen am committed Projektzustand können im Desktop bis zum
zugehörigen gerenderten Avalonia-Frame gemessen werden. Das gilt auch für reine
Metadaten-/Präsentationsänderungen und Undo/Redo; initiale Projektanlage und
Analyse beim Öffnen sind ausgenommen. Eingabedrafts und Previews werden nicht gemessen.

Preparation beginnt vor Parsing und Validierung der Bestätigung und endet vor
`ToBeamModel()`. Analysis umfasst diese Konvertierung und `BeamAnalysis.Analyze()`
bis zum `BeamAnalysisOutcome`. Bei nichtmechanischen Änderungen ist Analysis
`TimeSpan.Zero`. Presentation umfasst die anschließende synchrone Verarbeitung
einschließlich ViewModel-Benachrichtigungen. UI endet an `CompositionBatch.Rendered`
nach dem Animation-Callback; dies bestätigt keine physische Bildschirmausgabe.
Total und Phasen stammen aus derselben monotonen Zeitlinie.

Die Diagnose wartet nicht im Benutzerkommando, invalidiert keine Visuals und
verändert den Visual Tree nicht. Nicht verfügbare oder abgebrochene Frames
verwerfen die offene Messung ohne Einfluss auf die Produktaktion. Das Anfordern
eines Composition-Batches kann Messoverhead verursachen.

Erfasst werden ausschließlich UTC-Startzeit, technischer Aktionstyp und Dauern.
Die letzten 50 abgeschlossenen Einträge bleiben im Arbeitsspeicher des Fensters;
Projektinhalte, Persistenz, Telemetrie und Netzwerkzugriffe sind ausgeschlossen.
Messkontexte bleiben transient und gehören weder zu Revisionen noch zum Projektformat.
