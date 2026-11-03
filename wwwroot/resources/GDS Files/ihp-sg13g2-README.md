# IHP SG13G2

**SG13G2** is the open-source process design kit (PDK) for IHP's **0.13 µm SiGe:C BiCMOS process** - IHP being Innovations
for High Performance Microelectronics, in Frankfurt (Oder). IHP publishes it on GitHub, as IHP-Open-PDK, under the
**Apache License 2.0**.

What sets it apart is its **bipolar transistors**: SiGe heterojunction bipolar transistors (HBTs) with a transit frequency
of 300 GHz and a maximum oscillation frequency of 500 GHz, beside ordinary CMOS - which is why so much of what has been
made in it is radio and millimeter-wave work.

## The process

- **1.2 V** core transistors on a thin gate oxide, and **3.3 V** ones on a thick oxide drawn under `ThickGateOx`. Both
  come as NMOS, PMOS and isolated NMOS.
- **SiGe:C npn HBTs**, at 300 GHz f<sub>T</sub> and 500 GHz f<sub>max</sub>.
- Polysilicon resistors - `Rsil`, `Rppd` and the high-resistance `Rhigh` - MIM capacitors, inductors, a Schottky diode
  and ESD protection devices.
- **Seven metals**, all aluminum: five thin ones, `Metal1` 0.42 µm thick and `Metal2` to `Metal5` 0.49 µm each, then two
  thick top metals, `TopMetal1` at 2 µm and `TopMetal2` at 3 µm. The **MIM capacitor** sits between `Metal5` and
  `TopMetal1`.

![The SG13G2 stack, to scale: Activ in the 400 nm trench and gate poly on the surface, the contacts up to Metal1 at 0.64 µm, Metal1 to Metal5 each over a 0.54 µm via, the MIM plate over Metal5, then TopVia1, TopMetal1 at 6.03 µm and 2 µm thick, TopVia2, and TopMetal2 at 10.83 µm and 3 µm thick](ihp-sg13g2-images/metal-stack.svg)

*The SG13G2 stack to scale, drawn for GDS Viewer from the layer thicknesses of the PDK's process specification (2.16).
These are the numbers in the layermap's height and thickness columns.*

## Layers

The drawing layers, by their GDS numbers as `layer/datatype`. A layer's pins are its datatype 2, and the names of its nets
are text on datatype 25 (`8/25` for `Metal1`) - which is where IHP's LVS reads them. Its fill is datatype 22. Note that
the numbers do not climb with the stack: `Metal2` is 10, `Via2` 29, `Metal3` 30, `Metal5` 67 and `TopMetal1` 126.

| Layer | GDS | What it is |
|---|---|---|
| `NWell` | 31/0 | N-well |
| `nBuLay` | 32/0 | N buried layer: the bipolar transistors' sub-collector, and isolated NMOS |
| `Activ` | 1/0 | Active area: where transistors, diodes and capacitors are made |
| `ThickGateOx` | 44/0 | The thick gate oxide of the 3.3 V devices |
| `GatPoly` | 5/0 | Polysilicon: the gates, and interconnect |
| `nSD` | 7/0 | N+ source/drain implant |
| `pSD` | 14/0 | P+ source/drain implant |
| `SalBlock` | 28/0 | Salicide block: Activ and GatPoly left unsalicided, as in a resistor |
| `Cont` | 6/0 | Contact from `Activ` and `GatPoly` up to `Metal1` |
| `Metal1` | 8/0 | Metal 1 |
| `Via1` | 19/0 | Via from `Metal1` to `Metal2` |
| `Metal2` | 10/0 | Metal 2 |
| `Via2` | 29/0 | Via from `Metal2` to `Metal3` |
| `Metal3` | 30/0 | Metal 3 |
| `Via3` | 49/0 | Via from `Metal3` to `Metal4` |
| `Metal4` | 50/0 | Metal 4 |
| `Via4` | 66/0 | Via from `Metal4` to `Metal5` |
| `Metal5` | 67/0 | Metal 5 |
| `MIM` | 36/0 | The MIM capacitor's top plate |
| `Vmim` | 129/0 | Via from the MIM's plate up to `TopMetal1`, drawn in place of `TopVia1` so a tool does not read the capacitor as a short |
| `TopVia1` | 125/0 | Via from `Metal5` to `TopMetal1` |
| `TopMetal1` | 126/0 | The first thick top metal, 2 µm |
| `TopVia2` | 133/0 | Via from `TopMetal1` to `TopMetal2` |
| `TopMetal2` | 134/0 | The second thick top metal, 3 µm |
| `Passiv` | 9/0 | Opening in the passivation over a pad |

