# Portable storage experiment

The project links the production DatabaseCore, four stores, models, clock, observable base, localized strings and source-generated startup snapshot directly from JoJot. The schema, migrations, database worker, write serialization and timestamp behavior therefore remain the production implementation. It targets net11.0 and enables AOT compatibility analysis.

The host must choose an isolated experiment directory and call `AppEnvironment.SetAppDataDirectory(directory)` before preferences/snapshot operations; pass an explicit database path to `DatabaseCore.OpenAsync`. The adapter supplies the snapshot directory and forwards storage log calls to Serilog's host-configured logger. It does not configure production logging or WPF UI-thread diagnostics. No production files are changed.

The tests copy the genuine EF-generated fixture and exercise schema versions 0, 1 and 2, existing data from each store, and note create/update/reopen/delete persistence. They do not establish overall UI or Native AOT publish correctness.
