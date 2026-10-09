# SpanDraft – lokale Bibliotheken

## Zuständigkeit und Quellen

Material- und Querschnittsbibliotheken sind Anwendungsdaten von
`SpanDraft.Desktop`. Library-Modelle und Verhalten liegen in `Libraries`,
JSON-Verträge, Ressourcenladen und Datei-I/O in `Persistence`. Core kennt nur
die fachlichen Objekte `Material` und `ISectionDefinition`, keine Bibliotheken,
Kategorien oder Persistenz.

Der eingebaute Materialkatalog ist eine unveränderliche, eingebettete,
versionierte JSON-Ressource. Benutzer-Materialien und Benutzer-Querschnitte
werden in getrennten lokalen Dateien verwaltet. Gleiche Materialnamen in
Built-in und User sind zulässig; die Quellen werden nicht zusammengeführt.
Ein importierter Normprofil- oder FreeCAD-Katalog gehört nicht zu diesem Vertrag.

## Lokale Schlüssel und Projektsnapshots

Bibliothekseinträge sind Vorlagen ohne persistente technische ID. Der
Materialname beziehungsweise Preset-Name ist ausschließlich innerhalb seiner
Bibliothek ein Schlüssel: nichtleer, bereits getrimmt und mit
`OrdinalIgnoreCase` eindeutig. Ein Name identifiziert keine projektübergreifenden
Materialwerte und begründet keine historische Identität nach einer Umbenennung.

Benutzerbibliotheken bieten `All`, `Add`, `Replace(oldName, entry)`, `Remove`
und `Find`. Einträge behalten ihre Reihenfolge; Replace ersetzt an derselben
Position und darf den Namen ändern. Namenskollisionen werden abgelehnt, ohne
den bisherigen Inhalt zu verändern. Fehlende Replace-Ziele führen zu
`KeyNotFoundException`; Find liefert null und Remove false für fehlende Namen.
Sammlungen werden nur lesbar angeboten; optionale Metadaten werden defensiv
kopiert. Der Built-in-Katalog bietet ausschließlich lesende Operationen.

Bei einer Auswahl übernimmt das Projekt ausschließlich ein vollständiges
Core-Objekt. Die vorhandenen Material- und Querschnittsdefinitionen sind
unveränderlich; ihre Übernahme benötigt keine Verbindung zum Library-Eintrag.
Ersetzen, Umbenennen oder Löschen eines Eintrags verändert keine bereits
übernommenen Projektwerte. Der verbindliche eigenständige Projektdateivertrag
steht in [PROJECT_FORMAT.md](PROJECT_FORMAT.md); Bibliotheksmetadaten werden
nicht in das Projekt übernommen.

## Materialeinträge

`MaterialLibraryEntry` enthält das bestehende Core-`Material` als einzigen
Träger von E, fy, optionaler Dichte und optionaler Poissonzahl. `Material.Name`
ist zugleich die primäre technische Bezeichnung; es gibt keine zweite
Designation-Property.

Zusätzliche Library-Metadaten:

- `MaterialCategory`: stabile interne Kategorien `StructuralSteel`,
  `StainlessSteel`, `AlloySteel`, `Aluminium` und `Other`.
- `MaterialNumber`: optionaler String, beispielsweise `1.0038` oder `1.4301`;
  keine Zahl und kein Schlüssel.
- `OtherStandards`: optionale Liste alternativer Bezeichnungen aus anderen
  Normsystemen, beispielsweise `AISI 304`; keine besondere AISI-Property.

Fehlende Metadaten erhalten keine Platzhalter. MaterialNumber und alternative
Bezeichnungen werden getrimmt, leere Werte entfallen. Eine fehlende Liste wird
im Modell als leere unveränderliche Sammlung angeboten. Unbekannte Dichte und
Poissonzahl bleiben unabhängig voneinander null; es werden keine Werte ergänzt.
Kategorien und Metadaten beeinflussen keine mechanischen Vergleiche oder
Berechnungen.

## Querschnittspresets

`SectionLibraryEntry` enthält nur Name und `ISectionDefinition`. Die ausgewählte
Biegeachse gehört weiterhin zum Balken beziehungsweise Projekt und ist kein
Preset-Bestandteil. Verfügbare Achsen einer Definition sind davon unabhängig.

Die parametrischen Formen sind Rechteck, Rechteckrohr, Kreis, Rundrohr, I/H,
U, T und Winkel. Persistiert werden ausschließlich Form und ursprüngliche
Eingabeparameter einschließlich unabhängiger Radien. Radius 0 ist eine explizite
Eingabe, kein fehlender Wert. Berechnete Fläche, Schwerpunkt, Momente,
Hauptachsenwinkel, Widerstandsmomente und abgeleitete Radien werden nicht
gespeichert. Beim Laden rekonstruiert der vorhandene Geometriekern diese Werte.

