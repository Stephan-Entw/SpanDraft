# Balken, Werkstoff und Querschnitt

Die Projektdefinition beschreibt einen **geraden Balken** mit einer Länge, einem Werkstoff, einem über die ganze Balkenlänge **konstanten Querschnitt** und genau einer ausgewählten Biegeachse. Die Wahl dieser Größen bestimmt die Steifigkeit und die einfache elastische Biegebewertung.

## Balkenlänge

Die Balkenlänge `L` reicht vom linken Ende `x = 0` bis zum rechten Ende `x = L`. Lager und Lasten dürfen auch **an den Enden** liegen. Lager zwischen den Enden und überstehende Balkenteile (Überhänge) sind möglich.

Wenn Sie die Länge später verkleinern, dürfen bereits vorhandene Lager und Lasten dadurch nicht außerhalb des Balkens liegen. SpanDraft lehnt eine solche Änderung ab, bis die betreffenden Objekte angepasst sind.

## Werkstoff auswählen

Werkstoffe können aus dem eingebauten Katalog, einer eigenen Werkstoffbibliothek oder als projektbezogene Definition übernommen werden. Die wichtigen Kennwerte sind:

| Kennwert | Bedeutung | Wirkung in SpanDraft |
| --- | --- | --- |
| `E` – Elastizitätsmodul | Beschreibt die Steifigkeit des Werkstoffs im elastischen Bereich | Bestimmt zusammen mit `I` die Biegesteifigkeit `E · I` und damit die elastische Durchbiegung und Rotation |
| `fy` – Streckgrenze | Hinterlegte Grenze für das Einsetzen plastischer Verformung, vereinfacht als Kennwert | Geht in den angezeigten Sicherheitsfaktor ein |
| `ρ` – Dichte (optional) | Masse pro Volumen | Wird gespeichert, erzeugt **kein automatisches Eigengewicht** |
| `ν` – Poissonzahl (optional) | Querkontraktion bei Längsdehnung | Wird gespeichert, **nicht** für die aktuelle Biegerechnung verwendet |

Wichtig: Ein höherer `E`-Wert macht einen Balken bei ansonsten gleichen Bedingungen steifer, ohne damit automatisch die Streckgrenze zu erhöhen. Die Streckgrenze verändert die Verformung im linear-elastischen Modell nicht. Umgekehrt beeinflusst `fy` den Sicherheitsfaktor, nicht die errechnete Durchbiegung.

Materialkategorie, Werkstoffnummer und alternative Normbezeichnungen dienen der Identifikation und Suche. Sie werden nicht als zusätzliche Berechnungsparameter verwendet. Die Katalogwerte sind von SpanDraft zusammengestellte technische Referenzwerte und ersetzen keine passende Werkstoffnorm, Herstellerangabe oder Materialbescheinigung.

### Eigene Werkstoffe und Bibliotheken

- Sie können einen Werkstoff neu definieren oder einen bestehenden als Ausgangspunkt anpassen.
- Für die einmalige Verwendung genügt das Übernehmen in das Projekt. Für eine spätere Wiederverwendung speichern Sie ihn zusätzlich ausdrücklich in **Meine Werkstoffe**.
- Eigene Bibliothekseinträge lassen sich bearbeiten und löschen; der eingebaute Katalog bleibt unverändert.
- Das Projekt speichert **eine vollständige Kopie der übernommenen Werkstoffwerte**. Spätere Bibliotheksänderungen ändern bestehende Projekte nicht.

Ein Projekt ist beim erneuten Öffnen daher nicht von einer bestimmten Werkstoffbibliothek abhängig.

## Querschnitt definieren

SpanDraft unterstützt folgende parametrisierte Grundformen:

| Form | Typische Eingaben |
| --- | --- |
| Rechteck | Breite `b`, Höhe `h` |
| Rechteckrohr | Außenbreite, Außenhöhe, Wanddicke, optional äußerer Eckenradius |
| Rundmaterial | Durchmesser |
| Rundrohr | Außendurchmesser, Wanddicke |
| I-/H-Profil | Höhe, Breite, Stegdicke, Flanschdicke, innerer Übergangsradius |
| U-Profil | Höhe, Breite, Stegdicke, Flanschdicke, innerer Übergangsradius |
| T-Profil | Höhe, Breite, Stegdicke, Flanschdicke, innerer Übergangsradius |
| Winkelprofil | Breite, Höhe, Schenkeldicke, innerer Übergangsradius |

Die Form ist idealisiert. Beispielsweise sind die Außenkanten von I-, U-, T- und Winkelprofilen scharf; ein eingegebener innerer Radius verändert die berechneten Kennwerte. Bei Rechteckrohren bezieht sich ein Radius auf die **Außenecke**; der innere Radius ergibt sich aus Außenradius und Wanddicke. `r = 0` bedeutet in allen betreffenden Eingaben eine scharfe Ecke.

