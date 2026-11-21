# DEV-85 · Zielarchitektur Single Instance / Multi Workspace

## 1. Ziel

SpanDraft soll mehrere Projekte gleichzeitig in getrennten Fenstern öffnen und bearbeiten können.

Die Zielarchitektur lautet:

- pro gemeinsam genutztem SpanDraft-App-Datenbereich existiert genau eine zuständige Primary-Instanz,
- innerhalb dieser Instanz können mehrere unabhängige Workspace-Fenster existieren,
- ein Workspace besitzt maximal eine `ProjectSession`,
- projektbezogener Zustand ist strikt zwischen Workspaces getrennt,
- anwendungsweiter Zustand wird genau einmal zentral verwaltet,
- mehrere ungespeicherte Projekte und Create-Drafts besitzen unabhängige Recovery-Slots,
- eine Projektdatei kann höchstens einer offenen Session gehören,
- externe Datei- und URI-Aktivierungen werden unabhängig von ihrer Plattformquelle in denselben internen Öffnungspfad überführt,
- normales Fensterschließen und Application Quit verwenden dieselbe Workspace-Close-Logik.

Solver, Projektformat und das fachliche Modell von `ProjectSession` bleiben möglichst unverändert.

Der Umbau betrifft primär Desktop-Lifecycle, Ownership, Aktivierung und Persistenz-Orchestrierung.

---

# 2. Grundprinzipien

## 2.1 Single Process ist Voraussetzung

Die globalen Services der Zielarchitektur setzen voraus, dass nur die Primary-Instanz auf den lokalen SpanDraft-Zustand zugreift.

Dazu gehören:

- Recovery,
- Settings,
- Werkstoffbibliothek,
- Querschnittsbibliothek,
- Recent Projects,
- ProjectPathRegistry.

Single Instance wird deshalb früh eingeführt und ist Voraussetzung für Multi-Recovery und Multi-Workspace.

Die einzelnen Stores werden nicht zusätzlich cross-process-sicher gemacht.

---

## 2.2 Kritische Operationen folgen demselben Transaktionsmuster

Operationen, die über ein `await` hinweg eine Invariante schützen müssen, folgen grundsätzlich:

```text
Preflight
↓
Reservation / Freeze
↓
asynchrone Arbeit
↓
Commit
```

Bei Fehler:

```text
Rollback
↓
Reservation freigeben
```

Dieses Prinzip gilt insbesondere für:

- Projekt öffnen,
- Save As,
- Settingsänderungen,
- Library-Mutationen,
- Recovery-Übergänge,
- Workspace Close.

Ein Zustand wird nicht nur geprüft und anschließend ungeschützt über ein `await` weiterverwendet.

---

## 2.3 Services besitzen Zustand, Coordinators orchestrieren

Persistenz- und State-Services kennen keine Fenster.

Coordinators verbinden globale Services mit Workspace-Lifecycle und UI.

Beispiel:

```text
SettingsService
≠
Fensterverwaltung
```

Der Service besitzt die Settings.

Der `SettingsCoordinator` prüft und verteilt Änderungen auf Workspaces.

---

# 3. Architekturübersicht

```text
Program
│
├── Activation Sources
│     ├── Command Line
│     ├── Windows Instance Broker
│     └── macOS Platform Activation
│
└── Primary Process
      │
      └── App
            │
            └── DesktopApplicationHost
                  │
                  ├── SettingsService
                  ├── MaterialLibraryService
                  ├── SectionLibraryService
                  ├── RecentProjectsService
                  │
                  ├── RecoveryCatalog
                  ├── ProjectPathRegistry
                  │
                  ├── ActivationDispatcher
                  ├── WorkspaceWindowManager
                  ├── ProjectOpenCoordinator
                  ├── SettingsCoordinator
                  ├── AppDialogCoordinator
                  └── ApplicationCloseCoordinator
```

Ein Workspace besteht konzeptionell aus:

```text
WorkspaceWindowHandle
├── WindowId
├── MainWindow
├── MainWindowViewModel
├── WorkspaceCreationReason
└── WorkspaceRecoveryContext?
```

Der eigentliche Projektzustand bleibt:

```text
MainWindowViewModel
└── EditorViewModel
      └── ProjectSession
```

---

# 4. Dependency-Richtung

Die Abhängigkeiten verlaufen grundsätzlich:

```text
File I/O / Stores
        ↓
State Services / Recovery / Registry
        ↓
Coordinators
        ↓
WorkspaceWindowManager
        ↓
MainWindow / ViewModels
```

Konkret:

- `SettingsService` kennt keine Workspaces.
- LibraryServices kennen keine Workspaces.
- `RecoveryCatalog` kennt keine Avalonia-Fenster.
- `ProjectPathRegistry` kennt logische Identitäten und Besitzer-IDs.
- `SettingsCoordinator` arbeitet gegen eine kleine Participant-Schnittstelle.
- `ApplicationCloseCoordinator` arbeitet gegen Workspace-Close-Operationen.
- `AppDialogCoordinator` erhält einen ActiveWindowProvider.

Zyklische Abhängigkeiten zwischen Services und Fensterverwaltung werden vermieden.

---

# 5. DesktopApplicationHost

Der `DesktopApplicationHost` wird Composition Root der laufenden Anwendung.

Er übernimmt:

1. globale Services erzeugen,
2. globale Services initialisieren,
3. RecoveryCatalog initialisieren,
4. Workspace-Infrastruktur erzeugen,
5. ActivationRequests dispatchen,
6. Application-Lifecycle-Zustand halten,
7. Application Quit koordinieren.

Er enthält keine Solver- oder Balkenfachlogik.

`MainWindow` erzeugt künftig keine globalen Stores mehr.

---

# 6. Threading-Modell

UI-nahe Orchestrierung läuft auf dem Avalonia-UI-Thread.

Dazu gehören:

- Workspace-Erzeugung,
- Fensterregistrierung,
- Aktivierung,
- ActivationRequest-Dispatch,
- Application Quit,
- globale UI-Transitions.

Services bleiben Avalonia- und Dispatcher-unabhängig.

Sie serialisieren kritische asynchrone Operationen intern, soweit erforderlich.

Plattform-Aktivierung und IPC verändern niemals direkt Workspace-Zustand, sondern erzeugen immutable ActivationRequests.

---

# 7. Workspace-Modell

UI-Modus und Projekt-Ownership werden getrennt betrachtet.

| UI-Modus | ProjectSession | Recovery |
|---|---|---|
| Start | nein | keine |
| Recovery Center | nein | keine eigene Recovery |
| CreateSetup | nein | Setup-Recovery |
| Editor | ja | Project-Recovery bei dirty Session |
| EditSetup | ja | dieselbe Project-Recovery |
| ReportPreview | ja | dieselbe Project-Recovery |

`EditSetup` besitzt weiterhin dieselbe bestehende `ProjectSession`.

Es entsteht:

- keine zweite Session,
- keine Setup-Recovery,
- keine neue RecoveryId.

---

# 8. Workspace-bezogener Zustand

Folgende Zustände gehören genau einem Workspace:

- `ProjectSession`,
- Editor,
- Undo/Redo,
- Dirty-/Savepoint-Zustand,
- Analyseergebnis,
- Report-Vorschau,
- Create- oder Edit-Setup,
- lokale Eingabepuffer,
- Interaction Performance,
- Diagnostics,
- Workspace-Busy-Zustand,
- Project- oder Setup-RecoveryContext.

Normale Operationen in Workspace A blockieren Workspace B nicht.

---

# 9. Globaler Zustand

Folgende Zustände existieren genau einmal pro Primary:

- Settings,
- Built-in-Werkstoffkatalog,
- User-Materialbibliothek,
- User-Querschnittsbibliothek,
- Recent Projects,
- RecoveryCatalog,
- ProjectPathRegistry,
- Window Registry,
- Activation Queue,
- app-weite Dialoge,
- Application Lifecycle.

„Global“ bedeutet Ownership durch den Host, nicht `static`.

---

# 10. WorkspaceWindowManager

Der `WorkspaceWindowManager` verwaltet ausschließlich Fenster-Lifecycle:

- Workspace erzeugen,
- registrieren,
- deregistrieren,
- aktiven Workspace bestimmen,
- Startfenster verwalten,
- Fenster aktivieren,
- owned/modeless Workspace-Fenster verwalten.

Er liest keine Projektdateien und implementiert kein Save As.

---

# 11. Startfenster-Invariante

Es gilt:

```text
StartOnlyWorkspaceCount <= 1
```

Beim normalen Start ohne Projekt existiert genau ein Startfenster.

`Neu` oder `Öffnen` aus diesem Fenster verwendet denselben Workspace weiter.

---

# 12. New aus einem bestehenden Projekt

```text
Projekt A
→ Ctrl+N
→ neuer Workspace B
→ CreateSetup
```

B erhält temporär:

```text
WorkspaceCreationReason.NewProject
```

Wird das CreateSetup abgebrochen:

- eigens für `New` erzeugter Workspace → Fenster schließen,
- bereits vorhandenes Startfenster → zur Startseite zurückkehren.

Nach erfolgreicher Projekterstellung verliert die CreationReason ihre Bedeutung.

---

# 13. EditSetup

```text
Projekt A
→ Projekt bearbeiten
→ EditSetup
```

Die bestehende `ProjectSession` bleibt bestehen.

Unbestätigte EditSetup-Eingaben erhalten keine separate Setup-Recovery.

---

# 14. Projekt schließen

„Projekt schließen“ entfernt das Projekt aus seinem Workspace.

Danach:

- existiert kein anderer StartOnly-Workspace → Workspace wird Startseite,
- existiert bereits ein StartOnly-Workspace → Workspace wird geschlossen.

---

# 15. Fenster schließen

Fenster-X bedeutet:

```text
diesen Workspace schließen
```

Es führt nicht automatisch zur Startseite.

Die Close-Logik wird nicht in `MainWindow` dupliziert.

---

# 16. Einheitliche WorkspaceCloseTransaction

Jeder Workspace verwendet denselben Close-Mechanismus:

```text
WorkspaceCloseTransaction
```

Sie behandelt:

- Dirty ProjectSession,
- Save / Discard / Cancel,
- CreateSetup,
- EditSetup,
- Recovery-Discard,
- Workspace-Ressourcen,
- Session-Cleanup.

Konzeptionell:

```text
PrepareAsync(reason)
↓
CommitAsync()
```

