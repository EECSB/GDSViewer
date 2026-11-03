# GlobalFoundries GF180MCU

**GF180MCU** is the open-source process design kit (PDK) for GlobalFoundries' **0.18 µm 3.3 V / 6 V MCU process**, released
by Google and GlobalFoundries in 2022 under the **Apache License 2.0**. After SkyWater's SKY130, it was the second working
foundry process that anybody could download, read and design with, without a non-disclosure agreement.

The process has made commercial chips in volume for years. The *open PDK*, on the other hand, is still labeled an
**experimental preview**: usable for test chips and early design work, not yet promised for production.

## The process

- **3.3 V** core transistors, and **6 V** ones on a thick gate oxide drawn under `Dualgate`, which the design manual also
  gives 5 V rules for. A **native** 6 V NMOS, and high-voltage **LDMOS** transistors.
- A minimum channel length of **0.28 µm** at 3.3 V, and 0.5 to 0.7 µm for the 5 V and 6 V devices.
- **Two to six metals**, the top one thick (6K, 9K, 11K or 30K Å), and an optional **MiM capacitor** of 1, 1.5 or
  2 fF/µm².
- Resistors in diffusion, well, poly and metal, among them a **high-sheet-resistance poly** of 1, 2 or 3 kΩ/□. Also
  vertical **BJTs**, diodes, MOS capacitors and an **eFuse**.

The open PDK is built in four stacks, each one a PDK of its own in open_pdks:

| PDK | Metals | Top metal |
|---|---|---|
| `gf180mcuA` | 3 | 30K Å, 3.0 µm |
| `gf180mcuB` | 4 | 11K Å, 1.1 µm |
| `gf180mcuC` | 5 | 9K Å, 0.9 µm |
| `gf180mcuD` | 5 | 11K Å, 1.1 µm |

**This PDK is gf180mcuD.** open_pdks added it in 2023 because GlobalFoundries prefers the thicker top metal for its
wire-bonding yield, and said every open shuttle after the first would use it. In a five-metal stack the top metal is drawn
on `Metal5` and follows the MetalTop rules, and the MiM capacitor sits between `Metal4` and `Metal5`.

