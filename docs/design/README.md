# KANDOO — DESIGN ASSETS (permanent index)

The visual specification of every screen lives in ONE design canvas (46 boards, 5 pages). Style **v2** (cards, light, icons) is the approved style for all screens. Colors: honey, ink, cream, wax, green, brown (tokens in `Main.dc.html` and in `src/Kandoo.App/Resources/DesignTokens.xaml`).

- Live canvas (owner's Claude account, agents cannot open it): https://claude.ai/artifact/Tw99UBEonFnLKx9Jo9J7R2
- One-page gallery of 31 earlier boards: https://claude.ai/artifact/UzcmJrcFS3pNmcZ7mR5zH7
- **Repository copy (source for agents): `docs/design/canvas/`** = the canvas `project/` folder, one `.dc.html` file per board (plain HTML with inline CSS, flex/grid layout, fixed frame size in px). Read the file of a board to get exact layout, spacing, colors, texts and states. The file `canvas.json` has frame sizes, titles and pages.
  The PO uploads this folder (zip exported by the PM on 2026-11-22). Until `docs/design/canvas/` exists on `main`, agents must not start screen-fidelity tasks.

## Pages and boards (file -> screen)

**Page 1 — new style v2 (start here)**: `Role-Select-v2` role selection (390x844); `Phone-Review-v2` store review queue (phone); `Phone-Invoice-v2` invoice issue (phone); `Phone-Product-v2` product create (phone); `Customer-Login-v2` customer login; `Customer-Invoices-v2` my invoices (customer); `Customer-Invoice-v2` customer web invoice page (receipt look, sample data).

**Page 2 — Windows store screens v1 (1280 wide, to be redone in v2 in this order)**: `Setup` first-run setup; `Review` review queue; `Invoice` invoice issue; `Invoices` invoices list; `Products` product list; `Product` product create; `Product-Edit` product edit and stock; `Customers`; `Return` invoice return; `Receivables`; `Pro-View` journal (professional view); `Inventory-Value`; `Sales-Report`; `Accounting` home; `Devices` devices and access; `Settings`; `Assistant`; `States` empty and error states.

**Page 3 — store phone v1 (390 wide)**: `Phone-Pair`, `Phone-Review`, `Phone-Product`, `Phone-Invoice`, `Phone-Invoices`, `Phone-Products`, `Phone-Customers`, `Phone-Return`, `Phone-Accounting`, `Phone-More`, `Phone-Assistant`.

**Page 4 — customer v1 (390 wide)**: `Customer-Login`, `Customer-Invoices`, `Customer-Invoice-Detail`, `Customer-New-Banner`, `Customer-Empty`, `Customer-Locked` (code exhausted), `Customer-Invoice` (old web page).

**Page 5 — brand and logo**: `Main` visual identity (color and type), `Brand-Logo` three PM proposals (rejected), `Brand-Logo-Owner` the owner's logo, flat redraws (not yet chosen).

## Rules for agents
1. The board file is the specification; a screen is accepted only when it matches its board (layout, spacing, colors, texts, RTL) in a screenshot comparison reported by the lane. Where a v1 board exists and no v2 exists, build v2 style from the same content (cards, light, icons) and say so in the report.
2. RTL: page content is right-to-left (`FlowDirection=RightToLeft` on page content). The Windows title-bar buttons (minimize, maximize, close) stay on the RIGHT edge of the window; do not mirror the window frame.
3. Never invent screens or copy that are not on a board; list questions in the report.
4. New or changed designs are added by the PM to the canvas and re-exported here; agents never edit `docs/design/canvas/`.