Bei einem Fehler vor Commit bleibt der Workspace offen.

Ein globales Cross-Workspace-Rollback wird bewusst nicht eingeführt.

---

# 17. Normaler Window Close

Fenster-X:

```text
WorkspaceCloseTransaction
↓
Save / Discard / Cancel
↓
bei Erfolg Workspace schließen
```

Bei `Cancel` bleibt dieses Fenster offen.

---

# 18. Application Quit ist bewusst sequenziell

Application Quit ist keine globale All-or-Nothing-Transaktion.

Der `ApplicationCloseCoordinator` verarbeitet Workspaces nacheinander.

Beispiel:

```text
Workspace A
→ Discard
→ A wird geschlossen

Workspace B
→ Cancel
→ Quit stoppt
```

Ergebnis:

- A bleibt geschlossen,
- B und alle noch nicht bearbeiteten Workspaces bleiben offen.

Die Entscheidung „Discard A“ wird nicht rückwirkend aufgehoben, nur weil der Benutzer bei B später `Cancel` wählt.

Damit entfallen:

- globales PrepareCommit,
- Cross-Workspace-Recovery-Rollback,
- PreparedClose über alle Fenster,
- komplizierte Recovery-Rollback-Kaskaden.

Das reduziert Fehlerpfade erheblich.

---

# 19. CreateSetup beim Close

CreateSetup besitzt keine gespeicherte Projektdatei, aber gegebenenfalls Setup-Recovery.

Beim erfolgreichen Workspace-Close wird der Draft bewusst verworfen.

Bei Cancel bleibt:

- CreateSetup bestehen,
- Recovery bestehen.

---

# 20. EditSetup beim Close

EditSetup gehört zur bestehenden ProjectSession.

Bei Cancel bleiben die Eingaben bestehen.

Bei erfolgreichem Close gehen unbestätigte EditSetup-Eingaben wie heute verloren.

---

# 21. Busy und native Dialoge

Ein Workspace kann nicht geschlossen werden, solange eine nicht sicher unterbrechbare Operation aktiv ist.

Dazu zählen insbesondere native File Picker bzw. vergleichbare modale Systemdialoge.

User Quit wird in diesem Zustand:

- nicht gestartet,
- oder abgelehnt.

Die Anwendung versucht nicht, native Betriebssystemdialoge künstlich von außen zu schließen.

Das entspricht dem heutigen Prinzip, Quit bei Busy-Zuständen zu blockieren.

---

# 22. ProjectIdentity

Eine Projektdatei erhält eine kanonische logische Identität:

```text
ProjectIdentity
```

Für existierende Dateien wird mindestens:

- absoluter Pfad bestimmt,
- Symlink-Auflösung durchgeführt,
- Pfad normalisiert,
- die tatsächliche Case-Semantik des zugrunde liegenden Dateisystems berücksichtigt.

Damit werden beispielsweise unter macOS typische Pfadaliasse wie Symlinks nicht als verschiedene Projekte behandelt.

Für noch nicht existierende Save-As-Ziele wird der kanonische bzw. aufgelöste Parent-Pfad zusammen mit dem Zielnamen verwendet.

Hardlink-Erkennung ist kein Ziel von DEV-85.

---

# 23. ProjectPathRegistry

Die Registry schützt zentral alle Konflikte um eine `ProjectIdentity`:

> Eine ProjectIdentity gehört höchstens einer offenen `ProjectSession`, genau einer laufenden Ownership-Operation oder einem noch unentschiedenen bevorzugten Recovery-Kandidaten.

Die Registry verwaltet keine Fensterobjekte.

Sie verwendet:

- `ProjectIdentity`,
- `WindowId`,
- `OperationId`,
- den Arbitration-Zustand der Identity.

Mögliche fachliche Zustände sind mindestens:

```text
Free
RecoveryPending
Opening
Open
SaveTargetReserved
```

`RecoveryPending` wird vom `RecoveryCatalog` beim Startup bzw. bei Änderungen der Recovery-Priorität in die Registry publiziert. Der Catalog bleibt Eigentümer der Recovery-Daten; die Registry bleibt die einzige Stelle, die daraus die Konfliktentscheidung für Open und Save As ableitet.

---

# 24. Registry-Reservations

Reservations sind explizite Tokens.

Beispiele:

```text
OpenReservation
├── OperationId
└── ProjectIdentity
```

```text
SaveTargetReservation
├── OperationId
├── WindowId
└── ProjectIdentity
```

Nur der Besitzer eines Tokens darf Commit oder Release durchführen.

---

# 25. Registry-Arbitration

Alle Open- und Save-As-Konflikte werden an genau einer Stelle entschieden.

| aktueller Zustand | Open | Save As |
|---|---|---|
| `Free` | `OpenReservation` erwerben | `SaveTargetReservation` erwerben |
| `RecoveryPending` | Recovery Center beim bevorzugten Kandidaten öffnen/aktivieren | ablehnen |
| `Opening` | an bestehendes Single-Flight anschließen | ablehnen |
| `Open` | vorhandenen Workspace aktivieren | ablehnen |
| `SaveTargetReserved` | auf Abschluss warten und danach neu bewerten | ablehnen |

Save As auf den eigenen bereits zugeordneten Pfad ist normales Save und kein Ownership-Wechsel.

`RecoveryCatalog`, Recent Projects, Shell-/Finder-Aktivierung und `ProjectOpenCoordinator` implementieren keine eigenen abweichenden Pfadkonfliktregeln. Sie liefern lediglich Kontext oder Requests an die Registry.

Ändert sich die Recovery-Lage, wird auch der Registry-Zustand aktualisiert:

- bevorzugte Recovery wird erfolgreich wiederhergestellt → `RecoveryPending → Open`,
- bevorzugte Recovery wird verworfen und ein älterer gültiger Kandidat existiert → `RecoveryPending` bleibt bestehen und zeigt auf den neuen bevorzugten Kandidaten,
- letzter gültiger Kandidat wird verworfen → `RecoveryPending → Free`.

---

# 26. ProjectOpenCoordinator

Der Coordinator verbindet:

```text
ProjectIdentity
+
ProjectPathRegistry
+
IProjectFileStore
+
ProjectFileCodec
+
WorkspaceWindowManager
```

Er ist der gemeinsame Pfad für alle normalen Projektöffnungen.

Recovery-Ownership wird nicht separat im Coordinator entschieden. Trifft ein Open auf `RecoveryPending`, liefert die zentrale Registry-Arbitration das Ergebnis „Recovery behandeln“; der Coordinator öffnet dann nicht die Diskversion, sondern delegiert zur passenden Recovery-Oberfläche.

---

# 27. Open-Transaktion

```text
1. Pfad absolutisieren
2. ProjectIdentity bestimmen
3. Registry-Arbitration
4. bei RecoveryPending: Recovery Center beim relevanten Kandidaten aktivieren und beenden
5. ansonsten OpenReservation erwerben
6. Datei lesen
7. ProjectFileCodec.Deserialize
8. Workspace bestimmen oder erzeugen
9. ProjectSession aktivieren
10. Reservation → Open committen
```

Die Reservation bleibt bis zur erfolgreichen Session-Aktivierung bestehen.

Fehler vor Commit führen zum vollständigen Rollback der Reservation.

Damit gilt dieselbe Arbitration unabhängig davon, ob der OpenRequest aus dem Menü, Recent Projects, Windows-Shell, macOS-Aktivierung oder IPC stammt.

---

# 28. Zwei gleichzeitige Opens

Der erste Request erwirbt `Opening`.

Weitere OpenRequests derselben ProjectIdentity hängen sich an das bestehende Single-Flight-Ergebnis.

Bei Erfolg aktivieren sie denselben Workspace.

Bei Fehler wird die Identity wieder frei.

Es entsteht keine zweite Session.

---

# 29. Open während Save As

Trifft Open auf:

```text
SaveTargetReserved
```

wartet es auf Abschluss der Save-As-Operation und wertet die Registry anschließend erneut aus.

Save As erfolgreich:

```text
Ziel ist Open
→ Workspace des neuen Besitzers aktivieren
```

Save As fehlgeschlagen:

```text
Ziel wieder frei
→ normales Open
```

---

# 30. Neues Fenster bei Open

Vor der Erzeugung eines neuen Projektfensters erfolgen:

- File Read,
- Formatprüfung,
- Deserialisierung.

Eine fehlende oder beschädigte Datei hinterlässt kein leeres neues Projektfenster.

Editor-/Analyseinitialisierung kann anschließend im neuen Workspace erfolgen.

Die Reservation bleibt bis zur erfolgreichen Aktivierung bestehen.

---

# 31. Save-As-Transaktion

```text
1. Ziel auswählen
2. ProjectIdentity bestimmen
3. Registry-Arbitration
4. SaveTargetReservation erwerben
5. aktuelle ProjectRevision fixieren
6. Recovery auf vorgesehenen Zielpfad flushen
7. Projektdatei atomar schreiben
8. Session.MarkSaved(...)
9. Registry-Ownership umstellen
10. alten Pfad freigeben
11. Recovery als gespeichert bereinigen
```

Session- und Registry-Commit nach erfolgreichem Write müssen synchron und durch vorherige Invarianten garantiert sein.

---

# 32. Save-As-Rollback

Bei Fehler vor Commit:

- Session behält bisherigen FilePath,
- Registry behält bisherigen Besitzer,
- Zielreservation wird freigegeben,
- Recovery wird auf den bisherigen Sessionzustand zurückgeführt.

---

# 33. Template-/Beispiel-Projekte

Ein mit SpanDraft ausgeliefertes Beispiel oder über DEV-64 geöffnetes Template darf weder als bereits geändert erscheinen noch seine Quelldatei als Save-Ziel übernehmen.

Dafür erhält `ProjectSession` eine eigene Factory mit expliziter Semantik:

```text
ProjectSession.FromTemplate(templateState)
```

Sie erzeugt:

```text
FilePath = null
SavedRevisionId = CurrentRevision.RevisionId
IsDirty = false
```

Damit gilt unmittelbar nach dem Öffnen:

- kein `*` im Fenstertitel,
- keine Recovery nur wegen des Öffnens,
- keine Save-/Discard-Rückfrage beim unveränderten Schließen.

Nach der ersten echten Projektänderung wird die Session wie gewohnt dirty und Recovery wird aktiviert.

Da `FilePath == null` bleibt, führt das erste `Save` zu Save As.

