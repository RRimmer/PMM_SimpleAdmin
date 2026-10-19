# lib

`0Harmony.dll` — [Lib.Harmony](https://github.com/pardeike/Harmony) 2.4.2, `lib/net10.0` (MIT license).
It is kept in the repository so the plugin builds without the NuGet package. The build copies it to
`bin/.../harmony/0Harmony.dll`. On the server it must stay in `plugins/PMM_SimpleAdmin/harmony/`:
the plugin loads it into the default (non-collectible) load context itself.
If PMM_VIPCore, PMM_StoreCore or PMM_GG1MapChooser is installed too, the copy it already loaded is reused.

No CS2-SimpleAdmin, VIPCore, GG1MapChooser or cs2-store dll is needed for the build: the plugin reaches
their APIs by reflection at run time.

`../native/linux-x64/libe_sqlite3.so` — SQLite 3.49.1 for Linux built for GLIBC 2.28 (the same file as in
PanoramaMenuManager core). The NuGet build of SQLitePCLRaw needs GLIBC 2.34, which older server distributions do not have.
The build copies it to `bin/.../runtimes/linux-x64/native/`.
