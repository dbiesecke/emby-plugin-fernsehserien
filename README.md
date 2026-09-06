# fernsehserien.de für Emby

Eigenständiger Metadaten- und Bildprovider für **Serien, Filme, Staffeln und Episoden**. Deutsche Texte, Originaltitel, Produktionsdaten, Genres, Besetzung und verfügbare Bilder direkt von fernsehserien.de.

## Installation

1. Die `Emby.Plugin.Fernsehserien.dll` aus einem [GitHub-Release](https://github.com/dbiesecke/emby-plugin-fernsehserien/releases) oder dem Artefakt `fernsehserien-plugin` unter [Actions](https://github.com/dbiesecke/emby-plugin-fernsehserien/actions) herunterladen.
2. Emby stoppen, DLL in den `plugins`-Ordner des Emby-Datenverzeichnisses kopieren und Emby starten. Im offiziellen Docker-Image ist das `/config/plugins`.
3. In den Bibliothekseinstellungen `fernsehserien.de` für die gewünschten Metadaten- und Bildtypen aktivieren und die Providerreihenfolge festlegen.

Die DLL enthält den HTML-Parser. Keine API-Schlüssel, kein Worker und keine weiteren DLLs erforderlich. Test-Assemblies niemals installieren.

## Zuordnung

Unter **Identifizieren** nach Titel suchen oder im Feld **fernsehserien.de (Seitenpfad)** die konkrete ID eintragen:

| Typ | Beispiel |
|---|---|
| Serie | `dark` |
| Film | `filme/inception` |
| Staffel | `dark/episodenguide/staffel-1/33342` |
| Episode | `dark/folgen/1x01-geheimnisse-1144654` |

Serien vor ihren Staffeln und Episoden identifizieren. Automatische Titelzuordnung verlangt einen eindeutigen Titel/Originaltitel und – falls angegeben – dasselbe Jahr. Unnummerierte Specials benötigen eine explizite Quell-ID. Es wird keine Nummer aus der Listenposition erfunden.

## Kompatibilität und Build

Ziele: **Emby 4.9** (Prüfinstanz 4.9.1.90) und **4.10.0.20 Beta**. Build gegen SDK 4.9.1.80, `netstandard2.0`, eine plattformübergreifende DLL. Der genaue Prüfstatus steht in [docs/usage.md](docs/usage.md).

```sh
dotnet restore tests/Checks.csproj --locked-mode
dotnet build tests/Checks.csproj -c Release --no-restore
dotnet run --project tests/Checks.csproj -c Release --no-build
python3 scripts/package.py
```

`global.json` fixiert .NET SDK 10.0.302, Lockdateien fixieren NuGet-Abhängigkeiten. PRs und Main-Pushes bauen und prüfen. Main-Pushes und Tags starten zusätzlich Wegwerf-Emby-Container. Tags `v0.1.0` bzw. `v0.1.0-beta.1` erzeugen nach erfolgreichen Prüfungen Releases mit DLL, ZIP und SHA-256-Prüfsummen.

[Ausführliche Nutzung, Diagnose und Rollback](docs/usage.md) · [Drittanbieterhinweise](THIRD-PARTY-NOTICES.md)
