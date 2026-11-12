# EF SQLite fixture

`ef-sqlite-v2.db` was generated from the unmodified JoJot EF Core implementation at commit `b1694cff743c68db9d60e3dad2baba5817defdb7`, with EF Core SQLite 11.0.0 and .NET SDK 11.0.100. SHA-256: `585FF254838650A80E013CC60403E86139A805CA74C79E6E528DD2C0EDB22B18`.

It contains 100 default-desktop notes, a nullable legacy session and two additional notes (including a file-backed note), preferences, and a pending move. It was closed and checkpointed before capture. Tests copy it before opening and derive version 0 and 1 shapes from the copy.

To regenerate, check out the baseline commit, copy the current `JoJot.Benchmarks` source into that isolated checkout, then run `dotnet run --project JoJot.Benchmarks/JoJot.Benchmarks.csproj -c Release -- seed-compatibility <new-directory>`. The output also includes a schema dump; copy only the closed `jojot.db` into this directory. Review fixture-content and metadata differences before replacing it.