Manual-Definitionen speichern A sowie eine oder zwei Achsen mit Bezeichnung,
I und einem Tabellenwert W. Für eine Achse gilt W+ = W− = W. Einzelne Y/Z/U/V
sowie die Paare Y/Z und U/V folgen unverändert [DOMAIN.md](DOMAIN.md),
einschließlich kanonischer Reihenfolge und Ablehnung ungültiger Paare.
Legacy-Core-Sections werden beim Schreiben aus ihren ursprünglichen Eingaben
auf dieselben parametrischen beziehungsweise Manual-Formen normalisiert.

## Versionierte Dateien und Validierung

Die voneinander getrennten Envelope-Verträge verwenden:

| Quelle | `format` | `formatVersion` |
| --- | --- | --- |
| Benutzer-Materialien | `SpanDraft.MaterialLibrary` | 1 |
| Benutzer-Querschnitte | `SpanDraft.SectionLibrary` | 1 |
| Eingebauter Materialkatalog | `SpanDraft.MaterialCatalog` | 1 |

Jeder Envelope besitzt ein verpflichtendes `entries`-Array; eine leere Liste
ist gültig. Die Library-Wire-DTOs sind unabhängig von Projekt-DTOs und deren
Versionen. Benutzer-Materialien und eingebauter Katalog teilen nur ihr
Library-internes Materialmapping. DTOs und Codec-Tests definieren die Felder.
Core-Objekte werden nicht direkt serialisiert.

Dateien enthalten eingerücktes UTF-8-JSON mit camelCase-Feldern, stabilen
Stringwerten für Kategorien, Formen und Achsen, ohne CLR-Typnamen oder
numerische Enumwerte. Physikalische Werte stehen in kanonischen SI-Einheiten:
Längen in m, A in m², I in m⁴, W in m³, E/fy in Pa, Dichte in kg/m³ und
Poissonzahl dimensionslos. Kultur und Anzeigeeinheiten spielen keine Rolle.
Unbekannte optionale Materialwerte und leere optionale Metadaten werden beim
Schreiben weggelassen; fehlende oder null gesetzte optionale Werte bleiben
beim Laden unbekannt.

Pflichtfelder müssen vorhanden und dürfen nicht null sein. Beschädigtes JSON,
doppelte JSON-Eigenschaften, nicht endliche Zahlen, unbekannte Formate,
Versionen, Kategorien, Formen oder Achsen sowie Namensduplikate und ungültige
Domainwerte werden als `LibraryFormatException` abgelehnt. Es gibt keine
Teilübernahme gültiger Einträge aus einer ungültigen Datei. Zusätzliche
unbekannte Felder werden ignoriert. Domain-Konstruktoren bleiben die letzte
fachliche Validierungsinstanz; ungültige Werte werden nicht korrigiert.

## Lokale Speicherung und Fehlerverhalten

Benutzerdateien liegen im bestehenden SpanDraft-AppData-Verzeichnis:

- `<LocalApplicationData>/SpanDraft/material-library.json`
- `<LocalApplicationData>/SpanDraft/section-library.json`

Fachliche Stores verwenden den bestehenden `IProjectFileStore` und dessen
`WriteAtomicAsync`; es gibt keinen zweiten File-I/O-Mechanismus. Verzeichnisse
werden erst beim Speichern angelegt. Fehlende Dateien ergeben neue leere
Benutzerbibliotheken und werden beim Laden nicht erzeugt.

Eine gültige Datei wird vollständig geladen. Formatfehler sowie I/O- und
Zugriffsfehler werden an den Aufrufer weitergegeben; insbesondere gibt es
keinen Settings-artigen Fallback auf eine leere Bibliothek. Laden verändert
keine Datei. Vor jedem Speichern wird eine vorhandene Zieldatei erneut
validiert: eine beschädigte oder unbekannt versionierte Datei bleibt erhalten,
auch nach einem fehlgeschlagenen Ladeversuch oder bei einem neuen Store.
Die Stores bieten keinen automatischen Reset oder Recovery-Ersatz.
Atomische Veröffentlichung, Abbruch und Schreibfehler folgen dem bestehenden
File-Store-Vertrag.

## Curation eingebauter Materialien

Eingebaute Materialwerte werden von SpanDraft selbst kuratiert. Benötigt werden
nur einzelne technische Kennwerte, die fachlich anhand öffentlich verfügbarer
Informationen geprüft werden. Fremde Materialtabellen oder Datenbanken werden
nicht systematisch kopiert oder importiert; fremde Beschreibungstexte werden
nicht übernommen.

Es gibt keine per-Material-Source-, URL- oder Provenancefelder. Der allgemeine
öffentliche Hinweis steht in der README. Katalog-Infrastruktur und fachliche
Befüllung sind getrennte Arbeiten: Produktionsmaterialien werden nur mit
geprüften Kennwerten aufgenommen; synthetische Daten gehören ausschließlich
in Tests, nicht in die eingebettete Produktionsressource.
