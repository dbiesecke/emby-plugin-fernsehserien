"""Package the single plugin DLL; never ship SDK or test assemblies."""
import hashlib
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / 'src/Fernsehserien.csproj').findtext('.//Version')
tag = os.environ.get('GITHUB_REF_NAME', '')
if os.environ.get('GITHUB_REF_TYPE') == 'tag':
    if not re.fullmatch(r'v' + re.escape(version) + r'(?:-[0-9A-Za-z.-]+)?', tag):
        raise SystemExit('Tag must match project version (optional prerelease suffix).')
else:
    tag = 'v' + version
out = root / 'artifacts'
out.mkdir(exist_ok=True)
dll = root / 'src/bin/Release/netstandard2.0/Emby.Plugin.Fernsehserien.dll'
(out / dll.name).write_bytes(dll.read_bytes())
with zipfile.ZipFile(out / f'fernsehserien-{tag}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    z.write(dll, dll.name)
    for path in ['README.md', 'docs/usage.md', 'THIRD-PARTY-NOTICES.md', 'src/HtmlParser/LICENSE.txt']:
        z.write(root / path, path)
files = sorted(p for p in out.iterdir() if p.suffix in ['.dll', '.zip'])
(out / 'SHA256SUMS').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}\n' for p in files))
print(f'Packaged {tag}: single plugin DLL, ZIP and SHA256SUMS')
