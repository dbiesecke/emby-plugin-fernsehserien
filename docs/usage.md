# Nutzung: Emby fernsehserien.de 0.1.0

## Bibliothek konfigurieren

Nach Installation der DLL und Neustart im Dashboard unter Bibliotheken die gewünschte Film- oder Serienbibliothek bearbeiten. `fernsehserien.de` bei den Metadaten- und Bildanbietern aktivieren. Die Reihenfolge entscheidet, welcher Provider Vorrang hat. Andere Provider können fehlende Informationen ergänzen. Sprache `Deutsch` wählen; dieser Provider liefert ausschließlich deutsche Quelltexte.

Zuerst an einer kleinen Testbibliothek identifizieren und aktualisieren. Bei Serien zuerst die Serie, danach Staffeln/Episoden aktualisieren. Episoden verwenden die expliziten Staffel-/Folgenangaben des fernsehserien.de-Episodenguides für die Originalfassung. Keine automatische Umrechnung von DVD-, absoluter oder deutscher Sonderreihenfolge. Bibliotheken mit abweichender Nummerierung benötigen die jeweilige Episoden-Quell-ID.

## IDs und Metadaten

Der Provider-Schlüssel heißt `Fernsehserien`. Der Wert ist der Seitenpfad ohne führenden Slash (zum Beispiel `dark` oder `filme/inception`). Bestehende IDs werden bei Aktualisierung erneut abgerufen. Unterschiedliche gleichnamige Treffer werden nicht automatisch ausgewählt. Bei Remakes immer das Jahr prüfen.

Originalpremieren werden von deutschen Premieren getrennt. Laufzeiten stammen aus dem eigentlichen Film-/Episodenbereich, nicht aus Trailern. Besetzung, Rollen, Regie, Produktionsfirmen und externe IDs erscheinen nur, wenn sie im passenden Quellbereich ausdrücklich vorhanden sind. Abdeckung hängt vom Titel ab.

Fehlende Felder liefert das Plugin leer an Emby's regulären Metadatenprozess. Es verändert keine Bibliotheksdateien und schreibt nicht direkt in Emby's Datenbank. Emby wendet Providerprioritäten, Aktualisierungsmodus und Feldsperren an. Vor einer breiten Aktualisierung das Verhalten gesperrter Felder in der eigenen Bibliothek prüfen.

Unnummerierte Specials werden nicht als S00E00 oder nach Listenposition einsortiert. Eine konkrete Episoden-ID ermöglicht ihre manuelle Identifizierung und bewahrt die vorhandene Nummerierung.

## Bilder

Emby kann Poster (`Primary`), Hintergründe (`Backdrop`), Banner und eindeutig bezeichnete Logos abrufen. Episodenbilder sind `Primary`. Staffelposter werden nur aus der Staffel selbst übernommen; Episodenbilder und Serienbanner werden nicht zu Staffelpostern umgedeutet. Manche Titel haben ausschließlich ein Banner oder keinen passenden Bildtyp.

Bildvarianten aus `srcset` werden nach angebotener Größe gewählt. Bei Quellbildern ohne HTML-Maße werden PNG-/JPEG-Abmessungen vor der Einordnung gelesen; ein `sendung`-Bild ist nicht automatisch ein Poster. URLs werden nicht durch erfundene Auflösungs-Suffixe verändert. Erlaubt sind ausschließlich HTTPS-Quellhosts und `bilder.fernsehserien.de`; beim Download werden Bildsignatur und Inhaltstyp geprüft. Bestehende Bilder ersetzt Emby nur gemäß dem ausgewählten Aktualisierungsmodus.

## Cache und Fehlerdiagnose

- Suche: 1 Stunde; Detailseiten und Episodenguides: 24 Stunden; HTTP-404: 5 Minuten.
- Maximal 64 HTML-Seiten im Speicher, jeweils höchstens 4 MiB. Neustart leert den Cache.
- Höchstens zwei parallele Requests, 20 Sekunden Timeout je Versuch und maximal zwei Wiederholungen bei temporären Fehlern.
- `Retry-After` wird beachtet. Bei Wartezeiten über 30 Sekunden bricht der Aufruf ab, statt zu früh erneut anzufragen.
- Challenge-/Fehlerseiten werden nicht als Metadaten gecacht. Abbruch eines wartenden Scans beeinträchtigt keine anderen Aufrufer derselben Anfrage.

