# DEV-58: M2-Abnahmeakte

Baseline des vor Änderungen durchgeführten Audits:
`3552a815eb91b99d5f8472a161bc2e6d312448c9`, Branch `features/solver-umbau`.
Normative Basis: [DEV-58](https://linear.app/stephan-projects/issue/DEV-58),
[Architektur](../../../docs/SOLVER_ARCHITEKTUR.md),
[Umbau](../../../docs/SOLVER_UMBAU.md), [Floating-Point-Vertrag](FLOATING_POINT_CONTRACT.md).
Diese Akte indiziert Evidenz; sie verändert keinen Vertrag.

## Coverage-Audit vor Änderungen und gezielter Abschluss

„Vollständig“ bezieht sich auf die relevanten festen Nachweise des heutigen
Migrationsumfangs, nicht auf einen experimentellen Beweis für alle möglichen Modelle.
Die folgenden Testnamen bezeichnen tatsächliche Assertions, nicht nur ähnliche Fälle.

| Zusage | Baseline-Kategorie | Vorhandene Evidenz / M2-Abschluss |
|---|---|---|
| C1, A: Primitives und lokale Fehler | vollständig belegt | `SolverBinary64Tests`, `SolverBoundedTests`, `SolverBendingNumericsTests`: rationale Produkte, Differenzen, Integrale, EI-Datenfehler und gerichtete Grenzen |
| C1, B: Systemdaten, Primärlösung, Residual | vollständig belegt | `SolverBendingSystemTests.CheckExactMechanics`, `SolverCertifiedResidualTests`, `SolverVerifiedBendingStateTests.StagesBThroughDEncloseExactMechanicsAndFullNormalizedState`: exakte Matrix/RHS, Datenbounds, exaktes Residual und rationale Lösung |
| C1, C/D: adjungierte Relation und Zustandsgrenze | vollständig belegt | `SolverBendingVerificationTests.StagesCAndDProveAdjointResidualInverseRelationAndActualError`: adjungierte Residuen, exaktes `I−RS`, `Mv≤μv`, `v>0`, `μ<1`, tatsächlicher Zustandsfehler; vollständige mechanische Zustände zusätzlich in `SolverVerifiedBendingStateTests` |
| C1, E/F: Felder, Reaktionen, Funktionale, SI | vollständig belegt | `SolverBendingFunctionalTests`, `SolverBendingOutputTests.StagesEAndFEncloseFixedIndependentExactMechanicsThroughoutEveryPiece`, `SolverBendingOutputQualityTests.StageFBackScalingEnclosesExactBinaryValueAndBothUnderflowLosses`: unabhängige Macaulay-Sollwerte, eigene Reaktionsadjungierte, direkte Kandidatenwerte, beide SI-Verlustbudgets |
| C2: mathematisches τ, vollständige Boundary-Fälle | teilweise belegt | M1 prüft τ rational für gelieferte Outputs und gerichtete Primitives; M2 ergänzt `CompleteRigidSupportCasesEncloseExactErrorsAndRespectMathematicalTau` für vollständige Liefer-/Gate-Fälle nahe τ und konservative Ablehnung |
| C3: sichere Norm, Nullanregung | vollständig belegt | `SolverExcitationTests.EnclosesTheExactNormAcrossTheBoundaryFamilies`, `SolverVerifiedBendingStateTests.LoadedZeroRhsStillExecutesFullVerification`, `UnloadedDoesNotReadSkippedNumericalStageData`: winzige Beiträge, Kompensation, ±0 und übersprungener Solve |
| C4: Exponenten und Bereiche | vollständig belegt | `SolverBinary64Tests`, `SolverBendingRangeTests`, `SolverRuizTests`, SI-Range-Tests; M2 ergänzt den vollständigen historischen U4-Kleinstbalken und bitgleiche normierte Zweierpotenz-Umskalierung |
| C5: gerichtete Grenzen an ±λ, keine subnormalen Bounds | vollständig belegt | `OutwardImplementsTheFullBoundaryTable`, `DirectedScalingPreservesExactNormalResultsAndUsesTheUnderflowSign`, `AssertStored` in den exakten Tests |
| C6: GammaUpper | vollständig belegt | `GammaUpperUsesDownwardDenominatorAndUpperQuotient`, `GammaRejectsInvalidCounts`, `ComputedBoundEnclosesTheExactRelativeBoundOnTheStoredMagnitude` |
| C7: Residual/Unterlaufmodi und Materialisierung | vollständig belegt | `BoundaryOraclesEncloseExactResidualAndAllEmulatedUnderflowModes`, `ProductAndSubtractionMustRemainTwoMaterializedRoundings`, `FixedA6MatrixCoversCancellationAndNormalToUnderflowProducts` |
| C8: RawBound ≠ VerifiedStateBound ≠ VerifiedOutputBound | vollständig belegt | `RawBoundCanUnderestimateActualErrorButVerifiedBoundEnclosesIt`; getrennte System-/Funktions-/SI-Tests prüfen die jeweilige Schranke unabhängig |
| C9: Konstantenklassen | vollständig belegt | Code-Abgleich mit FPC §8: Binary64-Grenzen und τ, explizite Range-Regeln, getrennte Ruiz-/Nachiterations-/Neumann-Strategie; deren Grenzen und Abbruchregeln besitzen feste M1-Tests |
| C10: Primitive- und Mechanikmutanten | teilweise belegt | Dauerhafte M1-Tests erkennen alle im Umfang erreichbaren Mutanten; Zuordnung unten macht dies explizit. L1/Rampe außerhalb des heutigen Umfangs |
| C11: Vertrag / Implementierung / Closure konsistent | vollständig belegt | Audit von `state/fpc.py`, Boundary-, Summary- und Oracle-Logs gegen C#-Operationsfolgen und die exakten Tests; kein bestätigter Widerspruch. EI wird in M1 konservativ zusätzlich mit Rundungsbeitrag geführt |
| C12, O12, O19: reale Plattformkonformität im Release-Build | teilweise belegt | M1 besitzt die relevanten Plattformtests; M2 führt sie nativ auf macOS ARM64 und über den bestehenden Windows-x64-Product-CI-Job aus. Keine plattformübergreifende Bitgleichheit verlangt |
| C13: heutiger Migrationsumfang | teilweise belegt | Interner End-to-End-Pfad aus M1 vorhanden; M2 schließt unabhängige Golden-/CAD-/Boundary-Abnahme. Future-Features und O9 bleiben außerhalb |
| DEV-58: V01–V15 und aktueller Validation-Katalog unabhängig über Kernel | unbelegt | M1-End-to-End-Tests vergleichen überwiegend dieselben internen Daten bzw. ergänzend den alten Solver. M2 nutzt Friend-Assembly-Zugriff, dieselben 18 Cases, 45 analytischen/externen Referenzen, Physics-/Comparison-Regeln und metamorphischen Prüfungen |
| DEV-58: CAD/6 nm, belastete benachbarte Ereignisse, UDL-Überlappung | teilweise belegt | M1 hat nahe Zustände und exakte Einzel-UDL-Felder; M2 schließt die Ausgabekette der 35 CAD-Fälle aus `compare.py:cad`, `fpc_chain.py:constructed` (6nm-pair/global-distance), zweier benachbarter belasteter Ereignisse und überlappender UDLs |
| DEV-58: Koinzidenz, ausgewählte Achse, Axial-null | vollständig belegt | `SolverMechanicalInputTests`, `SolverBendingSystemTests.CoincidentRigidSupportsEliminateEachDirectionExactlyOnce`, exaktes Output-Oracle `coincident`, `SolverKernelIntegrationTests`: gemeinsame Reaktion, Achse, u/N/ReactionX=+0 bzw. abwesend. M2 erweitert +0-/Anwesenheitsprüfungen auf den ganzen Validation-Katalog |
| DEV-58: Extrema, Kandidatenvollständigkeit und Seiten | vollständig belegt | `SolverBendingOutputExtremaTests`: unabhängig faktorisierte doppelte/benachbarte Wurzeln, mehrere innere Kandidaten, Randnähe, kurze Stücke, Ereignisgrenzen und Gleichstand; `PolynomialRootsTests`: feste unabhängige Polynome. Kandidatenwerte separat mit Macaulay-Oracles eingeschlossen. `SolverKernelIntegrationTests` prüft `Interior→Side=null` |
| Federn, Gelenke, EI-Wechsel, Rampen, belastete Axialmechanik | außerhalb des heutigen Migrationsumfangs | Historische Closure-Fälle dieser Features werden nicht pauschal portiert. L1 gehört zu DEV-6, weitere Regeln zu den jeweiligen Feature-Tracks |

M2-Plan aus dem Audit: bestehende A–F-Assertions wiederverwenden; nur den
Validation-Einstieg, konkrete vollständige Boundary-/Regressionsfälle, einen
Guard der heutigen Eingabegrenze und die fehlende Zweierpotenz-Umskalierung
ergänzen. Keine fachliche/numerische Änderung am M1-Kernel erforderlich.

## Architektur-Invarianten und Fehlerabgrenzung

Die Invarianten 1–5 sind durch `AllLoadKindsLeaveMechanicsMatrixAndRoundingCountsBitIdentical`
(einschließlich Ruiz-Matrix und Exponenten), `EveryRigidSupportCombinationPreservesEquationCountEliminationAndBand`,
exakte System-Oracles, globale Abstandstests und getrennte gespeicherte ULP-Koordinaten
abgesichert. Ein Penalty-/Regularisierungs-/Snapping-Ersatz widerspricht diesen Assertions.
Invariante 6: `ExactRankAndAxialModeAreIndependent`,
`ExactRankDoesNotCollapseTinyOrVeryLargeCoordinateDifferences` und
`MechanismPrecedesEveryNumericalPreparation` entscheiden ohne numerische Toleranz.
Invariante 7: exakte E/F- und vollständige Boundary-Tests.
Invariante 8: `MigrationEntriesCannotAcceptFutureFeaturePrimitivesOrSolverLoads`.
Invariante 9: Der Kernel bietet keinen Sensitivitäts-/Konditions-/Backward-Error-Eingang
und keinen Statuszweig dafür; die Verifikation ist unabhängig von LU-Kandidatenqualität.
Invarianten 10/11: die Nullpfad- und internen Konsistenztests aus M1.

| Ursache | Dauerhafter C#-Nachweis |
|---|---|
| Mechanismus und Vorrang vor Range | `SolverMechanicalStabilityTests.MechanismPrecedesEveryNumericalPreparation` |
| NumericalRange | `SolverBendingRangeTests`, `SolverRuizTests.MatrixUnderflowAndExponentOverflowAreRangeFailures`, SI-Grenzen; M2 `HistoricalTinyBeamIsVerifiedButRejectedBySiRangeBeforeOutputGate` |
| ZeroPivot versus winziger nicht-null Pivot | `SolverDenseLuTests.ZeroTinyAndNonfinitePivotsHaveDifferentCausesWithoutFallback` |
| NotVerified | `FixedMatrixRhsAndBoundsProduceNotVerifiedWithoutDisturbingLu`, ungültige Gewichte/Kontraktionen in `SolverBendingVerificationTests` |
| Gate nach erfolgreicher Verifikation | `FixedVerifiedLowLevelSystemFailsOnlyTheOutputGate`; M2 vollständige Fälle mit `state.IsVerified` und exakter Fehler-/Gate-Prüfung vor der erwarteten Ablehnung |
| NonFinite | `NonfiniteCertifiedIntermediatesAreNonfiniteFailures`, `ZeroTinyAndNonfinitePivotsHaveDifferentCausesWithoutFallback`, `FunctionalScalingOverflowAndUnexpectedNonfiniteHaveDifferentCauses` |
| InternalInconsistency | `InconsistentSystemDimensionsHaveStructuredInternalCause` |

## C10: Mutant → dauerhafter Test

Bedeutung nach [Boundary-Implementierung](state/fpc_boundary.py), nicht nach dem Namen vermutet.

| Mutant | Fehler | C#-Test, der ihn erkennt |
|---|---|---|
| outward | nur nextafter, ohne Projektion subnormaler Grenzen | `SolverBinary64Tests.OutwardImplementsTheFullBoundaryTable` |
| lambda | lokales Residualbudget an/unter λ auf null setzen | `SolverCertifiedResidualTests.FlushedProductSkipsSubtractionAndAccumulatorRounding` und `BoundaryOraclesEncloseExactResidualAndAllEmulatedUnderflowModes` |
| exponent | frexp-Exponent ohne −1 | `SolverBinary64Tests.ExponentEnclosesEveryBoundaryInItsExactBinaryInterval` |
| contribution | ersten Punktkraftbeitrag in der Anregungsnorm auslassen | `SolverExcitationTests.EnclosesTheExactNormAcrossTheBoundaryFamilies` |
| gamma | `(k+1)·2^-53` statt konservativer Gamma-Schranke | `SolverBinary64Tests.GammaUpperUsesDownwardDenominatorAndUpperQuotient`, insbesondere k=2^52 |
| unloaded | SI-Produkte erst bilden und deren Summe auf null testen | `SolverExcitationTests.EnclosesTheExactNormAcrossTheBoundaryFamilies`: nicht-null winzige UDL mit unterlaufendem SI-Produkt bleibt geladen |
| B3 | Vorzeichen des Punktmomentintegrals umkehren | `SolverBendingNumericsTests.A3PointLoadTermsEncloseRationalPolynomialsWithBothSigns`; E/F-Fälle `end-moment`, `mixed` |
| L1 | äußeres Rampenintegral p=3 mit 24/25 verfälschen | außerhalb des heutigen Umfangs: kein Rampenprimitiv im Kernel; Eingabegrenze durch `MigrationEntriesCannotAcceptFutureFeaturePrimitivesOrSolverLoads` gesichert. Der eigentliche Mechanikmutant ist im DEV-6-Track abzunehmen |
| L2 | Difference-Integral p=4 mit 24/20 verfälschen | `SolverBendingNumericsTests.A4SegmentIntegralsEncloseIndependentMacaulayDifferences`: p=4 innerhalb und jenseits der Last, einschließlich kurzer UDL |

Keine Mutantenoption, keine absichtlich falsche Produktionsvariante und kein
Mutation-Framework eingeführt. Die historische Yun-/Sturm-Gegenprobe in
[state-fpc-oracles.log](results/state-fpc-oracles.log) bleibt Herkunftsevidenz;
die C#-Erwartungen stammen aus festen Faktoren/Analytik, nicht aus `PolynomialRoots`.

## Unabhängigkeit und Reproduktion

`SolverOutputOracle.FromCompatibility` ergänzt das vorhandene exakte Macaulay-Oracle
um globale Kraft-/Momentenbilanz und Verträglichkeit an den Lagern. Seine Unbekannten
sind Anfangskinematik und Reaktionen, nicht lokale Stationszustände. Die rationale
Elimination verwendet `SolverExactLinearAlgebra`. Vier eingefrorene statische Lösungen
prüfen dieses Oracle zusätzlich exakt. Keine Kernelwerte werden zu Golden References.

Die vollständigen Boundary-Fälle verwenden eine symmetrische UDL auf [0,25;1,75]
und Lager bei 1 und `1+gap`. Feste Abstände: 2^-10 (komfortabel),
1,3125·2^-24 (knapp unter τ), 1,25·2^-24 (knapp darüber), 2^-28
(konservative Ablehnung) und 2^-52 (benachbarte mechanische Binary64-Stationen).
Alle prüfen tatsächliche Fehler ≤ Bound; gelieferte zusätzlich Bound ≤ exakte Skala/10^8.
Die ersten vier prüfen tatsächlichen Fehler < τ, also auch konservative Ablehnung.
Kandidatenwerte und beide Ereignisseiten sind eingeschlossen.

V01–V15 und V04-S01/S02/S03 bestehen über denselben Validation-Katalog.
Die V14-N/A-Regel und sämtliche Golden-Toleranzen bleiben unverändert.
Der alte Solver ist ergänzende Regression und weiterhin öffentlicher Produktpfad.

Die Befehle für Product und beide Acceptance-Pfade stehen in
[validation/README.md](../../README.md). Alle Abnahmen benötigen ausschließlich .NET
und eingecheckte Daten; die Python-Dateien werden nur als Provenienz gelesen/gehasht.

Plattform-/Laufergebnisse werden nach Abschluss der nativen Prüfungen und des
CI-Laufs am gepushten HEAD im Sitzungsbericht angegeben. Der vorhandene Windows-x64-
Product-Job führt Primitives, exakte Oracles, Bounds, Verifikation, Gate, CAD-Fälle
und Fehlerklassen aus; der bestehende separate Validation-Job läuft auf Linux.
macOS ARM64 führt beide Lösungen und beide vollständigen Acceptance-Pfade aus.
CI-Finalisierung und Solver-Hash bleiben M4; Produktumschaltung und öffentliches
Fehlermapping M3. Kein Merge nach `main`, keine Future-Features, kein O9-Aufbau.
