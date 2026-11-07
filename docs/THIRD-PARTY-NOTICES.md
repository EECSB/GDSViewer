# Third-party notices

This project itself is released into the public domain — see [UNLICENSE](../UNLICENSE). The components below
are not ours and keep their own terms. All of them are permissive; all but one ask for nothing beyond
carrying the notice along, which is what this file is for. The one is Open Iconic's font: its SIL Open Font
License also asks that the font not be sold by itself, that it stay under that license whether changed or not,
and that a changed version not use a reserved font name — Open Iconic declares none.

## Compiled into the app

These are part of what a browser downloads.

| Component | Version | License | Where |
|---|---|---|---|
| [Clipper2](https://github.com/AngusJohnson/Clipper2) | 2.0.1 | [Boost Software License 1.0](../GdsII/Clipper2/LICENSE) | [`GdsII/Clipper2/`](../GdsII/Clipper2), vendored as source |
| [Monaco Editor](https://github.com/microsoft/monaco-editor) | 0.41.0 | MIT | [`wwwroot/lib/monaco/`](../wwwroot/lib/monaco) |
| [three.js](https://github.com/mrdoob/three.js) | 0.152.0 | MIT | [`wwwroot/lib/three/`](../wwwroot/lib/three) |
| [Bootstrap](https://github.com/twbs/bootstrap) | 5.1.0 | MIT | [`wwwroot/css/bootstrap/`](../wwwroot/css/bootstrap) |
| [Open Iconic](https://github.com/iconic/open-iconic) | 1.1.1 | [SIL Open Font License 1.1](../wwwroot/css/open-iconic/FONT-LICENSE) for the font, [MIT](../wwwroot/css/open-iconic/ICON-LICENSE) for the icons | [`wwwroot/css/open-iconic/`](../wwwroot/css/open-iconic) |
| [Roboto](https://github.com/googlefonts/roboto-2) | 2.137 | [Apache License 2.0](../wwwroot/css/fonts/Roboto/LICENSE.txt) | [`wwwroot/css/fonts/Roboto/`](../wwwroot/css/fonts/Roboto) |
| [QrCodeGenerator](https://github.com/manuelbl/QrCodeGenerator) | 2.0.6 | MIT | `Net.Codecrete.QrCodeGenerator`, a package reference |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.15 | Apache License 2.0 | `PdfPig`, a package reference, for a PDF sent to an AI model as its text |
| [ASP.NET Core Blazor WebAssembly](https://asp.net/) | 10.0.10 | MIT | `Microsoft.AspNetCore.Components.WebAssembly`, a package reference |

The first six are **vendored** — checked in as plain files rather than pulled by a package manager — so
that `dotnet build` is the whole build and the app keeps working offline as a PWA. See
[`wwwroot/lib/README.md`](../wwwroot/lib/README.md) and
[`GdsII/Clipper2/README.md`](../GdsII/Clipper2/README.md) for the first three; the stylesheet, the icons and the
font are under [`wwwroot/css/`](../wwwroot/css), each with its notice in its header or beside it.

The last three are ordinary NuGet references, because the no-package-manager rule is about **JavaScript** —
what a bundler would otherwise pull into `wwwroot`. A .NET package is compiled by the same `dotnet build`
that compiles the app, needs no second toolchain, and is restored offline from the cache after the first
time. The licenses were read out of the packages' own `.nuspec` rather than off nuget.org's page, for the
reason [LibTessDotNet](#used-by-the-command-line-tool-only) gives below: the two can disagree.

## Used by the command-line tool only

These reach a developer's machine and never a browser, which is why they are ordinary package references.

| Component | Version | License | Used for |
|---|---|---|---|
| [LibTessDotNet](https://github.com/speps/LibTessDotNet) | 1.1.15 | SGI Free Software License B 2.0 | triangulating polygons for the 3D model exporters |
| [SharpGLTF.Toolkit](https://github.com/vpenades/SharpGLTF) | 1.0.6 | MIT | writing GLTF |
| [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.Core`) | 2.2.0 | Apache License 2.0, and MIT | `gds mcp`: talking the Model Context Protocol |
| [Microsoft.Extensions.AI.Abstractions](https://github.com/dotnet/extensions) | 10.8.3 | MIT | brought by the MCP SDK |
| [Microsoft.Extensions.Logging.Abstractions and DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | 10.0.10 | MIT | brought by the MCP SDK |

**The MCP SDK is under two licenses at once.** Its project is moving from MIT to Apache-2.0: new code is
Apache-2.0, and code whose authors have not yet agreed to relicense it stays MIT, so both apply to the one
assembly. Apache-2.0 asks for the license itself to travel with the code, and the SDK's package carries no copy
of it, so `GdsII.Cli`'s does: its own license file, as the SDK's repository has it at the commit 2.2.0 was built
from, under `licenses/modelcontextprotocol/` -
[`GdsII.Cli/licenses/modelcontextprotocol/LICENSE`](../GdsII.Cli/licenses/modelcontextprotocol/LICENSE) here.
The repository has no `NOTICE` file.

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
[`wwwroot/resources/GDS Files/Sky130 GDS/`](../wwwroot/resources/GDS%20Files/Sky130%20GDS) are SkyWater's, published under the
Apache License 2.0. `Mosfet.gds` beside them is ours. GF180's and IHP's are below, under [GF180MCU](#gf180mcu) and
[IHP SG13G2](#ihp-sg13g2).

The three drawings sky130's README shows, under
[`sky130-images/`](../wwwroot/resources/GDS%20Files/sky130-images), are the PDK's own, from its documentation in
[google/skywater-pdk @ 7198cf6](https://github.com/google/skywater-pdk/tree/7198cf647113f56041e02abf3eb623692820c5e1/docs)
under the Apache License 2.0, bundled unchanged: `metal_stack.svg` from `docs/_static/`, and
`cross-section-nfet_01v8.svg` and `cross-section-cap_mim.svg` from `docs/rules/device-details/`. The README itself
is ours, written from those documents.

The ten designs under [`Sky130 Designs/`](../wwwroot/resources/GDS%20Files/Sky130%20Designs) are their
authors', each published under the Apache License 2.0 and bundled unchanged: every file is byte for byte the
one at the commit linked below. None of their repositories carries a `NOTICE` file. The license's text ships
beside all of the sample data as
[`LICENSE-Apache-2.0.txt`](../wwwroot/resources/GDS%20Files/LICENSE-Apache-2.0.txt), which is what the
app's credit for a design links to.

| Design | Author | Project | File taken from |
|---|---|---|---|
| `tt_um_mcs4_cpu.gds` | Munetomo Maruyama | [ttsky25a_MCS4_CPU](https://github.com/munetomo-maruyama/ttsky25a_MCS4_CPU) | [TinyTapeout/tinytapeout-sky-25a @ 85e372c](https://github.com/TinyTapeout/tinytapeout-sky-25a/blob/85e372cfc19e94d7b572a6feae9174bc21070982/projects/tt_um_mcs4_cpu/tt_um_mcs4_cpu.gds) |
| `tt_um_vga_clock.gds` | Matt Venn | [tt08-vga-clock](https://github.com/mattvenn/tt08-vga-clock) | [TinyTapeout/tinytapeout-08 @ b4342ed](https://github.com/TinyTapeout/tinytapeout-08/blob/b4342ed12fe5759cf989cc621f22d3e4d539341d/projects/tt_um_vga_clock/tt_um_vga_clock.gds) |
| `rgb_mixer.gds` | Matt Venn | [caravel_rgb_mixer](https://github.com/mattvenn/caravel_rgb_mixer) | [mattvenn/caravel_rgb_mixer @ c0ba2c4](https://github.com/mattvenn/caravel_rgb_mixer/blob/c0ba2c47d83113218372ddb692c9a458586e2544/gds/rgb_mixer.gds) |
| `tt_um_rnunes2311_12bit_sar_adc.gds` | Ricardo Nunes | [tt07-12bit_SAR_ADC](https://github.com/rnunes2311/tt07-12bit_SAR_ADC) | [TinyTapeout/tinytapeout-07 @ 239db05](https://github.com/TinyTapeout/tinytapeout-07/blob/239db054bafb02b90bfd25d141c6b22b3c54d7f1/projects/tt_um_rnunes2311_12bit_sar_adc/tt_um_rnunes2311_12bit_sar_adc.gds) |
| `tt_um_vaf_555_timer.gds` | Vincent Fusco | [tt06_555](https://github.com/vincentfusco/tt06_555) | [TinyTapeout/tinytapeout-06 @ 4f8caad](https://github.com/TinyTapeout/tinytapeout-06/blob/4f8caad5f922725e81f85e38e301f856363efbde/projects/tt_um_vaf_555_timer/tt_um_vaf_555_timer.gds) |
| `sky130_ef_ip__opamp.gds` | Efabless | [sky130_ef_ip__opamp](https://github.com/fossi-foundation/sky130_ef_ip__opamp) | [fossi-foundation/sky130_ef_ip__opamp @ f0eb5cd](https://github.com/fossi-foundation/sky130_ef_ip__opamp/blob/f0eb5cd47112d90fd6d7792ba68e763beacac8e7/gds/sky130_ef_ip__opamp.gds) |
| `sky130_ef_ip__rc_osc_500k.gds` | Tim Edwards, Efabless | [sky130_ef_ip__rc_osc_500k](https://github.com/RTimothyEdwards/sky130_ef_ip__rc_osc_500k) | [RTimothyEdwards/sky130_ef_ip__rc_osc_500k @ 343f448](https://github.com/RTimothyEdwards/sky130_ef_ip__rc_osc_500k/blob/343f4481b2199efe4ddba48428c89fcf1029ae9a/gds/sky130_ef_ip__rc_osc_500k.gds) |
| `sky130_fd_io__top_gpiov2.gds` | SkyWater Technology | [sky130_fd_io](https://github.com/fossi-foundation/skywater-pdk-libs-sky130_fd_io) | [fossi-foundation/skywater-pdk-libs-sky130_fd_io @ 3ea3051](https://github.com/fossi-foundation/skywater-pdk-libs-sky130_fd_io/blob/3ea30515f909f1c2f7e6ee887e83bd7fb4a78b90/cells/top_gpiov2/sky130_fd_io__top_gpiov2.gds) |
| `tt_um_urish_skullfet.gds` | Uri Shaked | [tt05-skullfet](https://github.com/urish/tt05-skullfet) | [TinyTapeout/tinytapeout-05 @ 7748264](https://github.com/TinyTapeout/tinytapeout-05/blob/774826437e2dd0f939a2b20154ab0b62e262020b/projects/tt_um_urish_skullfet/tt_um_urish_skullfet.gds) |
| `tt_um_oscillating_bones.gds` | Uri Shaked | [tt08-oscillating-bones](https://github.com/urish/tt08-oscillating-bones) | [TinyTapeout/tinytapeout-09 @ c970178](https://github.com/TinyTapeout/tinytapeout-09/blob/c9701782ae3a61874681404a88c7ec2c23476991/projects/tt_um_oscillating_bones/tt_um_oscillating_bones.gds) |

Tiny Tapeout's terms leave each project with its author and keep the combined chip as Tiny Tapeout's own, so
what is bundled is each project's own file and never a whole shuttle. The same list, with a sentence on each
design, is [`designs.json`](../wwwroot/resources/GDS%20Files/designs.json) — the file the app reads its
credits from.

### GF180MCU

The 229 GF180 standard cells under [`GF180 GDS/`](../wwwroot/resources/GDS%20Files/GF180%20GDS) are GlobalFoundries',
the 7-track `gf180mcu_fd_sc_mcu7t5v0` library, published under the Apache License 2.0 and bundled unchanged, each byte
for byte the file at
[fossi-foundation/globalfoundries-pdk-libs-gf180mcu_fd_sc_mcu7t5v0 @ 0ef8889](https://github.com/fossi-foundation/globalfoundries-pdk-libs-gf180mcu_fd_sc_mcu7t5v0/tree/0ef8889a8df0acf959683fbe73013f342a2206a2/cells).
The library has 249 files in all, twenty of them the same cell kept in two folders; each is bundled once.

The cross-section gf180mcu's README shows,
[`gf180mcu-images/cross-section-1p5m-tm11k-mim-b.png`](../wwwroot/resources/GDS%20Files/gf180mcu-images), is the
PDK's own - `docs/analog/layout/inter_specs/images/2_cross_section_43.png` in
[google/gf180mcu-pdk @ de3240d](https://github.com/google/gf180mcu-pdk/tree/de3240d7529a6970437ac3344820aaae7839f215/docs)
- under the Apache License 2.0, bundled unchanged, and so is the design manual's drawn-layer table vendored at
[`OtherResources/GF180/06_Drawn_layer10.csv`](../OtherResources/GF180/06_Drawn_layer10.csv), which the tests hold the
layermap to. The README, the layermap and the deck are ours, written from the PDK's documents.

**The GF180 designs are not shipped.** The Examples picker lists eleven under **GF180 Designs**, credited like the sky130
ones, and each is downloaded from its own repository when somebody opens it - the raw file at the commit
[`gf180-designs.json`](../wwwroot/resources/GDS%20Files/gf180-designs.json) names, which is also the list of who made
each and under what terms. Every one is published under the Apache License 2.0.

### IHP SG13G2

The 84 SG13G2 standard cells under [`IHP GDS/`](../wwwroot/resources/GDS%20Files/IHP%20GDS) are IHP's, the
`sg13g2_stdcell` library, published under the Apache License 2.0 as one library,
[`sg13g2_stdcell.gds` in IHP-GmbH/IHP-Open-PDK @ 5e6d592](https://github.com/IHP-GmbH/IHP-Open-PDK/tree/5e6d592e4002946a4616f798c357f0f3c06cf3b6/ihp-sg13g2/libs.ref/sg13g2_stdcell/gds),
and split here into a file per cell: each file is that library's header records, one cell's records byte for byte,
and the record that ends a library. The cells place nothing, so each is whole on its own.

Vendored at [`OtherResources/IHP/`](../OtherResources/IHP), from the same commit and under the same license, unchanged:
the PDK's KLayout layer properties `sg13g2.lyp`, its 2.5D stack script `sg13g2_beol.lyd25`, and its rule tables
`main_rules.md` and `extra_rules.md`. Beside them, `sg13g2-layer-table.csv` is the layer table of chapter 2 of
`SG13G2_os_layout_rules.pdf`, read out of the PDF with pdftotext and checked row for row against `sg13g2.lyp`. The
tests hold the layermap and the deck to these. The README, its drawing of the stack, the layermap and the deck are
ours, written from the PDK's documents.

**The IHP designs are not shipped.** The Examples picker lists eleven under **IHP Designs**, credited like the others,
and each is downloaded from its own repository when somebody opens it - the raw file at the commit
[`ihp-designs.json`](../wwwroot/resources/GDS%20Files/ihp-designs.json) names, which is also the list of who made each
and under what terms. Every one is published under the Apache License 2.0.

## Not shipped

[KLayout](https://www.klayout.de/) is used by the test suite as a second implementation to check this one
against — it reads what this writes, and the OASIS fixtures are its output. It is not distributed here and
is not required to build or run anything; the tests that need it say so.

Nor are the build's and the tests' own packages: the Blazor dev server
(`Microsoft.AspNetCore.Components.WebAssembly.DevServer` 10.0.10, `PrivateAssets="all"`) and the test
project's `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.8.1 and
`coverlet.collector` 10.0.1. Nothing of them reaches the site, the library or the tool.
