# SpanDraft-Projektformat V1

## Projektdateien

Dateiendung: `.spandraft` (beim Speichern ohne Beachtung der Groß-/Kleinschreibung).
Eine Projektdatei ist lesbares, eingerücktes UTF-8-JSON. Der Root enthält:

```json
{
  "format": "SpanDraft.Project",
  "formatVersion": 1,
  "document": { },
  "presentation": { }
}
```

Die leeren Objekte im Beispiel zeigen ausschließlich die Root-Struktur, keine
vollständige gültige Datei. Das Format enthält keine CLR-Typnamen.

## Persistierte Daten und Einheiten

`document` enthält `length`, `material`, `section`, die geordneten Arrays
`supports`, `pointLoads`, `distributedLoads` und `namingState`.

- `material`: `name`, `youngsModulus`, `yieldStrength`.
- Alle Entities: `id` als GUID und `name`. Supports zusätzlich `type` und
  `position`; Punktlasten zusätzlich `type`, `position` und `value`.
- Konstante Streckenlasten: `startPosition`, `endPosition`, `intensity`.
- `namingState`: `nextSupportOrdinal`, `nextForceNumber`, `nextMomentNumber`,
  `nextDistributedLoadNumber`; positive ganzzahlige Zähler, unabhängig von Namen.
- `presentation.annotationOffsets`: Array aus `entityId`, `dx`, `dy`.
  Fehlender Entity-Eintrag bedeutet automatische Annotation. Offsets sind relative
  DIPs zum jeweiligen automatischen Anker, keine absoluten Canvas-Koordinaten.

Physikalische Werte stehen ausschließlich in SI: Längen/Positionen in m,
Kräfte in N, Momente in N·m, Intensität in N/m, Werkstoffwerte in Pa,
Querschnittskennwerte A/I/W in m²/m⁴/m³. Keine lokalisierten Zahlstrings oder
Umrechnung in Anzeigeeinheiten. Null ist für Positionen und Lastwerte gültig.
IDs, Namen, Entity-Reihenfolge, Zähler und Offsets bleiben erhalten.

## Stabile Typbezeichnungen

| Kategorie | `type` | Konstruktive Daten |
| --- | --- | --- |
| Querschnitt | `rectangle` | `width`, `height` |
| Querschnitt | `rectangularHollow` | `width`, `height`, `wallThickness` |
| Querschnitt | `circle` | `diameter` |
| Querschnitt | `circularHollow` | `outerDiameter`, `wallThickness` |
| Querschnitt | `custom` | `area`, `secondMomentOfArea`, `sectionModulus` |
| Lager | `fixed`, `pinned`, `roller` | `position` |
| Punktlast | `force` | `position`, `value` in N |
| Punktlast | `moment` | `position`, `value` in N·m |

Die Geometrie der Standardquerschnitte wird vollständig gespeichert; ihre
Kennwerte werden aus dieser Geometrie rekonstruiert. Benutzerquerschnitte speichern
die gelieferten A/I/W-Werte. Für konstante Streckenlasten ist der Arrayname die
Typfestlegung; sie besitzen keinen zusätzlichen Discriminator.

## Validierung und Kompatibilität

Alle oben genannten gemeinsamen Felder und die zum Querschnittstyp gehörenden
Daten sind Pflichtfelder, auch bei leeren Arrays oder einem gültigen Nullwert.
Fehlende oder null gesetzte Pflichtfelder, nicht endliche Zahlen, leere/ungültige
oder projektweit doppelte GUIDs, leere/ungetrimmte oder ohne Beachtung der
Groß-/Kleinschreibung doppelte Entity-Namen, ungültige Zähler und Domainwerte
werden abgelehnt. Annotationen dürfen nur vorhandene Entities referenzieren und
jede Entity nur einmal aufführen. Lagerpositionen müssen projektweit verschieden
sein; Positionen/Bereiche müssen innerhalb des Balkens liegen.

Projekte ohne Lager oder mit unzureichender Lagerung dürfen gespeichert und
geöffnet werden. Der Analysis-Status beschreibt anschließend ihre Berechenbarkeit.

Unbekannte zusätzliche Felder werden ignoriert. Unbekannte Versionen oder
Typbezeichnungen und doppelte JSON-Eigenschaften werden abgelehnt. Beschädigte
Dateien werden nicht still repariert. Änderungen an Code- oder Klassennamen
ändern den Dateivertrag nicht. Eine zukünftige inkompatible Formatänderung
benötigt eine neue `formatVersion`; V1 nimmt keine automatische Migration vorweg.

Nicht persistiert werden Analysis-/Solver-/Engineering-Ergebnisse, History,
RevisionIds/Savepoint, Dateipfad, Hover/Selection/Werkzeuge, Draft-/Textbuffer,
Placement-/Drag-Vorschauen, Fehler-/Konfliktzustände sowie Canvas-/Viewport- und
Stationslayout. Öffnen beginnt mit leerer History und berechnet Analysis neu.

## Interne Recovery

Recovery ist ein einzelner privater lokaler App-Daten-Slot, kein Autosave der
Benutzerdatei. Ihr internes Envelope enthält `recoveryVersion`, optional
`originalFilePath`, `writtenAtUtc` in UTC und `project` mit exakt demselben
Projektformat V1. Das Envelope ist kein öffentliches Austauschformat.

Nur committed Zustände einer dirty Session werden geschrieben. Beim Wiederherstellen
bleibt das Projekt dirty; die ursprüngliche Benutzerdatei wird nicht automatisch
geändert. Eine vorhandene Recovery wird ohne Nachfrage entfernt, wenn ihr
vollständiger Projektinhalt mit der ursprünglichen Projektdatei übereinstimmt.
