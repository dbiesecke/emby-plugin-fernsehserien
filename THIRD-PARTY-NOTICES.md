# Third-party notices

The plugin embeds the DOM/parser subset of **HTML Agility Pack 1.12.4**, from
https://github.com/zzzprojects/html-agility-pack/tree/v1.12.4 (MIT).

Source is included in `src/HtmlParser`; see its `LICENSE.txt`. The namespace was
changed to `Emby.Plugin.Fernsehserien.HtmlParser` and upstream-only warnings
CS0618/CS3021 are suppressed in those files. Network, console, mixed-code and
object-encapsulation helpers are excluded. No parser package download or DLL
is needed at runtime.

The Emby SDK is a compile-time reference, not distributed with this plugin.
Metadata and images remain content of fernsehserien.de and their respective
credited authors/rightsholders. This is an independent, unofficial plugin.
