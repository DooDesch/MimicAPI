# MimicAPI

A reflection / helper library for building MelonLoader mods for MIMESIS.

## Building

The build resolves the MIMESIS and MelonLoader assemblies from a local game
install. Point it at your install in any of these ways (highest precedence first):

1. Command line:
   `dotnet build -c Release -p:GamePath="D:\Steam\steamapps\common\MIMESIS"`
2. Environment variable: set `MIMESIS_PATH` to the game install root.
3. A `Directory.Build.user.props` next to `MimicAPI/Directory.Build.props`
   (gitignored - not committed):
   ```xml
   <Project>
     <PropertyGroup>
       <GamePath>D:\Steam\steamapps\common\MIMESIS</GamePath>
     </PropertyGroup>
   </Project>
   ```
4. Auto-detection of a Steam install in the common locations.

Then build:

```
dotnet build -c Release
```

See `MimicAPI/Directory.Build.props` for details, including overriding
`GameAssemblyPath` / `MelonLoaderPath` / `PluginsPath` directly for non-standard
layouts. The game is Mono/net35, so MelonLoader assemblies are taken from
`MelonLoader/net35`.