Die ausgelieferte Template-/Beispieldatei wird niemals als Save-Ziel verwendet oder überschrieben.

---

# 34. SettingsService

Der Service hält:

```text
CurrentSettings
LastPersistedSettings
ProcessCulture
SettingsLoadState
```

### CurrentSettings

Autoritativer In-Memory-Zustand.

### LastPersistedSettings

Letzter erfolgreich geschriebener Zustand.

### ProcessCulture

Beim Prozessstart festgelegte Culture und UI Culture.

### SettingsLoadState

Zum Beispiel:

```text
Valid
RecoveredFromInvalidSettings
```

---

# 35. Settings-Patch

Der Settings-Dialog ersetzt keinen vollständigen alten Settings-Snapshot.

Er liefert ein fachlich vollständiges Preferences-Patch für die tatsächlich editierbaren Einstellungen.

Dazu gehören insbesondere:

- ausgewähltes UnitProfile,
- CustomProfile,
- LastStandardProfile,
- SignificantDigits,
- EngineeringNotation,
- UiLanguage,
- RegionalFormat.

`LastAuthor` gehört nicht zum Settings-Patch.

Damit kann ein alter Dialog keinen neueren LastAuthor zurücksetzen.

---

# 36. Settings-Apply

Beim tatsächlichen Apply:

```text
betroffene Workspaces kurz freeze
↓
aktuellen Zustand erneut validieren
↓
Patch mit CurrentSettings mergen
↓
persistieren
↓
Runtime-Zustand synchron committen
↓
Freeze lösen
```

Andere Workspaces bleiben während der gesamten Dauer des geöffneten Settings-Dialogs normal benutzbar.

Der Settings-Dialog ist also nicht global modal.

Nur der eigentliche Commit ist kurz app-weit koordiniert.

---

# 37. Sprache und Regionalformat

Diese Werte werden in `CurrentSettings` und auf Disk aktualisiert.

`ProcessCulture` bleibt während der laufenden Anwendung unverändert.

Neue Fenster desselben Prozesses verwenden weiterhin die aktive ProcessCulture.

Die neue Sprache bzw. das neue Regionalformat gilt ab dem nächsten vollständigen Start.

---

# 38. LastAuthor

Semantik bleibt wie heute:

- erfolgreiche Erstellung eines neuen Projekts aktualisiert LastAuthor,
- Öffnen nicht,
- Editieren nicht,
- bereits offene CreateSetups werden nicht rückwirkend verändert.

Persistenz bleibt best-effort.

---

# 39. Beschädigte Settings

Eine beschädigte `settings.json` führt weiterhin dazu, dass SpanDraft mit Defaults starten kann.

Der Zustand wird jedoch als:

```text
RecoveredFromInvalidSettings
```

markiert.

Automatische Nebenwrites, beispielsweise ein neues `LastAuthor`, dürfen die beschädigte Datei nicht still überschreiben.

Ein neuer gültiger Settingsstand wird erst geschrieben, wenn der Benutzer:

- Einstellungen explizit speichert,
- oder die Einstellungen bewusst zurücksetzt.

Vor diesem ersten expliziten Überschreiben wird die ursprüngliche beschädigte Datei best-effort unter einem diagnostischen Namen gesichert, beispielsweise:

```text
settings.invalid.json
```

Existiert eine solche Sicherung bereits, wird sie nicht still überschrieben; die konkrete Benennung darf einen eindeutigen Zusatz verwenden.

Erst danach wird der neue gültige Settingsstand atomar veröffentlicht.

Damit bleibt der Komfort des Fallbacks erhalten und die ursprüngliche fehlerhafte Datei bleibt für Diagnose und Support nachvollziehbar.

---

# 40. MaterialLibraryService und SectionLibraryService

Beide Services besitzen:

- autoritativen In-Memory-Snapshot,
- Persistenz,
- Mutationsgate,
- Revision,
- Changed-Signal,
- globalen Fehlerzustand.

Der MaterialService besitzt zusätzlich den Built-in-Katalog.

Built-in-Materialien werden einmal pro Anwendung geladen.

---

# 41. Fail-Closed bei Library-Fehlern

Servicezustände:

```text
Available(snapshot)
Unavailable(error)
```

Ein fehlerhafter Load wird nicht als leere schreibbare Library veröffentlicht.

Bei `Unavailable` bleiben mutierende Operationen gesperrt.

---

# 42. Library-Transaktionen

```text
Service-Gate
↓
aktuellen Snapshot prüfen
↓
Konfliktprüfung
↓
neuen Snapshot erzeugen
↓
persistieren
↓
In-Memory-Commit
↓
Revision erhöhen
↓
Changed
```

Bei Persistenzfehler:

- alter Snapshot bleibt,
- keine neue Revision,
- kein Changed.

---

# 43. Library-Konflikte

Optimistische Konflikterkennung gilt für:

- Add,
- Replace,
- Rename,
- Delete.

Ein Editor hält seinen Ausgangszustand.

Stale Operationen dürfen neuere Änderungen nicht still überschreiben.

---

# 44. Library-Werte in Projekten

Globale Library-Änderungen verändern niemals automatisch bereits gespeicherte:

- Materialsnapshots,
- Section-Snapshots.

Offene Auswahloberflächen sehen aktualisierte Kataloge.

Eine bereits zur Bearbeitung geöffnete Kopie bleibt Snapshot und wird beim Commit konfliktgeprüft.

---

# 45. RecentProjectsService

Alle Startseiten verwenden denselben In-Memory-Zustand.

Die bestehende Fehlersemantik bleibt erhalten.

Ein transienter Read-Fehler darf insbesondere nicht dazu führen, dass ein unbekannter Diskzustand mit einer vermeintlich leeren Liste überschrieben wird.

---

# 46. Recovery-Grundmodell

Recovery bleibt best-effort Schutz gegen Crash-Verlust.

Wichtig:

> Eine dirty Session muss im normalen Editierbetrieb nicht zu jedem Zeitpunkt einen exakt aktuellen Recovery-Slot besitzen.

Der bestehende Debounce ist weiterhin zulässig.

Ein normaler temporärer Recovery-Write-Fehler führt nicht automatisch zur Sperrung des Editors.

`RecoveryFaulted` ist ausschließlich ein Sonderzustand nach einem fehlgeschlagenen Close-/Discard-Sicherheitsübergang, bei dem der bisherige Recovery-Schutz bereits aufgehoben wurde und nicht wieder hergestellt werden konnte.

---

# 47. WorkspaceRecoveryContext

Mögliche Zustände:

```text
None
CreateDraftRecovery
ProjectRecovery
```

Start-Workspaces besitzen kein Recovery.

EditSetup verwendet ProjectRecovery der bestehenden Session.

---

# 48. Recovery-Verzeichnis

Aktive Slots liegen unter dem plattformgerechten lokalen SpanDraft-Datenverzeichnis:

```text
SpanDraft/Recovery/

project-{RecoveryId}.json
setup-{RecoveryId}.json
```

Bewusst verworfene Slots:

```text
SpanDraft/Recovery/Discarded/
```

---

# 49. RecoveryId

RecoveryId wird primär durch den kontrollierten Slotnamen definiert.

Die bestehenden Project-/Setup-Recovery-Envelopes müssen dafür nicht unnötig verändert werden.

---

# 50. CreateSetup → Project

CreateSetup und daraus erzeugtes Projekt verwenden dieselbe RecoveryId.

```text
setup-{id}.json
↓
project-{id}.json vollständig publizieren
↓
setup-{id}.json entfernen
```

Bei Crash können beide vorhanden sein.

Beide werden unabhängig validiert.

Regel:

```text
gültiges Project + gültiges Setup
→ Project gewinnt
```

```text
beschädigtes Project + gültiges Setup
→ Setup bleibt Kandidat
```

---

# 51. Legacy-Recovery

Bestehende:

```text
recovery.json
setup-recovery.json
```

werden nicht per Copy/Delete migriert.

Beim tatsächlichen Claim wird eine RecoveryId erzeugt und der Slot innerhalb desselben lokalen Datenbereichs atomar an seinen neuen Platz verschoben.

Schlägt der Move fehl, bleibt der Legacy-Slot bestehen.

---

# 52. RecoveryCatalog

Der Catalog:

- enumeriert Recovery beim App-Start,
- erkennt Legacy-Slots,
- validiert Slots einzeln,
- erkennt Project-/Setup-Paare gleicher RecoveryId,
- erkennt redundante Recovery,
- gruppiert Recoveries nach Originalprojekt,
- bestimmt pro Originalprojekt den bevorzugten gültigen Kandidaten,
- erzeugt RecoveryContexts,
- unterscheidet orphaned und claimed,
- publiziert die bevorzugten noch unentschiedenen Originalpfade als `RecoveryPending` in die `ProjectPathRegistry`.

Er besitzt keine UI und entscheidet nicht selbst über Open- oder Save-As-Konflikte.

Wenn sich ein Recovery-Cluster ändert – etwa durch Restore oder Discard –, aktualisiert der Catalog die Registry so, dass die zentrale Arbitration jederzeit den aktuellen bevorzugten Kandidaten kennt.

---

# 53. Recovery ist kein blockierender Vor-Startup mehr

Die alte Semantik:

```text
Recovery-Dialog
→ Cancel
→ App beendet
```

wird für Multi-Recovery nicht fortgeführt.

Die Primary startet zunächst eine funktionsfähige Anwendung und erreicht einen stabilen `Ready`-Zustand.

Vorhandene Recovery-Kandidaten werden anschließend innerhalb der laufenden Anwendung dargestellt, beispielsweise über:

```text
Recovery Center
```

oder eine entsprechende Startoberfläche.

Damit blockiert Recovery nicht minutenlang:

- den Instance Broker,
- Secondary-Prozesse,
- weitere Aktivierungen.

---

# 54. Recovery-UX

Mehrere Recovery-Kandidaten werden gemeinsam überschaubar dargestellt.

Die UI muss mindestens erlauben:

- einzelnen Kandidaten wiederherstellen,
- einzelnen Kandidaten verwerfen,
- mehrere bzw. alle wiederherstellen,
- beschädigte Kandidaten separat anzeigen,
- Recovery-Oberfläche verlassen, ohne die Anwendung zu beenden.

Als Komfortfunktion kann eine gruppierte Recovery eines Originalprojekts zusätzlich „Alle Varianten dieses Projekts verwerfen“ anbieten. Die Aktion verwendet intern dieselbe vorhandene Discard-Mechanik je Kandidat und verändert das Recovery-Modell nicht.

