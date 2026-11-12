# Grass Distance Fix

A client plugin for Space Engineers 1 and Pulsar on Windows (.NET Framework). Grass keeps its material size as you move away, apart from the existing LOD transitions and the fade at the draw limit. Distance thinning still runs, so distant grass can look sparser. The patch applies to the vanilla grass renderer. Foliage Rewrite has its own shader and bypasses this fix when its rewrite is enabled.

## Build and load

```powershell
dotnet build -c Release -p:GameBin64="D:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64"
```

In Pulsar, add `bin/Release/net48` as a development folder in the plugin sources menu. Enable **GrassDistanceFix** and restart the game. Pulsar supplies Harmony. Disable the plugin and restart to restore the original shader.

For a running game with the SE MCP bridge, use its PulsarHelper to load the DLL. Live load and unload both queue the game's built-in shader reload.

## Verification

On EarthLike, use a stationary patch of grass with a landmark and move the spectator camera back at a low, consistent angle. Compare the same patch with the plugin disabled and enabled. Check that grass no longer swells at intermediate distances, that it still fades near the configured draw limit, and that pebble foliage looks the same.