Im Emby-Log nach `Fernsehserien` suchen. Der Fehlertyp zeigt Abruf-, URL- oder Parserprobleme; es werden keine Seiteninhalte oder Zugangsdaten geloggt. Bei fehlendem Treffer zunächst Seitenpfad, Typ und Jahr kontrollieren. Bei Website-Sperren später erneut versuchen. Das Plugin umgeht keine Challenges.

## Entwicklung und Release

Das ausführbare Prüfprogramm nutzt kleine nachgebildete HTML-Ausschnitte und einen HTTP-Testhandler statt externer Testframeworks. Es deckt Titel-/Jahreszuordnung, Trailer-Abgrenzung, Premiere, Staffel/Folge/Specials, Bilder, Redirects, Cache, Abbruch und Retry-Limits ab.

Für einen manuellen Vergleich können lokal abgerufene Dateien `dark.html`, `movie.html`, `season.html`, `episode.html` und `dark-guide.html` an das Prüfprogramm übergeben werden:

```sh
dotnet run --project tests/Checks.csproj -c Release -- /pfad/zu/html-dateien
```

Vollständige Website-Inhalte werden nicht mit dem Repository verteilt. Der Scraper übernimmt erprobte Konzepte aus `fernsehserien-mcp` und `cf-media-search`: Suchweiterleitungen, Plus-Kodierung, Titel-/Jahreserkennung und Episodenguide-Auswertung. Ein unbestätigter Slug-Fallback wurde bewusst nicht übernommen.

Für Laufzeitprüfungen baut die Pipeline eine **separate Test-DLL** und lädt diese mit dem Plugin ausschließlich in Wegwerfcontainern. Sie prüft Emby's Providerregistrierung, Identifizierung, Metadaten für alle vier Typen sowie Bildregistrierung und Download. Sie öffnet keine Hostports und benutzt keine Produktionsdaten. Diese Prüfung benötigt Zugriff auf fernsehserien.de und kann bei einer Quellsperre fehlschlagen.

Tags müssen zur Version in der Projektdatei passen. Für Updates Projekt-/Assemblyversion, Dokumentation und passende Beispiele gemeinsam aktualisieren. Release-ZIP enthält nur Plugin-DLL und Dokumentation/Lizenzhinweise. Ein fehlgeschlagener Laufzeittest blockiert die Veröffentlichung.

## Prüfstatus

- Release-DLL gegen Emby SDK 4.9.1.80: lokal gebaut.
- Kleine Parser-/HTTP-Prüfungen: lokal bestanden.
- Echte HTML-Seiten von Dark, Inception, Staffel 1 und Folge 1: lokal bestanden; 26 nummerierte Dark-Episoden korrekt zugeordnet.
- Emby 4.9.1.90 und 4.10.0.20: GitHub-Laufzeitprüfung eingerichtet; Ergebnis noch ausstehend.
- Vollständiger Bibliotheksrefresh einschließlich Erhalt gesperrter Felder: noch nicht durch Laufzeitprüfung bestätigt.

## Update und Rollback

Emby stoppen, vorhandene Plugin-DLL sichern, neue DLL einsetzen und neu starten. Bei Problemen wieder stoppen und die gesicherte DLL zurückkopieren. Ein DLL-Rollback stellt bereits aktualisierte Bibliotheksmetadaten nicht wieder her; dafür Emby-Datenbackup bzw. vorhandene NFO-Dateien verwenden.

Das Plugin hat keine eigene HTTP-API, keinen Worker und keine Streamingfunktion. Daher sind OpenAPI-Schema, MediaFlow-Konfiguration und `cloudflare_tools.md` für dieses Projekt nicht erforderlich.