„Abbrechen“ bedeutet künftig:

```text
Recovery-Entscheidung verlassen
```

nicht:

```text
SpanDraft beenden
```

Unentschiedene Recovery-Slots bleiben erhalten.

Solange mindestens ein `RecoveryPending`-Kandidat existiert, bleibt die Recovery-Oberfläche in derselben Sitzung jederzeit erreichbar. Mindestens vorgesehen sind:

- ein dauerhafter Hinweis bzw. Einstieg auf der Startseite,
- ein Menüeintrag „Wiederherstellung…“.

Trifft ein normaler OpenRequest auf eine `RecoveryPending`-Identity, wird ebenfalls die Recovery-Oberfläche direkt beim relevanten Kandidaten geöffnet bzw. aktiviert.

Damit kann ein Recovery-Konflikt niemals unsichtbar einen normalen Open oder Save As blockieren.

---

# 55. Recovery und normale Dateioperationen

Recovery-Konflikte sind Bestandteil der zentralen `ProjectPathRegistry`-Arbitration.

Besitzt ein gültiger bevorzugter orphaned Recovery-Kandidat einen `OriginalFilePath`, publiziert der `RecoveryCatalog` dessen `ProjectIdentity` als:

```text
RecoveryPending
```

Folgen:

```text
OpenProject(A)
→ Recovery Center beim bevorzugten Kandidaten A aktivieren
```

und:

```text
Save As → A
→ ablehnen
```

Damit kann weder ein Shell-/Recent-Open die unentschiedene Recovery umgehen noch ein anderes Projekt per Save As genau die Originaldatei überschreiben, auf die sich die Recovery bezieht.

Nach Restore oder Discard aktualisiert der Catalog die Registry gemäß dem in §25 definierten Arbitration-Vertrag.

---

# 56. Mehrere Recoveries desselben Originals

Mehrere gültige Recoveries können denselben Originalpfad besitzen.

Ihre Priorität wird deterministisch bestimmt:

```text
1. WrittenAtUtc absteigend
2. RecoveryId als stabiler Tie-Break
```

Der höchstpriorisierte gültige Kandidat ist der bevorzugte Recovery-Kandidat und erzeugt den Registry-Zustand `RecoveryPending` für den Originalpfad.

Weitere gültige Kandidaten werden als ältere Recovery-Varianten behandelt.

Werden sie wiederhergestellt, geschieht dies detached:

```text
ProjectSession.FilePath = null
```

und:

```text
RecoveryOriginPath = ursprünglicher Pfad
```

Wird der bevorzugte Kandidat verworfen, wird unmittelbar der nächstältere gültige Kandidat zum bevorzugten `RecoveryPending`-Kandidaten. Erst wenn kein gültiger Kandidat mehr verbleibt, wird die `ProjectIdentity` in der Registry wieder freigegeben.

Damit hängt Datei-Ownership weder von der zufälligen Klickreihenfolge noch von identischen Zeitstempeln ab.

---

# 57. Redundante Recovery

Eine Project-Recovery wird nur dann automatisch als redundant behandelt, wenn:

1. Originaldatei erfolgreich gelesen wird,
2. sie erfolgreich deserialisiert wird,
3. Projektinhalt vollständig content-equal ist.

Bei:

- Missing,
- I/O-Fehler,
- beschädigtem Original,
- Formatfehler

bleibt Recovery erhalten.

---

# 58. Recovery-Discard

Bewusstes Verwerfen erfolgt nicht als sofortiges Delete.

Stattdessen:

```text
aktiver Recovery-Slot
→ atomarer Move
→ Recovery/Discarded/
```

`Discarded` wird vom Catalog ignoriert.

Physisches Löschen erfolgt später best-effort.

Damit erscheint ein bewusst verworfener Draft nach einem Delete-Fehler nicht als Crash-Recovery wieder.

---

# 59. PrepareDiscard

Vor dem Move:

- ausstehende Debounce-Writes invalidieren,
- laufende Recovery-Publikation geordnet abschließen,
- verhindern, dass ein alter Write den aktiven Slot danach neu erzeugt.

Schlägt `PrepareDiscard` fehl:

```text
Workspace bleibt offen
```

Der Benutzer erhält einen Fehler.

---

# 60. RecoveryFaulted

`RecoveryFaulted` entsteht nicht bei normalen Debounce-Fehlern.

Der Zustand ist ausschließlich für den Sonderfall vorgesehen:

1. aktiver Recovery-Schutz wurde für einen Close-/Discard-Vorgang bereits aufgehoben,
2. der Close kann anschließend nicht abgeschlossen werden,
3. der Recovery-Schutz kann weder zurückverschoben noch neu geschrieben werden.

Nur dann darf der Workspace nicht einfach normal weitereditieren.

Mögliche Remediation:

- Recovery erneut herstellen,
- Projekt erfolgreich speichern / Save As,
- anschließend kontrolliert weiterarbeiten oder schließen.

---

# 61. App-Lifecycle-State

Der Host besitzt:

```text
Starting
Ready
QuitInProgress
ShuttingDown
```

`Ready` wird erreicht, sobald:

- globale Services initialisiert sind,
- Workspace-/Activation-Infrastruktur funktionsfähig ist,
- RecoveryCatalog seine Kandidaten kennt,
- die App Requests sicher queuen und später verarbeiten kann.

Die vollständige Bearbeitung aller Recovery-Kandidaten ist keine Voraussetzung für `Ready`.

---

# 62. Activation statt ausschließlich Broker

Ein `LaunchRequest` kann aus mehreren Quellen stammen.

```text
ActivationSource
├── Startup Command Line
├── Windows Secondary Process
├── macOS File Activation
├── macOS URI Activation
└── macOS Reopen / Activate
```

Alle Quellen erzeugen dasselbe interne Modell.

---

# 63. LaunchRequest

Zulässige Typen:

```text
Activate
OpenProject(absolutePath)
OpenUri(uri)
```

Payload:

- klein,
- versioniert,
- validiert,
- begrenzt.

Keine generische Command-Ausführung.

---

# 64. Windows Activation

Unter Windows kann eine Shell-/Dateiaktivierung einen zweiten Prozess starten.

Deshalb:

```text
Secondary
↓
InstanceCoordinator
↓
Request an Primary
↓
Accepted / Retry / Rejected
```

Der Secondary lädt vorher keine fachlichen SpanDraft-Daten wie Settings, Libraries, Recents oder Recovery.

`OpenUri` ist ab Phase 2 Bestandteil des internen Activation-Vertrags. Die OS-weite Registrierung von `spandraft://` hängt jedoch von der Distribution ab:

| Distribution | Registrierung von `spandraft://` |
|---|---|
| MSIX / Microsoft Store | über `uap:Protocol` im AppxManifest |
| installierte klassische Windows-Version | durch den Installer |
| portable/dev Build ohne Installer | keine automatische systemweite Registrierung |

Eine portable/dev Version schreibt sich nicht ungefragt selbst nach HKCU.

Unabhängig von der OS-Registrierung bleibt `OpenUri` intern vollständig testbar und verwendet denselben `ActivationDispatcher`.

Sind mehrere installierte SpanDraft-Distributionen auf demselben Windows-System registriert, entscheidet Windows anhand seiner aktuellen Datei-/Protokollzuordnung, welche Distribution eine `.spandraft`-Datei oder `spandraft://`-URI erhält. Getrennte tatsächlich verwendete App-Datenbereiche bleiben durch ihre jeweilige `DataScopeId` und Primary vollständig isoliert. DEV-85 führt keine Weiterleitung zwischen Store-, Installer- oder anderen getrennten Installationen ein und entscheidet nicht, welche davon der Betriebssystem-Handler ist.

Für DEV-64 folgt daraus: Eine SpanDraft-Meldungsbox kann nur Fehler behandeln, nachdem eine `spandraft://`-Aktivierung die Anwendung tatsächlich erreicht hat. Ist für die aktuelle Umgebung kein Protocol-Handler registriert, muss die Website/Hilfe diesen Fall selbst verständlich behandeln; die konkrete UX dafür gehört zu DEV-64, nicht zu DEV-85.

---

# 65. macOS Activation

macOS wird nicht künstlich auf den Windows-Prozessmechanismus gezwungen.

App-Bundle-Aktivierungen durch:

- Datei öffnen,
- URI öffnen,
- App erneut aktivieren

werden über die Plattform-/Avalonia-Aktivierung der bestehenden Anwendung übernommen.

Diese Aktivierung erzeugt denselben internen `LaunchRequest`.

Damit existiert ab dem Dispatcher keine Windows-/macOS-Sonderlogik mehr.

Die macOS-Bundle-Konfiguration muss dazu passende:

- Document Types,
- URI Types

deklarieren.

---

# 66. Single-Instance-Election

Die Election muss dieselbe Ressource schützen wie die gemeinsam genutzten lokalen SpanDraft-Daten.

Bevorzugtes Grundmodell:

```text
exklusiv gehaltene Lock-Datei
im SpanDraft-Datenbereich
```

Eigenschaften:

- plattformübergreifend,
- an den tatsächlich verwendeten Datenbereich gekoppelt,
- das Betriebssystem gibt den Lock bei Prozessabsturz frei.

Die erstmalige Initialisierung der Instance-Metadaten erfolgt unter dieser exklusiven Election. Damit können zwei gleichzeitig gestartete erste Prozesse nicht unabhängig unterschiedliche Metadaten für denselben Datenbereich anlegen.

Die konkrete Implementierung muss sicherstellen, dass Store-/MSIX- und andere Distributionsformen nur dann dieselbe Primary teilen, wenn sie tatsächlich denselben Datenbereich verwenden.

---

# 67. DataScopeId und Instance-Metadaten

Der App-Datenbereich erhält eine stabile logische Identität:

```text
DataScopeId
```

Sie wird als kleine Instance-Metadatei im jeweiligen Datenbereich gehalten und kann für:

- Broker-Handshake,
- IPC-Endpunkt,
- Diagnose

verwendet werden.

Erzeugung:

```text
Datenbereich bestimmen
↓
Election-Lock erwerben
↓
DataScopeId vorhanden?
├── ja → lesen
└── nein → atomar erzeugen
```

Der Endpoint-Name wird nicht blind aus einem möglicherweise virtualisierten oder umgeleiteten Pfadstring abgeleitet.

Primary und Secondary müssen im Handshake dieselbe DataScopeId bestätigen.

