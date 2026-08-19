# Third-party notices

This project itself is released into the public domain — see [UNLICENSE](../UNLICENSE). The components below
are not ours and keep their own terms. All of them are permissive; none of them asks for anything beyond
carrying the notice along, which is what this file is for.

## Compiled into the app

These are part of what a browser downloads.

| Component | Version | License | Where |
|---|---|---|---|
| [Clipper2](https://github.com/AngusJohnson/Clipper2) | 2.0.1 | [Boost Software License 1.0](../GdsII/Clipper2/LICENSE) | [`GdsII/Clipper2/`](../GdsII/Clipper2), vendored as source |
| [Monaco Editor](https://github.com/microsoft/monaco-editor) | 0.41.0 | MIT | [`wwwroot/lib/monaco/`](../wwwroot/lib/monaco) |
| [three.js](https://github.com/mrdoob/three.js) | 0.152.0 | MIT | [`wwwroot/lib/three/`](../wwwroot/lib/three) |
| [QrCodeGenerator](https://github.com/manuelbl/QrCodeGenerator) | 2.0.6 | MIT | `Net.Codecrete.QrCodeGenerator`, a package reference |
| [ASP.NET Core Blazor WebAssembly](https://asp.net/) | 10.0.10 | MIT | `Microsoft.AspNetCore.Components.WebAssembly`, a package reference |

The first three are **vendored** — checked in as plain files rather than pulled by a package manager — so
that `dotnet build` is the whole build and the app keeps working offline as a PWA. See
[`wwwroot/lib/README.md`](../wwwroot/lib/README.md) and
[`GdsII/Clipper2/README.md`](../GdsII/Clipper2/README.md).

The last two are ordinary NuGet references, because the no-package-manager rule is about **JavaScript** —
what a bundler would otherwise pull into `wwwroot`. A .NET package is compiled by the same `dotnet build`
that compiles the app, needs no second toolchain, and is restored offline from the cache after the first
time. Both licenses were read out of the packages' own `.nuspec` rather than off nuget.org's page, for the
reason [LibTessDotNet](#used-by-the-command-line-tool-only) gives below: the two can disagree.

## Used by the command-line tool only

These reach a developer's machine and never a browser, which is why they are ordinary package references.

| Component | Version | License | Used for |
|---|---|---|---|
| [LibTessDotNet](https://github.com/speps/LibTessDotNet) | 1.1.15 | SGI Free Software License B 2.0 | triangulating polygons for the 3D model exporters |
| [SharpGLTF.Toolkit](https://github.com/vpenades/SharpGLTF) | 1.0.6 | MIT | writing GLTF |

**LibTessDotNet's package and its source disagree**, and the stricter of the two is the one recorded
above. Its `.nuspec` declares `MIT`, which is what nuget.org shows; its repository's `LICENSE.txt` and
README both say SGI Free Software License B 2.0, and the code is a port of SGI's GLU tessellator, which is
where that license comes from. A packaging slip in the more permissive direction is the likelier of the
two explanations, so SGI-B-2.0 is what `GdsII.Cli` declares. Both are permissive and neither is copyleft,
so nothing turns on it beyond saying the right thing.

Note that this is a real obligation for `GdsII.Cli` and not for the app: packing as a .NET tool puts these
assemblies **inside** the package, where an ordinary reference would leave them as dependencies with their
own listings.

## Sample data

The 896 [sky130](https://skywater-pdk.readthedocs.io/en/main/) standard cells under
[`wwwroot/resources/GDS Files/`](../wwwroot/resources/GDS%20Files) are SkyWater's, published under the
Apache License 2.0. `Mosfet.gds` beside them is ours.

## Not shipped

[KLayout](https://www.klayout.de/) is used by the test suite as a second implementation to check this one
against — it reads what this writes, and the OASIS fixtures are its output. It is not distributed here and
is not required to build or run anything; the tests that need it say so.