Die aus der Geometrie ermittelten Kennwerte hängen von den eingegebenen Maßen ab. Ein selbst definierter I-Querschnitt ist **nicht automatisch** ein genormtes oder herstellbares Handelsprofil. Importierte Normprofilkataloge sind nicht Bestandteil dieser Version.

### Manuelle Querschnittskennwerte

Wenn Sie verlässliche Tabellen- oder Herstellerwerte besitzen, können Sie statt einer parametrischen Form **Manuell A/I/W** wählen:

- `A`: Querschnittsfläche in mm², cm² oder einer anderen gewählten Flächeneinheit;
- `I`: Flächenträgheitsmoment um die angegebene Achse, beispielsweise mm⁴;
- `W`: elastisches Widerstandsmoment um dieselbe Achse, beispielsweise mm³.

Sie können eine einzelne Achse oder ein zulässiges Achsenpaar (`y-y/z-z` beziehungsweise `u-u/v-v`) eingeben. Für manuelle Werte gilt in SpanDraft **`W+ = W− = W`**. Hat ein unsymmetrisches Profil tatsächlich verschiedene Widerstandsmomente auf beiden Seiten, kann ein einzelner manueller `W`-Wert das nicht vollständig abbilden. Prüfen Sie, ob dieser vereinfachte Ansatz für Ihre Fragestellung geeignet ist.

Manuelle Kennwerte enthalten **keine geometrische Kontur**. SpanDraft ergänzt weder fehlende Maße noch einen erfundenen Schwerpunkt.

## Warum die Biegeachse wichtig ist

Ein Querschnitt kann bei gleicher Last je nach Biegeachse eine unterschiedliche Steifigkeit und Spannung haben. Für die aktuelle Berechnung wählen Sie **eine** Biegeachse:

- Rechteck, Rechteckrohr, Rundmaterial, Rundrohr, I/H, U und T: **`y-y` oder `z-z`**.
- Winkelprofile: **`u-u` oder `v-v`**, die Hauptachsen des unsymmetrischen Profils.
- Manuelle Kennwerte: die beim Definieren angebotene(n) Achse(n).

In der Querschnittsskizze ist `y` horizontal und `z` vertikal. Für die Achsen `y-y` und `z-z` gelten die geometrischen Momente `Iy` und `Iz`. Bei einem hochkant stehenden Rechteck ist `Iy` in der Regel größer als `Iz`; bei einem quer liegenden Rechteck kann es umgekehrt sein. Die Namen sind **nicht** automatisch Synonyme für „stark“ und „schwach“.

![Lokale Querschnittskoordinaten und Biegeachsen](../assets/querschnitt-achsen.png)

Die Auswahl einer Achse legt fest, welches `I` und welche Widerstandsmomente `W+` und `W−` die zweidimensionale Rechnung verwendet. Sie dreht den Balken nicht räumlich und berechnet **keine gleichzeitige Biegung um zwei Achsen**. Bei Winkelprofilen sind `u-u` und `v-v` die Hauptbiegeachsen; eine separate räumliche Lastausrichtung erfolgt nicht.

## Was bedeuten A, I und W?

| Größe | Vereinfacht gesagt | Einheit |
| --- | --- | --- |
| `A` | Wie viel Materialfläche der Querschnitt besitzt | Fläche, z. B. mm² |
| `I` | Wie die Materialfläche bezogen auf die Biegeachse verteilt ist; beeinflusst `E · I` und damit die Biegesteifigkeit | Länge⁴, z. B. mm⁴ |
| `W+`, `W−` | Verhältnis von `I` zum Abstand der jeweiligen äußersten Randfaser; wichtig für die elastische Biegespannung | Länge³, z. B. mm³ |

Für einen symmetrischen Rechteckquerschnitt mit Breite `b` und Höhe `h` gilt bei Biegung um `y-y`: `Iy = b · h³ / 12` und `Wy = b · h² / 6`. Andere Profilformen benötigen andere Beziehungen; SpanDraft berechnet ihre Kennwerte aus der jeweiligen Geometrie.

Die Zeichen `+` und `−` bei `W` beziehen sich auf **geometrische Profilseiten**. Sie sagen **nicht**, welche Seite unter Zug oder Druck steht.

## Querschnittsbibliothek und Namen

Eigene parametrische und manuelle Querschnittsdefinitionen lassen sich zur Wiederverwendung in **Meine Querschnitte** speichern. Das ist eine separate Aktion; die bloße Übernahme eines Querschnitts in einen Balken legt keinen Bibliothekseintrag an.

Innerhalb der Querschnittsauswahl hat jede Grundform ihren eigenen noch nicht übernommenen Eingabestand, einschließlich eines optionalen Namens. Ein Formwechsel überträgt keinen selbst vergebenen Namen auf eine andere Grundform. Nur die tatsächlich bestätigte Definition wird Teil des Projekts. Auch hier speichert das Projekt eine unabhängige Kopie: Änderungen oder das Löschen eines Bibliothekspresets wirken nicht rückwirkend.

Weiter: [Lager und Randbedingungen](lager.md) oder [Spannung und Sicherheitsfaktor](../ergebnisse/spannung-sicherheit.md).