Ein Secondary darf als ausdrücklich begrenzte Ausnahme zur allgemeinen Datenzugriffsregel ausschließlich die für Election und IPC nötigen Instance-Metadaten lesen. Er liest oder verändert keine fachlichen SpanDraft-Daten wie:

- Settings,
- Libraries,
- Recents,
- Recovery,
- Projektzustände.

Bei getrennten tatsächlich verwendeten MSIX-/Desktop-Datenbereichen entstehen dadurch automatisch unterschiedliche DataScopeIds.

---

# 68. Election und Bootstrap-Endpunkt vor Settings

Die Reihenfolge in `Program` wird geändert.

Heute werden Settings und Culture sehr früh geladen. Künftig muss die Instance-Infrastruktur vorher vollständig genug stehen, dass ein fast gleichzeitig gestarteter Secondary einen gesunden Primary-Startup nicht mit einem IPC-Fehler verwechselt.

Ziel:

```text
Program.Main
↓
ActivationRequest bilden
↓
Datenbereich bestimmen
↓
Election-Lock erwerben bzw. bestehenden Lock erkennen
↓
Primary / Secondary entscheiden
```

Gewinnt der Prozess die Election, wird die minimale Instance-Infrastruktur unmittelbar aufgebaut:

```text
Election-Lock erworben
↓
DataScopeId / Instance-Metadaten unter dem Lock sicherstellen
↓
IPC-Endpunkt öffnen
   Lifecycle = Starting
   Brokerantwort = Retry
↓
Settings laden
↓
Culture konfigurieren
↓
Avalonia / Host starten
↓
Ready
```

Der IPC-Endpunkt gehört damit zur Bootstrap-Infrastruktur und benötigt keine Avalonia- oder Workspace-Instanz. Bis `Ready` nimmt er keine fachliche Verarbeitung vor, sondern beantwortet gültige Zustellversuche mit `Retry`.

Ein Secondary:

```text
Election-Lock ist bereits belegt
↓
nur Instance-Metadaten lesen
↓
IPC-Endpunkt kontaktieren
↓
Accepted / Retry / Rejected gemäß Broker-Vertrag
```

Ein sehr kurzer Zustand, in dem der Election-Lock bereits gehalten wird, DataScopeId/Instance-Metadaten oder IPC-Endpunkt aber noch nicht sichtbar sind, ist ein erwartetes Bootstrap-Race und noch kein Kommunikationsfehler. Das genaue Verhalten ist in §70.2 festgelegt.

Damit erfolgt die Primary-Entscheidung und Bereitstellung des Kommunikationskanals, bevor ein Secondary fachliche lokale SpanDraft-Daten lädt oder verändert und bevor die Primary Settings lädt.

---

# 69. Broker-Antworten

```text
Accepted
Retry
Rejected
```

### Accepted

Primary besitzt den Request sicher in ihrer Application Queue.

Diese Semantik gilt uneingeschränkt ab Phase 6, sobald Recovery Teil der laufenden Anwendung ist und die Primary trotz unentschiedener Recovery-Kandidaten stabil `Ready` sein kann. Eine `Ready`-Primary darf Requests dann unmittelbar bestätigen.

In den Entwicklungsphasen 2 bis 5 läuft dagegen noch die heutige blockierende Legacy-Startup-Recovery. Solange diese Recovery-Entscheidung offen ist, bleibt die Primary im Zustand `Starting`; externe Broker-Requests erhalten `Retry` und werden ausdrücklich nicht mit `Accepted` bestätigt. Damit kann ein anschließendes „Abbrechen“ der Legacy-Recovery keinen bereits übernommenen Request verlieren.

### Retry

Primary befindet sich gerade im Beenden oder ist nicht sicher empfangsbereit.

`Retry` überträgt keine Verantwortung für den Request an die Primary. Bis zu einem finalen `Accepted` oder `Rejected` bleibt der Secondary Eigentümer seines `LaunchRequest` und darf ihn nicht still verwerfen. Das konkrete Wiederholungsverhalten folgt dem Secondary-Retry-Vertrag in §70.1.

### Rejected

Ungültiger oder inkompatibler Request.

`Rejected` führt nicht zur zweiten Primary.

Der Benutzer erhält bei einem sichtbaren Startversuch eine verständliche Meldung statt eines scheinbar wirkungslosen Doppelklicks.

---

# 70. Broker-Protokoll

Das Protokoll bleibt bewusst klein und stabil.

Es enthält nur:

- DataScopeId,
- Protokollversion,
- Requesttyp,
- Payload,
- Response.

Änderungen an normalen SpanDraft-Projektversionen sollen nicht automatisch neue IPC-Protokollversionen erfordern.

## 70.1 Secondary-Retry-Vertrag

Der Secondary bleibt solange Eigentümer seines `LaunchRequest`, bis genau eines der folgenden Ereignisse eintritt:

- `Accepted`: Die Primary hat die Verantwortung übernommen.
- `Rejected`: Der Request ist endgültig abgelehnt und der Secondary zeigt bei einem sichtbaren Startversuch eine verständliche Meldung.
- der Secondary gewinnt selbst die Election und wird Primary: Derselbe Request wird zu seinem initialen LaunchRequest.
- der Benutzer bricht einen Startup bewusst ab, nachdem dieser Secondary selbst Primary geworden ist: Der noch nicht akzeptierte initiale Request wird zusammen mit diesem Startup verworfen. Das ist eine explizite Benutzerentscheidung und kein stiller Transportverlust.

Ein `Retry` beendet den Secondary nicht. Stattdessen gilt:

```text
Retry
↓
mit begrenztem/adaptivem Backoff warten
↓
Election/Primary-Liveness erneut prüfen
```

Vor jedem neuen Zustellversuch wird die Election erneut bewertet:

```text
Election-Lock frei
→ Secondary darf Lock erwerben
→ wird Primary
→ eigener Request = initialer LaunchRequest
```

```text
Primary hält Lock und antwortet weiterhin Retry
→ Request bleibt beim Secondary
→ später erneut zustellen
```

Für die Architektur wird kein fixer Gesamt-Timeout vorgeschrieben, solange eine lebende Primary nachweislich auf Requests mit `Retry` antwortet. Die Implementierung verwendet einen begrenzten bzw. adaptiven Backoff, damit wartende Secondary-Prozesse weder die Primary noch das System mit engen Retry-Schleifen belasten.

Außerhalb des in §70.2 beschriebenen unmittelbaren Bootstrap-Races gilt: Hält eine Primary den Election-Lock, bleibt aber nach der vorgesehenen Bootstrap-/Kommunikationsfrist über den IPC-Endpunkt nicht erreichbar oder antwortet nicht, darf der Secondary keine zweite Primary erzwingen. Er beendet den Zustellversuch mit einer sichtbaren Fehlermeldung.

Wenn die Primary `Retry` wegen eines sichtbaren blockierenden Zustands zurückgibt, aktiviert sie best-effort ihren relevanten TopLevel. In den Phasen 2 bis 5 ist das insbesondere der offene Legacy-Recovery-Dialog. Diese Aktivierung ist eine UX-Hilfe und keine Voraussetzung für die Request-Konsistenz.

Für den Übergangsfall der Phasen 2 bis 5 gilt damit ausdrücklich:

```text
Primary A zeigt Legacy-Recovery
Secondary B möchte Projekt X öffnen
→ A antwortet Retry
→ B bleibt Eigentümer von OpenProject(X)
→ Benutzer bricht Legacy-Recovery in A ab
→ A beendet sich, Election-Lock wird frei
→ B gewinnt gegebenenfalls die Election
→ B wird Primary
→ OpenProject(X) bleibt sein initialer Request
→ die weiterhin vorhandene Legacy-Recovery wird erneut angezeigt
```

Starten mehrere wartende Secondaries nach dem Ende der alten Primary gleichzeitig eine neue Election, gewinnt genau einer. Die übrigen stellen ihre Requests anschließend an diese neue Primary zu. Es entsteht keine zweite vollwertige Primary.

## 70.2 Bootstrap-Endpunkt und Kommunikationsfehler

Der Primary öffnet seinen IPC-Endpunkt unmittelbar nach erfolgreicher Election und dem sicheren Erzeugen bzw. Lesen der Instance-Metadaten, noch bevor Settings geladen, Culture angewendet oder Avalonia gestartet wird.

Solange der Application-Lifecycle noch `Starting` ist, antwortet dieser minimale Endpoint auf gültige Zustellversuche ausschließlich mit `Retry`. Er übernimmt keinen Request und benötigt keinen UI-Zustand.

Für den Secondary werden zwei Fälle unterschieden:

```text
Election-Lock belegt
+ Instance-Metadaten oder IPC-Endpunkt noch nicht verfügbar
+ Primary befindet sich offensichtlich im unmittelbaren Bootstrap
→ erwartetes Bootstrap-Race
→ mit begrenztem/adaptivem Backoff erneut prüfen
→ keine Fehlermeldung
```

und:

```text
Election-Lock bleibt belegt
+ Bootstrap-/Kommunikationsfrist ist überschritten
+ IPC-Endpunkt bleibt nicht erreichbar oder antwortet nicht
→ Kommunikationsfehler einer vermutlich hängenden/defekten Primary
→ sichtbare Fehlermeldung
→ niemals eine zweite Primary erzwingen
```

Die konkrete Dauer der Bootstrap-/Kommunikationsfrist ist Implementierungsdetail. Der Architekturvertrag verlangt nur, dass ein normaler Kaltstart nicht als Fehler klassifiziert wird und ein dauerhaft nicht erreichbarer Lock-Besitzer den Secondary nicht zu einer zweiten Primary verleitet.

Bootstrap- und IPC-Fehlermeldungen des Secondary verwenden bewusst die System-UI-Kultur. Der Secondary lädt keine SpanDraft-Settings; seine Sprache kann daher von der in SpanDraft gespeicherten UI-Sprache abweichen. Das ist beabsichtigt.

---

# 71. Zwischenphasen vor Multiwindow

Sobald Single Instance aktiv ist, dürfen externe OpenRequests auch im noch vorhandenen Single-Window-Zwischenstand nicht verloren gehen.

Dafür besitzt der Host eine Activation Queue.

Für die Phasen 2 bis 5 gilt jedoch eine bewusste Übergangsregel, solange noch die heutige blockierende Legacy-Startup-Recovery verwendet wird:

