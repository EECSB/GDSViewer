# Vendored: Clipper2

| Library | Version | Files | Source |
|---|---|---|---|
| Clipper2 | 2.0.1 | `*.cs` | [AngusJohnson/Clipper2](https://github.com/AngusJohnson/Clipper2), tag `Clipper2_2.0.1`, `CSharp/Clipper2Lib/` |

Licensed under the [Boost Software License 1.0](LICENSE), which is permissive and requires only that the
licence text travel with the source. It is [LICENSE](LICENSE) here, and named in
[../../docs/THIRD-PARTY-NOTICES.md](../../docs/THIRD-PARTY-NOTICES.md).

## Why the source is checked in rather than referenced as a package

[`GdsII`](..) has no `PackageReference` and is not going to get one. Everything in it is compiled into the
WebAssembly the browser downloads, and the rule the rest of the app follows — third-party code is vendored
as plain files, `dotnet build` is the whole build — applies to what ships whatever language it is written
in. The CLI is the project that takes packages, because nothing it references reaches a browser.

Checked in unmodified, and marked `-text -diff` in
[`.gitattributes`](../../.gitattributes) so a clone cannot hand them to a line-ending filter and a version
bump does not produce a ten-thousand-line diff. **Replace them wholesale rather than editing them**; if
something needs changing, it belongs in [`Booleans.cs`](../Geometry/Booleans.cs) on our side of the line.

## Which files, and why all of them

All nine of `CSharp/Clipper2Lib/`. The engine, the offsetter and the convenience API are what
[`Booleans.cs`](../Geometry/Booleans.cs) actually calls; the rest are referenced from those and will not compile
without them. The two that are not obviously needed are worth naming:

| File | Why it is here |
|---|---|
| `Clipper.Triangulation.cs` | new in 2.x and reachable from `Clipper.cs`; nothing here calls it |
| `Clipper.Minkowski.cs` | likewise, three kilobytes, and removing it means editing `Clipper.cs` |

Not vendored: `Clipper2Lib.csproj` (the files are compiled by [`GdsII.csproj`](../GdsII.csproj) along with
everything else) and `Clipper2.snk` (a signing key for the upstream package, which this does not build).

`USINGZ` is the one conditional in the sources — it adds a Z coordinate to every point. It is left
undefined, which is the plain two-dimensional build.