The full table, with every purpose and the layers of IHP's other processes, is chapter 2 of the layout rules. Chapter 3.2
lists layers that are **forbidden** in an SG13G2 design - `BiWind`, `BasPoly`, `EmPoly` and others - and IHP rejects a
layout carrying any of them.

## A few of its rules

The smallest width and spacing of the layers most drawn on, from the layout rules. Everything is drawn on a 5 nm grid;
`Activ`, `GatPoly` and the metals may turn at 45 degrees, the contacts and vias only at 90.

| Layer | Min width | Min spacing | Rules |
|---|---|---|---|
| `Activ` | 0.15 µm | 0.21 µm | Act.a, Act.b |
| `GatPoly` | 0.13 µm | 0.18 µm | Gat.a, Gat.b |
| `Cont` | 0.16 µm, exactly | 0.18 µm | Cnt.a, Cnt.b |
| `Metal1` | 0.16 µm | 0.18 µm | M1.a, M1.b |
| `Metal2`–`Metal5` | 0.20 µm | 0.21 µm | M2.a, M2.b and on |
| `Via1`–`Via4` | 0.19 µm, exactly | 0.22 µm | V1.a, V1.b and on |
| `TopVia1` | 0.42 µm, exactly | 0.42 µm | TV1.a, TV1.b |
| `TopMetal1` | 1.64 µm | 1.64 µm | TM1.a, TM1.b |
| `TopVia2` | 0.90 µm, exactly | 1.06 µm | TV2.a, TV2.b |
| `TopMetal2` | 2.00 µm | 2.00 µm | TM2.a, TM2.b |

The deck in this PDK checks 68 of them. See **Rules** in the tree. It catches the obvious; it is not a signoff deck.

## Libraries

Besides the primitive devices, `sg13g2_pr` - the transistors, HBTs, resistors and capacitors as parameterized cells - the
PDK provides:

- `sg13g2_stdcell`: 84 digital standard cells, which are this PDK's **IHP GDS Examples**.
- `sg13g2_io`: the I/O pad cells.
- `sg13g2_sram`: SRAM macros, single- and dual-port, from 64 words to 8192.

## Tools

The PDK carries its setup for KLayout - layout, DRC and LVS, and the parameterized cells - and for Magic and Netgen,
Xschem, ngspice, Xyce and Qucs-S, OpenROAD and LibreLane for digital place and route, and openEMS and Palace for
electromagnetic simulation.

## Making chips in it

IHP runs **open-source tapeouts** on its own multi-project wafers several times a year, and sends dies back to the
designers who ask for them. Every run since December 2023 is in IHP-Open-DesignLib, design by design. **Tiny Tapeout**
puts very small designs on shared silicon through them: its IHP shuttles TTIHP 0.2 and 25a went with the April 2025 run,
and 0.3 with May 2025's. Check each one for its current dates and terms before planning around it.

Some of what has been made in it is in **Examples**, under **IHP Designs**: Tiny Tapeout projects, analog and
millimeter-wave blocks from the open tapeouts, and IHP's own primitive devices and an SRAM. None ships with the app. Each
one is downloaded from its own repository when it is opened, at the commit its credit names.

## Where to read more

- [The PDK on GitHub](https://github.com/IHP-GmbH/IHP-Open-PDK), with the
  [layout rules](https://github.com/IHP-GmbH/IHP-Open-PDK/blob/main/ihp-sg13g2/libs.doc/doc/SG13G2_os_layout_rules.pdf)
  and the [process specification](https://github.com/IHP-GmbH/IHP-Open-PDK/blob/main/ihp-sg13g2/libs.doc/doc/SG13G2_os_process_spec.pdf)
- [IHP-Open-DesignLib](https://github.com/IHP-GmbH/IHP-Open-DesignLib), the designs of every open tapeout
- [IHP's Analog Academy](https://github.com/IHP-GmbH/IHP-AnalogAcademy), a course in analog design in this PDK
- [Tiny Tapeout](https://tinytapeout.com/)

*Written for GDS Viewer on 2026-11-02 from the documents above, at the PDK's commit 5e6d592. The PDK's own pages are the
authority where this and they disagree. The drawing of the stack is ours, made from the process specification's table.*