```text
Legacy-Recovery-Entscheidung offen
→ AppLifecycle = Starting
→ externer Broker-Request = Retry
```

Der Request wird in diesem Zustand nicht als übernommen bestätigt und nicht in die normale Activation Queue aufgenommen. Die Verantwortung bleibt beim Secondary gemäß §70.1. Nach erfolgreicher Legacy-Recovery-Entscheidung erreicht die Primary `Ready`; der Secondary stellt denselben Request erneut zu. Beendet „Abbrechen“ die Primary nach heutiger Semantik, prüft der Secondary die Election erneut und kann nach Freigabe des Locks selbst Primary werden; derselbe Request wird dann sein initialer LaunchRequest.

Außerhalb dieser Legacy-Startup-Phase gilt bereits in den Single-Window-Zwischenständen die normale Queue-Semantik. Kann der aktuelle Workspace den Request gerade nicht verarbeiten:

```text
Request bleibt queued
```

Sobald der Workspace wieder verfügbar ist, wird er verarbeitet.

Muss dafür das aktuelle Projekt verlassen werden, gilt zunächst weiterhin die bestehende Save/Discard/Cancel-Semantik.

Mit Phase 6 entfällt die Übergangsregel vollständig: Recovery ist dann Teil der laufenden Anwendung, die Primary kann trotz offener Recovery-Kandidaten `Ready` sein und bestätigte Requests sicher in der Application Queue halten.

Nach Aktivierung von Multiwindow ändert sich nur die Präsentationsstrategie:

```text
neuer Workspace statt aktuelles Projekt ersetzen
```

Der Transport- und Queue-Vertrag bleibt ansonsten unverändert.

---

# 72. Activation während kurzer globaler Transaktionen

Während beispielsweise eines Settings-Commits:

```text
LaunchRequest
→ Queue
```

Er geht nicht verloren.

Nach Ende der globalen Transaktion wird er verarbeitet.

---

# 73. Activation während Quit

Sobald User Quit tatsächlich gestartet wurde:

```text
neue externe Aktivierung
→ Retry
```

Ein Secondary behält bei `Retry` die Verantwortung für seinen Request. Nach Ende der alten Primary prüft er die Election erneut; gewinnt er sie, wird derselbe Request zu seinem initialen LaunchRequest. Andernfalls stellt er ihn an die neue Primary zu.

---

# 74. User Quit

User Quit wird plattformspezifisch ausgelöst, aber intern einheitlich verarbeitet.

Quellen können sein:

- Ctrl+Q,
- Cmd+Q,
- App-Menü,
- Dock-/Plattform-Quit,
- entsprechendes Application-Lifetime-Signal.

Vor der ersten Save-/Discard-/Cancel-Rückfrage führt der `ApplicationCloseCoordinator` einen best-effort Recovery-Checkpoint aus:

```text
alle aktuell dirty Project-/Create-RecoveryContexts
→ Flush / Drain
```

Ein fehlgeschlagener Recovery-Flush wird angezeigt bzw. protokolliert, macht User Quit aber nicht automatisch unmöglich. Der Benutzer kann anschließend weiterhin gezielt speichern, verwerfen oder abbrechen.

Bei einem synchronen Plattformsignal wie `ShutdownRequested` wird der unmittelbare Shutdown zunächst synchron abgebrochen. Anschließend startet genau ein asynchroner `ApplicationCloseCoordinator`.

Solange:

```text
AppLifecycle == QuitInProgress
```

startet ein weiteres Quit-Signal keinen zweiten Coordinator.

Der Coordinator verarbeitet anschließend die Workspaces sequenziell.

---

# 75. Sequenzieller Quit

Die Reihenfolge ist deterministisch:

```text
1. aktuell aktiver Workspace
2. danach die übrigen Workspaces in stabiler Fensterreihenfolge
```

Clean Workspaces können ohne Rückfrage geschlossen werden.

Für dirty Workspaces läuft jeweils dieselbe `WorkspaceCloseTransaction`.

Beispiel:

```text
A dirty
B dirty
C clean
```

A:

```text
Discard
→ Recovery tombstonen
→ A schließen
```

B:

```text
Cancel
→ Quit beenden
```

C bleibt offen, sofern es bis dahin noch nicht an der Reihe war.

A wird nicht wiederhergestellt.

Damit ist Quit:

- einfach,
- deterministisch,
- ohne Cross-Workspace-Rollback.

Der Recovery-Checkpoint vor der Dialogserie reduziert zusätzlich das Risiko, dass ein unerwarteter Prozessabbruch während eines längeren Quit-Ablaufs neuere Änderungen ohne Recovery hinterlässt.

---

# 76. System Shutdown

Ein echter OS-Shutdown bzw. eine Abmeldung wird vom normalen User Quit unterschieden, soweit die Plattform/Lifetime dies zuverlässig signalisiert.

System Shutdown verwendet keinen langen Save-/Discard-Dialogablauf.

Stattdessen:

```text
neue Mutationen stoppen
↓
dirty RecoveryContexts möglichst unmittelbar flushen/drainen
↓
OS-Shutdown fortsetzen
```

Priorität ist Crash-Recoverability.

Da auch normaler User Quit vor der ersten Rückfrage einen Recovery-Checkpoint ausführt, bleibt der Schutz robust, selbst wenn eine Plattform einen Beendigungsfall nur eingeschränkt differenzieren oder der Prozess während der Dialogserie unerwartet beendet wird.

---

# 77. AppDialogCoordinator

Anwendungsweite Dialoge:

- Settings,
- About,
- Versionshinweise.

Je Dialogtyp existiert höchstens eine Instanz.

Der Settings-Dialog ist nur zu seinem Owner modal.

Andere Workspaces bleiben benutzbar.

Bei User Quit gilt:

- ist ein app-weites oder natives Dialog-/Busy-Szenario nicht sicher abbrechbar, startet Quit nicht,
- andernfalls kann der Coordinator den app-weiten Dialog kontrolliert schließen.

---

# 78. Workspace-eigene Hilfsfenster

Diagnostics und andere modeless TopLevels gehören ihrem Workspace.

Beim erfolgreichen Workspace-Close werden sie:

- geschlossen,
- deregistriert,
- disposed.

Kein Hilfsfenster überlebt seinen Workspace.

---

# 79. `ApplicationLifetime.MainWindow`

`desktop.MainWindow` ist keine fachliche Wahrheit mehr.

Nicht darüber laufen:

- Projektlookup,
- aktiver Workspace,
- Dialogrouting,
- ActivationRequests.

Diese Aufgaben gehören zum WorkspaceWindowManager bzw. AppDialogCoordinator.

---

# 80. Nicht-Ziele

DEV-85 schützt nicht gegen:

- mehrere Benutzer oder Rechner, die dieselbe Projektdatei auf einem Netzlaufwerk bearbeiten,
- externe Änderungen einer bereits geöffneten `.spandraft`-Datei,
- Hardlink-Aliase derselben Datei,
- eingebauten Projektvergleich,
- Diff-Ansicht,
- Tabs,
- parallele Solverarchitektur,
- Cross-Process-State-Synchronisation,
- eigene Datenbank für Settings oder Libraries,
- Event-Bus-Framework,
- DI-Container,
- Festlegung, welche von mehreren parallel installierten SpanDraft-Distributionen Windows als Datei- oder Protocol-Handler auswählt.

Solche Themen können bei Bedarf separat behandelt werden.

---

# 81. Verbindliche Invarianten

Nach DEV-85 gelten:

1. Ein SpanDraft-Datenbereich besitzt höchstens eine zuständige Primary.

2. Secondary-Prozesse lesen oder verändern keine fachlichen SpanDraft-Daten; ausschließlich die für Election und IPC erforderlichen Instance-Metadaten dürfen gelesen werden.

3. Plattform-Aktivierungen und Broker-Aktivierungen enden im selben internen LaunchRequest-Pfad.

4. Mehrere Workspace-Fenster können gleichzeitig existieren.

5. Workspace-UI-Modus und ProjectSession-Ownership sind getrennt.

6. EditSetup erzeugt keine zweite Session und keine Setup-Recovery.

7. Eine ProjectIdentity besitzt zu jedem Zeitpunkt genau einen zentral arbitrierten Zustand: frei, `RecoveryPending`, laufend reserviert oder einer offenen Session zugeordnet.

8. `RecoveryPending` schützt den Originalpfad sowohl vor normalem Open als auch vor Save As.

9. Zwei konkurrierende Opens erzeugen höchstens eine Session.

10. Save As kann keinen fremd belegten, reservierten oder durch Recovery geschützten Pfad übernehmen.

11. ProjectIdentity berücksichtigt für existierende Dateien Symlink-Auflösung und passende Dateisystem-Case-Semantik.

12. Template-/Beispiel-Projekte starten clean, besitzen keinen FilePath und verwenden niemals die Quelldatei als Save-Ziel.

13. Settings-Patches überschreiben keine fremden globalen Felder.

14. ProcessCulture bleibt während der Prozesslaufzeit konstant.

15. Beschädigte Settings werden nicht durch automatische Nebenwrites still überschrieben und vor bewusstem Ersatz diagnostisch gesichert.

16. Library-Mutationen werden erst nach erfolgreicher Persistenz sichtbar.

17. Stale Library-Operationen überschreiben keine neueren Änderungen.

18. Library-Änderungen verändern keine bestehenden Projekt-Snapshots.

19. Fehlerhaft geladene Libraries bleiben fail-closed.

20. Start-Workspaces besitzen keine Recovery.

21. CreateSetup besitzt Setup-Recovery.

22. EditSetup benutzt Project-Recovery.

23. Recovery ist im normalen Editierbetrieb best-effort und darf zeitlich hinter dem aktuellen Zustand liegen.

24. Ein normaler Recovery-Debounce-Fehler sperrt den Editor nicht automatisch.

25. Ein bewusst verworfener Recovery-Slot erscheint nicht erneut als Crash-Recovery.

26. Create→Project erzeugt kein Recovery-Loch.

27. Mehrere Recoveries desselben Originals besitzen eine deterministische Priorität nach `WrittenAtUtc` und RecoveryId.

28. Wird der bevorzugte Recovery-Kandidat verworfen, übernimmt automatisch der nächstältere gültige Kandidat den `RecoveryPending`-Schutz.

29. Nur die bevorzugte Recovery darf den Originalpfad beanspruchen; weitere Varianten werden detached geöffnet.

30. Recovery Center bleibt während derselben Sitzung erreichbar, solange unentschiedene Kandidaten existieren.

