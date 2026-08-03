# AoM Proto Editor

The standalone AoM Proto Editor is built and published independently of the
CryBar Editor interface. Configure the `Data.bar` and Steam user-folder paths
from **Settings** on first launch.

## Run the published editor

Launch `publish\AoMProtoEditor.exe`.

The published build is self-contained for 64-bit Windows; keep the files in the
`publish` folder together with `AoMProtoEditor.exe`. To recreate it from this
folder, run:

```powershell
dotnet publish .\AoMProtoEditor.csproj -c Release -o .\publish
```
