# AoM Divine Data Editor

AoM Divine Data Editor is a standalone editor for Age of Mythology: Retold game
and mod data. Configure the `Data.bar` and Steam user-folder paths from
**Settings** on first launch.

## Run the published editor

Launch `publish\AoMDivineDataEditor.exe`.

The published build is self-contained for 64-bit Windows; keep the files in the
`publish` folder together with `AoMDivineDataEditor.exe`. To recreate it from this
folder, run:

```powershell
dotnet publish .\AoMDivineDataEditor.csproj -c Release -o .\publish
```