31. Startup-Recovery blockiert nicht den gesamten App-Startup.

32. Recovery-Cancel beendet nicht die Anwendung.

33. Window Close und Application Quit verwenden dieselbe WorkspaceCloseTransaction.

34. Application Quit arbeitet sequenziell, beginnt beim aktiven Workspace und benötigt keinen Cross-Workspace-Rollback.

35. Vor den Quit-Rückfragen wird dirty Recovery best-effort geflusht/gedraint.

36. Ein synchrones `ShutdownRequested`-Signal startet höchstens einen asynchronen Quit-Coordinator.

37. Native nicht unterbrechbare Dialoge verhindern den Start eines User Quit.

38. Externe ActivationRequests gehen auch in Single-Window-Zwischenphasen nicht still verloren; während der Legacy-Startup-Recovery der Phasen 2 bis 5 werden sie nicht vorzeitig akzeptiert, sondern mit `Retry` an den Secondary zurückgegeben.

39. Bis `Accepted` oder `Rejected` bleibt der Secondary Eigentümer seines LaunchRequest. Bei `Retry` hält er den Request, prüft Election/Primary-Liveness erneut und stellt später erneut zu oder übernimmt ihn nach gewonnener Election als eigenen initialen Request.

40. Eine neue Primary öffnet ihren minimalen IPC-Endpunkt unmittelbar nach Election und Instance-Metadaten, noch vor Settings, Culture und Avalonia; solange sie `Starting` ist, antwortet sie mit `Retry`.

41. Ein kurzzeitig fehlender Endpoint bzw. noch nicht sichtbare Instance-Metadaten bei bereits gehaltenem Election-Lock gelten während des unmittelbaren Bootstrap als Retry-Zustand und nicht als Fehler.

42. Ein Secondary erzwingt bei dauerhaft nicht erreichbarer, aber weiterhin den Election-Lock haltender Primary niemals eine zweite Primary; ein echter Kommunikationsfehler endet sichtbar.

43. Ein abgelehnter oder inkompatibler Brokerrequest startet keine zweite Primary.

44. Windows-URI-Registrierung ist distributionsabhängig; der interne `OpenUri`-Vertrag ist davon unabhängig.

45. Kritische asynchrone Operationen folgen Preflight → Reservation → Async Work → Commit/Rollback.

---

# 82. Verbindliche Testgruppen

## Single Instance und Activation

- Election erfolgt vor Settings-Load.
- Primary öffnet den minimalen IPC-Endpunkt nach Election/Instance-Metadaten und vor Settings-Load.
- Endpoint antwortet während `Starting` mit `Retry`, ohne Avalonia-/Workspace-Abhängigkeit.
- Zwei gleichzeitige erste Starts erzeugen unter dem Election-Lock genau eine DataScopeId.
- Zwei nahezu gleichzeitig gestartete Prozesse ergeben genau eine Primary; der zweite Request bleibt erhalten und es erscheint kein falscher IPC-Fehler während des Primary-Bootstrap.
- Election-Lock bereits belegt, Instance-Metadaten/Endpoint noch nicht sichtbar → Bootstrap-Retry statt sichtbarem Fehler.
- Secondary liest nur Instance-Metadaten, nicht Settings/Libraries/Recents/Recovery.
- Secondary-Bootstrap-/IPC-Meldungen verwenden die System-UI-Kultur ohne Laden der SpanDraft-Settings.
- Windows Secondary → Request an Primary.
- macOS File Activation → derselbe LaunchRequest-Dispatcher.
- macOS URI Activation → derselbe Dispatcher.
- DataScopeId stimmt im Handshake überein.
- getrennte Datenbereiche erhalten getrennte DataScopeIds.
- inkompatibler Request erzeugt keine zweite Primary.
- sichtbarer Rejected-Fall liefert verständliche Fehlermeldung.
- Request während Quit → Retry.
- `Retry` lässt Ownership des LaunchRequest beim Secondary.
- Primary antwortet wiederholt `Retry` → Secondary hält Request und verwendet begrenzten/adaptiven Backoff.
- Primary gibt Election-Lock frei → wartender Secondary kann Primary werden und behält denselben Request als initialen LaunchRequest.
- zwei wartende Secondaries nach Ende der alten Primary → genau einer gewinnt die Election; der andere stellt an die neue Primary zu.
- Election-Lock bleibt nach der Bootstrap-/Kommunikationsfrist belegt, IPC antwortet weiterhin nicht → sichtbarer Fehler, keine zweite Primary.
- `Retry` während sichtbarer Legacy-Recovery aktiviert best-effort den Recovery-Dialog.
- Request während Settings-Commit → Queue.
- `OpenUri` funktioniert intern unabhängig von der OS-weiten Protocol-Registrierung.

## Zwischenphasen

- Broker aktiv, Multiwindow noch nicht aktiv.
- Phasen 2–5: Legacy-Recovery offen → Primary bleibt `Starting`.
- Phasen 2–5: externer Request während Legacy-Recovery → `Retry`, niemals `Accepted`.
- Legacy-Recovery `Cancel` beendet Primary → kein zuvor bestätigter Request geht verloren.
- Legacy-Recovery `Cancel` beendet Primary bei wartendem Secondary → Secondary prüft Election erneut, kann Primary werden und behält denselben Request als initialen LaunchRequest.
- neue Primary sieht den unveränderten Legacy-Recovery-Slot erneut; dies ist erwartetes Übergangsverhalten.
- nach erfolgreicher Legacy-Recovery → `Ready`, erneute Zustellung funktioniert.
- Workspace busy außerhalb der Legacy-Recovery → Request bleibt queued.
- Request wird später verarbeitet.
- kein OpenRequest geht durch `CanOpen == false` still verloren.
- ab Phase 6: offene Recovery-Kandidaten blockieren `Ready` nicht mehr.

## Workspace

- höchstens ein StartOnly-Workspace.
- New aus Projekt erzeugt CreateSetup-Workspace.
- Cancel schließt nur eigens erzeugten Workspace.
- EditSetup behält vorhandene Session.
- EditSetup erzeugt keine Setup-Recovery.

## ProjectIdentity / Registry

- gleicher Pfad mit unterschiedlicher Case-Darstellung auf case-insensitive Dateisystem → eine Identity.
- Symlink und Ziel → eine Identity.
- zwei parallele Opens → eine Session.
- Open trifft `Opening` → Single-Flight.
- Open trifft `SaveTargetReserved` → wartet und bewertet neu.
- Open trifft `RecoveryPending` → Recovery Center beim relevanten Kandidaten.
- Save As auf `Open`, `Opening`, `SaveTargetReserved` oder `RecoveryPending` → abgelehnt.
- Restore des bevorzugten Recovery-Kandidaten überführt `RecoveryPending → Open`.
- Verwerfen des letzten Recovery-Kandidaten überführt `RecoveryPending → Free`.

## Template

- `ProjectSession.FromTemplate` startet clean.
- `FilePath == null`.
- `IsDirty == false`.
- kein `*` direkt nach dem Öffnen.
- keine Recovery direkt nach dem Öffnen.
- unverändertes Schließen ohne Save-Rückfrage.
- erste echte Änderung macht die Session dirty.
- erstes Save → Save As.
- Quelldatei bleibt unverändert.

## Settings

- SettingsPatch enthält CustomProfile/LastStandardProfile korrekt.
- neuer LastAuthor wird durch alten Settings-Dialog nicht überschrieben.
- andere Workspaces bleiben während geöffnetem Settings-Dialog benutzbar.
- Apply validiert alle betroffenen Workspaces erneut.
- beschädigte Settings → Defaults + Invalid-State.
- automatischer LastAuthor-Write überschreibt beschädigte Datei nicht.
- erstes explizites Settings-Save sichert die beschädigte Originaldatei diagnostisch.
- vorhandene Diagnose-Sicherung wird nicht still überschrieben.
- explizites Settings-Save darf danach gültigen neuen Zustand schreiben.

## Libraries

- Add/Replace/Rename/Delete konfliktgeprüft.
- Write-Fehler verändert In-Memory-Zustand nicht.
- beschädigte Library bleibt fail-closed.
- bestehende Projektsnapshots bleiben unverändert.
- offene Auswahl aktualisiert Libraryrevision.

## Recovery

- mehrere Recovery-Slots.
- Create→Project.
- beschädigter Slot isoliert.
- mehrere Recoveries desselben Originalpfads.
- Priorität: `WrittenAtUtc`, dann RecoveryId.
- neuester/hochpriorisierter gültiger Slot erzeugt `RecoveryPending`.
- beim Verwerfen des bevorzugten Kandidaten wird der nächste gültige Kandidat bevorzugt.
- ältere Recovery wird detached geöffnet.
- RecoveryOriginPath bleibt erhalten.
- Redundant-Recovery nur bei erfolgreichem Content-Equal.
- OpenProject mit `RecoveryPending` öffnet nicht still die Diskversion.
- Save As auf `RecoveryPending` wird abgelehnt.
- Recovery Center bleibt nach Verlassen über Startseite/Menü erreichbar.
- Recovery-Cancel beendet nicht die App.
- beschädigte Recovery kann einzeln verworfen werden.
- Legacy-Move erfolgreich/fehlgeschlagen.
- PrepareDiscard verhindert Wiederauftauchen.
- normaler Debounce-Fehler lässt Editor weiterarbeiten.
- RecoveryFaulted nur nach fehlgeschlagenem Close-/Discard-Sicherheitsübergang.

## Close / Quit

- Window Close verwendet WorkspaceCloseTransaction.
- Discard tombstoned Recovery vor Close.
- Cancel lässt Workspace offen.
- vor der ersten Quit-Rückfrage werden dirty Recoveries best-effort geflusht/gedraint.
- Quit-Reihenfolge beginnt beim aktiven Workspace.
- clean Workspace schließt ohne Rückfrage.
- sequenzieller Quit: A Discard, B Cancel → A geschlossen, B offen.
- CreateSetup-Close.
- EditSetup-Close.
- offener nativer File Picker verhindert Quit.
- `ShutdownRequested` wird synchron gecancelt und genau ein asynchroner Coordinator gestartet.
- zweites Quit-Signal während `QuitInProgress` startet keinen zweiten Coordinator.
- Workspace-Hilfsfenster werden geschlossen.
- echter OS-Shutdown versucht Recovery zu flushen statt Save-/Discard-Dialogserie.

## Packaging / Protocol Activation

