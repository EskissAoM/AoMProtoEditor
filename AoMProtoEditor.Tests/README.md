# AoM Proto Editor regression tests

This test project intentionally lives inside the main `AoMProtoEditor` folder.
The desktop project excludes `AoMProtoEditor.Tests/**/*.cs` from its own compile items.

Run the full regression suite from the `AoMProtoEditor` folder:

```powershell
dotnet test .\AoMProtoEditor.Tests\AoMProtoEditor.Tests.csproj
```

For the normal pre-patch check, run:

```powershell
dotnet build
dotnet test .\AoMProtoEditor.Tests\AoMProtoEditor.Tests.csproj
```

The suite currently protects:

- numeric editor display/formatting;
- compact asset path suggestions;
- top-menu Tactics/Abilities manager wiring;
- the five ProtoUnit editor tabs and their order;
- safe ability XML loading and saving;
- no XML declaration in generated ability XML;
- ability string-ID normalization;
- mandatory Range Indicator range rule;
- preservation of unknown placement attributes;
- persisted Main/Aux ability action-binding ownership metadata;
- ownership metadata copying when a unit is duplicated;
- exact `armor`, `directionalarmor`, and `armoroverride` closing tags.

UI interactions such as dropdown focus, wrapping, and visual spacing still need manual smoke testing because they depend on live Avalonia pointer/focus behavior.
