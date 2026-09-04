# Changelog

All changes to MimicAPI are in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/), and the project uses Semantic
Versioning.

## [0.4.0]

### Added

- `McpPlugin`: a set of static delegates that a MelonLoader MCP bridge finds by reflection. It gives
  the commands `get_session_state`, `get_world_state`, `list_rooms`, `get_room` and `get_players`,
  and it tells the bridge if this machine is the host. All types in the signatures come from the
  standard library, thus no assembly must reference the other one.
- `ServerNetworkAPI.GetPlayerCountInSession()`: the number of players in the session.
- `ServerNetworkAPI.GetSteamServerSocket()`: the `ServerSocket` of the Steam transport. It is a
  private field of the `FishySteamworks` component, not a field of `VWorld`.

### Changed

- `ServerNetworkAPI.GetMaximumClients()` now reads the limit of the server, as the name says. Before
  it gave the number of players in the session. With FakePlayers the two numbers are close together,
  thus the mistake was not visible.
- The version of the DLL comes from the tag. Before, every build said 1.0.0.

### Fixed

- The Thunderstore job runs only in the upstream repository. Only that repository can publish in the
  namespace NeoMimicry, thus a tag on a fork made a failed run.
- The package description on Thunderstore says what the package is.