- MSIX-Manifest registriert `spandraft://` über `uap:Protocol`, sobald DEV-64 ausgeliefert wird.
- klassischer Installer registriert das Protocol für die installierte Version.
- portable/dev Build registriert sich nicht ungefragt systemweit.
- macOS-Bundle deklariert passende Document-/URI-Types.

---

# 83. Implementierungsreihenfolge

## Phase 1 – Application Ownership

Noch Single Window.

Einführen:

- DesktopApplicationHost,
- klare Constructor-Injection,
- Application-Lifecycle-State,
- MainWindow verliert globale Store-Erzeugung.

## Phase 2 – Activation und minimale Single Instance

Noch Single Window.

Einführen:

- DataScope,
- Election-Lock,
- unter dem Lock erzeugte/gelesene DataScopeId und Instance-Metadaten,
- Primary-/Secondary-Election,
- minimaler IPC-Endpunkt unmittelbar nach gewonnener Election und vor Settings-Load,
- `Starting`-Responder (`Retry`) ohne Avalonia-Abhängigkeit,
- Bootstrap-Race-Behandlung für noch nicht sichtbare Metadaten/Endpoint,
- ActivationDispatcher,
- LaunchRequest `Activate`,
- LaunchRequest `OpenProject`,
- LaunchRequest `OpenUri`,
- Windows IPC/Broker,
- macOS Plattform-Aktivierungsquelle,
- Activation Queue.

Ab dieser Phase gilt:

```text
ein Datenbereich
→ eine Primary
```

und kein externer Request darf verloren gehen.

Der minimale IPC-Endpunkt steht bereits vor Settings/Culture/Avalonia zur Verfügung. Ein Secondary, der unmittelbar nach der Election startet, erhält deshalb während des normalen Primary-Bootstrap `Retry`; ein kurzzeitig noch fehlender Endpoint bzw. noch nicht sichtbare Instance-Metadaten werden gemäß §70.2 ebenfalls als Bootstrap-Race behandelt und lösen noch keinen sichtbaren Fehler aus.

Solange in den Phasen 2 bis 5 noch die blockierende Legacy-Startup-Recovery aktiv ist, bleibt die Primary während einer offenen Recovery-Entscheidung `Starting` und antwortet externen Broker-Requests mit `Retry` statt `Accepted`. Der Secondary behält dabei gemäß §70.1 die Verantwortung für seinen Request, prüft Election/Primary-Liveness erneut und stellt später erneut zu oder übernimmt denselben Request nach gewonnener Election als initialen LaunchRequest. Erst nach erfolgreicher Legacy-Recovery-Initialisierung gilt die normale Activation-Queue-Semantik. Diese Übergangsregel entfällt mit Phase 6.

Die OS-weite `spandraft://`-Registrierung ist noch nicht Voraussetzung; der interne `OpenUri`-Transport steht jedoch bereits.

## Phase 3 – Shared Services

Einführen:

- SettingsService,
- SettingsCoordinator,
- MaterialLibraryService,
- SectionLibraryService,
- RecentProjectsService,
- Invalid-Settings-State und diagnostische Sicherung vor bewusstem Ersatz.

Bestehendes Single-Window-Verhalten bleibt sichtbar erhalten.

## Phase 4 – ProjectIdentity und Dateitransaktionen

Einführen:

- ProjectIdentity,
- Symlink-Auflösung,
- Dateisystem-Case-Semantik,
- ProjectPathRegistry,
- zentraler Arbitration-Vertrag,
- Reservation Tokens,
- ProjectOpenCoordinator,
- Open-Single-Flight,
- Save-As-Reservation und Rollback,
- `ProjectSession.FromTemplate` für clean + pathless Template-Sessions.

## Phase 5 – Workspace-Ownership intern

Einführen:

- WorkspaceWindowManager,
- WorkspaceWindowHandle,
- StartOnly-Invariante,
- CreateSetup als eigener Workspace-Kontext,
- EditSetup weiterhin in bestehender Session.

Die öffentliche Multiwindow-Funktion bleibt noch deaktiviert.

## Phase 6 – Multi-Recovery und Recovery Center

Einführen:

- WorkspaceRecoveryContext,
- RecoveryId,
- RecoveryCatalog,
- mehrere Slots,
- Legacy-Move,
- Create→Project,
- Recovery-Gruppierung nach Originalprojekt,
- Priorität nach WrittenAtUtc + RecoveryId,
- `RecoveryPending` als Registry-Zustand,
- dynamische Übergabe an den nächstälteren Kandidaten,
- detached Restore,
- Discarded-Tombstones,
- Recovery Center,
- dauerhafter Recovery-Einstieg auf Startseite/Menü.

Recovery ist ab jetzt Teil der laufenden Anwendung und kein blockierender Vor-Startup mehr. Ab diesem Zeitpunkt entfällt die Legacy-`Starting`/`Retry`-Sonderregel aus Phase 2; die Primary kann trotz offener Recovery-Kandidaten stabil `Ready` sein und Requests mit `Accepted` übernehmen.

## Phase 7 – Einheitlicher Workspace Close

Einführen:

- WorkspaceCloseTransaction,
- Save/Discard/Cancel,
- CreateSetup-Close,
- EditSetup-Close,
- Recovery-PrepareDiscard,
- Busy/native-Dialog-Regeln.

Normaler Window Close wird auf diesen Pfad umgestellt.

## Phase 8 – Application Quit

Einführen:

- sequenzieller ApplicationCloseCoordinator,
- Recovery-Checkpoint vor der ersten Rückfrage,
- aktiver Workspace zuerst, danach stabile Fensterreihenfolge,
- User Quit über alle Plattformquellen,
- synchrones Cancel von `ShutdownRequested` + genau ein asynchroner Coordinator,
- Schutz gegen parallele Quit-Coordinatoren,
- separater OS-Shutdown-Recovery-Pfad,
- AppDialog-Lifecycle.

## Phase 9 – Multiwindow öffentlich aktivieren

Freischalten:

- New in neuem Fenster,
- Open in neuem Fenster,
- paralleles Bearbeiten,
- paralleles Vergleichen,
- Aktivierung bereits geöffneter Projekte.

Zu diesem Zeitpunkt existieren bereits:

- Single Instance,
- Plattform-Aktivierung,
- ProjectIdentity,
- Registry Arbitration inklusive RecoveryPending,
- Multi-Recovery,
- sicherer Close-Pfad,
- sequenzieller Quit.

## Phase 10 – DEV-64 und Plattformfeinschliff

Auf der bereits vorhandenen `OpenUri`-Infrastruktur:

- fachliche `spandraft://`-Routen,
- Website-/Hilfebeispiele,
- Template-Öffnung über `ProjectSession.FromTemplate`,
- MSIX-Registrierung über `uap:Protocol`,
- Protocol-Registrierung durch den klassischen Installer,
- bewusst keine automatische Registrierung für portable/dev Builds,
- macOS Document-/URI-Types,
- weitere Windows-/macOS-Aktivierungsdetails.

---

# 84. Nicht Bestandteil von DEV-85

Nicht eingeführt werden:

- eingebauter Projektvergleich,
- Diff-Ansicht,
- Tabs,
- parallele Solverarchitektur,
- Cross-Process-State-Synchronisation,
- Mehrbenutzer-Locking auf Netzlaufwerken,
- Schutz vor externem Überschreiben einer geöffneten Projektdatei,
- Hardlink-Erkennung,
- eigene Datenbank für Settings/Libraries,
- Event-Bus-Framework,
- DI-Container.

---

# 85. Zielzustand

Beispiel:

```text
Workspace A
└── A.spandraft
    dirty

Workspace B
└── B.spandraft
    clean

Workspace C
└── CreateSetup
```

A und B können unabhängig:

- bearbeitet,
- analysiert,
- gespeichert,
- rückgängig gemacht,
- geschlossen

werden.

Eine Änderung der Materialbibliothek wird in anderen Auswahloberflächen sichtbar, verändert aber keine bestehenden Projektwerte.

Ein erneuter OpenRequest für A aktiviert A.

Ein Save As von B auf A wird verhindert.

Ist für A noch eine unentschiedene Recovery vorhanden, befindet sich A stattdessen in `RecoveryPending`: Open führt zur passenden Recovery-Entscheidung und Save As eines anderen Projekts auf A wird abgelehnt.

Ein Beispielprojekt öffnet über `ProjectSession.FromTemplate` clean und ohne eigenen FilePath. Erst nach einer echten Änderung entsteht Dirty-/Recovery-Zustand; erstes Save führt zu Save As.

Mehrere Crash-Recoveries werden gemeinsam innerhalb der laufenden Anwendung angeboten. Der Recovery Center bleibt erreichbar, solange Kandidaten offen sind.

Der bevorzugte gültige Recovery-Stand eines Originalprojekts wird nach `WrittenAtUtc` und RecoveryId deterministisch bestimmt; ältere Varianten können detached geöffnet werden.

Ein Recovery-Dialog blockiert weder die gesamte Primary noch weitere externe Aktivierungen.

Ein Secondary-Prozess liest lediglich Instance-Metadaten und berührt keine fachlichen gemeinsam genutzten SpanDraft-Daten.

Ein Quit mit:

```text
A → Discard
B → Cancel
```

lässt A bewusst geschlossen und B geöffnet. Vor der Dialogserie wurde der aktuelle dirty Recovery-Zustand best-effort gesichert.

---

# 86. Architekturentscheidung

SpanDraft entwickelt sich von:

```text
MainWindow
=
Fenster
+ Projekt
+ globale Daten
+ Startup
+ Recovery
+ App-Lifecycle
```

zu:

```text
DesktopApplicationHost
=
App-Lifecycle
+ globale Services
+ globale Orchestrierung

ActivationDispatcher
=
gemeinsamer Eingang für Windows/macOS/URI/File Activation

WorkspaceWindow
=
lokaler UI-Workspace
+ optional eine ProjectSession
+ workspacebezogene Recovery

ProjectSession
=
ein Projektzustand

ProjectPathRegistry
=
Datei-Ownership und Arbitration

RecoveryCatalog
=
Recovery-Discovery und Slot-Lifecycle

WorkspaceCloseTransaction
=
einheitliche Close-Semantik
```

Modell C wird damit nicht als „mehrere MainWindows“ umgesetzt, sondern als Single-Instance-/Multi-Workspace-Architektur mit klaren Ownership-, Activation-, Persistenz- und Recovery-Grenzen.