![The gf180mcuD stack in cross-section: poly on the trench isolation under a conformal nitride and the ILD, then Metal1 to Metal4 each 0.55 µm thick in its own 1.15 µm IMD, the MiM capacitor's top plate over Metal4 in the thicker IMD4, and the 1.19 µm top metal under the passivation, with each thickness marked](gf180mcu-images/cross-section-1p5m-tm11k-mim-b.png)

*The gf180mcuD stack in cross-section, from the PDK's interconnect specification. These are the numbers in the
layermap's height and thickness columns.*

## Layers

The drawing layers, by their GDS numbers as `layer/datatype`. A layer's labels are its datatype 10 (`34/10` for `Metal1`),
and its dummy fill is datatype 4. Note that the numbers do not climb with the stack: `Via2` is 38, `Metal3` 42, `Via3` 40
and `Via4` 41, and `Metal5` is 81.

| Layer | GDS | What it is |
|---|---|---|
| `DNWELL` | 12/0 | Deep n-well |
| `Nwell` | 21/0 | N-well |
| `LVPWELL` | 204/0 | P-well |
| `COMP` | 22/0 | Active area: a transistor's source, drain and channel, and the taps |
| `Dualgate` | 55/0 | The thick gate oxide of the 5 V and 6 V devices |
| `Poly2` | 30/0 | Polysilicon: the gates, and interconnect |
| `Nplus` | 32/0 | N+ implant |
| `Pplus` | 31/0 | P+ implant |
| `SAB` | 49/0 | Salicide block: poly and active left unsalicided, as in a resistor |
| `Contact` | 33/0 | Contact from `COMP` and `Poly2` up to `Metal1` |
| `Metal1` | 34/0 | Metal 1 |
| `Via1` | 35/0 | Via from `Metal1` to `Metal2` |
| `Metal2` | 36/0 | Metal 2 |
| `Via2` | 38/0 | Via from `Metal2` to `Metal3` |
| `Metal3` | 42/0 | Metal 3 |
| `Via3` | 40/0 | Via from `Metal3` to `Metal4` |
| `Metal4` | 46/0 | Metal 4 |
| `Via4` | 41/0 | Via from `Metal4` to `Metal5` |
| `Metal5` | 81/0 | Metal 5, the top metal in this stack |
| `FuseTop` | 75/0 | The MiM capacitor's top plate |
| `Pad` | 37/0 | Opening in the passivation over a bond pad |

The full table, with the markers and every purpose, is section 4.1 of the
[design manual](https://gf180mcu-pdk.readthedocs.io/en/latest/physical_verification/design_manual/drm_04_1.html).

## A few of its rules

The smallest width and spacing of the layers most drawn on, at 3.3 V, from the design manual. Everything is drawn on a
0.005 µm grid.

| Layer | Min width | Min spacing | Rules |
|---|---|---|---|
| `COMP` | 0.22 µm | 0.28 µm | DF.1a, DF.3a |
| `Poly2` | 0.18 µm | 0.24 µm | PL.1, PL.3a |
| `Contact` | 0.22 µm, exactly | 0.25 µm | CO.1, CO.2a |
| `Metal1` | 0.23 µm | 0.23 µm | M1.1, M1.2a |
| `Metal2`–`Metal4` | 0.28 µm | 0.28 µm | Mn.1, Mn.2a |
| `Via1`–`Via4` | 0.26 µm, exactly | 0.26 µm | Vn.1, Vn.2a |
| `Metal5`, the top metal | 0.44 µm | 0.46 µm | MT.1, MT.2a |

The deck in this PDK checks 56 of them. See **Rules** in the tree. It catches the obvious; it is not a signoff deck.

## Libraries

Besides the primitive devices and their models, for ngspice and Xyce, in `gf180mcu_fd_pr`, the foundry provides:

- `gf180mcu_fd_sc_mcu7t5v0` and `gf180mcu_fd_sc_mcu9t5v0`: digital standard cells, 7 and 9 tracks tall.
- `gf180mcu_fd_io`: the I/O pad cells.
- `gf180mcu_fd_ip_sram`: SRAM macros, and `gf180mcu_fd_bd_sram`, the cells they are built from.

open_pdks adds Oklahoma State University's 3.3 V standard cells, `gf180mcu_osu_sc_gp9t3v3` and `gf180mcu_osu_sc_gp12t3v3`.

## Tools

Open-source flows support it end to end: Magic and KLayout for layout and DRC, KLayout and Netgen for LVS, ngspice and
Xyce for simulation, OpenROAD and LibreLane for digital place and route, and open_pdks to install it all.

## Making chips in it

Multi-project wafer shuttles put many small designs on one wafer to share its cost. Google and Efabless ran free GF180
shuttles: **GFMPW-0** in 2022, on `gf180mcuC`, and **GFMPW-1** in 2023, on `gf180mcuD`, until Efabless closed in 2025.
Since then **wafer.space** has run paid GF180MCU shuttles. Its first run took designs until 3 December 2025, taped out on
8 January 2026 and had its wafers back on 6 April. **Tiny Tapeout** puts very small designs on shared silicon through
them, and confirmed on 30 April 2026 that its first wafer.space chips work. Check each one for its current dates and
prices before planning around it.

Some of what has been made in it is in **Examples**, under **GF180 Designs**: Tiny Tapeout projects, an eFPGA tile,
analog blocks, and GlobalFoundries' own I/O pad and SRAM. None ships with the app. Each one is downloaded from its own
repository when it is opened, at the commit its credit names.

## Where to read more

- [GF180MCU PDK documentation](https://gf180mcu-pdk.readthedocs.io/), including the
  [design manual](https://gf180mcu-pdk.readthedocs.io/en/latest/physical_verification/design_manual/Design_Manual.html)
- [The PDK on GitHub](https://github.com/google/gf180mcu-pdk). Google's repositories are archived; the libraries are
  kept up under the [FOSSi Foundation](https://github.com/fossi-foundation)
- [open_pdks, which builds gf180mcuA to gf180mcuD](https://github.com/fossi-foundation/open-pdks)
- [wafer.space](https://wafer.space/) and [Tiny Tapeout](https://tinytapeout.com/)

*Written for GDS Viewer on 2026-11-02 from the documents above. The PDK's own pages are the authority where this and
they disagree. The cross-section is the PDK's own, section 3.43 of its interconnect specification, under the Apache
License 2.0.*
