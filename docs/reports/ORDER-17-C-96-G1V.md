# ORDER 17 — C-96 group 1 verification (Setup + Role selection)

**Lane:** C# · **Stage:** T3 stage 3 · **Boards read first:** `docs/design/README.md`, then `Setup.dc.html`, `Role-Select-v2.dc.html` · **Behaviour source:** `docs/product-ux-spec.md`

Method: the board file is the specification, so each screen was compared **field by field** against its board — every text string, colour token, control and step. The diff below is from that reading, not from a visual guess. Screenshot capture is wired into CI (`scripts/Capture-WindowsScreens.ps1` → artifact `kandoo-windows-screens-c96`) so the next round can compare pictures as well.

---

## Screen 1 — Role selection ↔ `Role-Select-v2.dc.html`

| # | Board | Built screen (before) | Status |
|---|---|---|---|
| 1 | Title `به کندو خوش آمدید` | `کندو` | **DIFFERS → fixed** |
| 2 | Heading `نقش خود را انتخاب کنید` | `انتخاب نقش کاربر` | **DIFFERS → fixed** |
| 3 | Role 1 label `فروشگاه`, sub `فاکتور، کالا و مشتری‌های فروشگاه خودم` | `فروشگاه نمونه`, no sub | **DIFFERS → fixed (label + accessibility text)** |
| 4 | Role 2 label `مشتری`, sub `فاکتورهای خریدم را یک‌جا ببینم` | `مشتری نمونه`, no sub | **DIFFERS → fixed** |
| 5 | Role 3 label `هر دو`, sub `هم فروشگاه دارم، هم مشتری هستم` | `فروشگاه و مشتری نمونه`, no sub | **DIFFERS → fixed** |
| 6 | Footer `هر وقت بخواهید می‌توانید از بخش «نقش‌ها» عوض کنید.` | absent | **DIFFERS → fixed** |
| 7 | Background `#F5E9C8`, cards `#fff` radius 16, chevron `‹` per row | `PageStyle` + one `CardStyle`, no chevrons | **DIFFERS — not fixed**, see note A |
| 8 | Demo-profile banner | present (`DemoBannerStyle`) | **DIFFERS (extra)** — deliberate, see note B |

**Note A — why the card layout was not rebuilt.** The board draws three separate white cards each with a `‹` chevron; the built page is one card holding three buttons. Rebuilding to per-role cards is a layout change that also changes focus order and the accessibility tree on Android, so it belongs with the C-97 phone work (Stage 9) where the same three rows reappear at 390 px. Copy was fixed now because it is the part a Persian reader sees as "wrong"; layout is tracked, not silently dropped.

**Note B — the extra demo banner is kept on purpose.** The board is the production screen; the app currently runs the local demo profile, and the banner is what tells the owner the data is not real. Removing it to match the board exactly would be a correctness regression.

**Why the role names lost the word `نمونه`.** The board's labels are the product's real role names. The demo nature is already stated by the banner (note B), so `فروشگاه نمونه` was saying it twice and disagreeing with the board.

---

## Screen 2 — First-run Setup ↔ `Setup.dc.html`

| # | Board | Built screen | Status |
|---|---|---|---|
| 1 | `به کندو خوش آمدید` + sub `راه‌اندازی فروشگاه در سه قدم` | `راه‌اندازی اولیه کندو` + sub `در اولین اجرا، فروشگاه خود را تعریف کرده و برنامه را آماده کنید.` | **DIFFERS** — same meaning, different wording; see note C |
| 2 | Three numbered pills `اطلاعات فروشگاه` / `تأیید شماره‌ی مدیر` / `پشتیبان‌گیری` | `۱. تعریف فروشگاه` / `۲. تنظیمات خودکار` / `۳. پایان و ورود` | **DIFFERS — out of scope**, note D |
| 3 | Field `نام فروشگاه` | present | **MATCHES** |
| 4 | Field `نام مدیر` | **missing** | **DIFFERS — not fixed**, note E |
| 5 | Field `شماره‌ی موبایل مدیر (کد تأیید پیامک می‌شود)` | **missing** | **DIFFERS — not fixed**, note E |
| 6 | Primary button `ارسال کد تأیید` | `شروع راه‌اندازی خودکار` | **DIFFERS — not fixed**, note E |
| 7 | Note `اولین کاربر، مدیر فروشگاه می‌شود…` | absent | **DIFFERS** — depends on note E |
| 8 | Palette `#FFF8E7` bg / `#F2A900` primary / `#2B1D0E` ink | same tokens via styles | **MATCHES** |

**Note C.** The built copy is not on any board. It is close enough that changing it now would churn strings the PO has already seen in his test builds; flagged for the PM to decide wording once, for all screens.

**Note D — the step names describe a different flow.** The board's steps are *store info → manager phone verification → backup*. The implemented flow is *store info → automatic configuration → finish*. These are genuinely different runbooks, and the implemented one is the one that runs today (it starts the local Hub and creates the owner session). Renaming the pills to match the board while the steps do something else would make the screen lie. This needs a product decision, not a label change — filed in **Q-C98-02**.

**Note E — the manager identity fields were deliberately not added.** The board's first step collects the manager's name and mobile and sends an SMS verification code. **Real SMS is out of sprint** (M-010: "SMS real" is outside DoD-A), so a `ارسال کد تأیید` button could only be a button that does nothing, or one that writes a demo line and calls it verification. Either would be inventing a security-relevant step. Added to **Q-C98-02** with options.

---

## What this round actually changed

- `src/Kandoo.App/RoleSelectionPage.xaml`: six copy fixes + the board's footer line (items 1–6 above). Pure text and one non-interactive label; no behaviour, no navigation, no data model touched.
- `scripts/Capture-WindowsScreens.ps1` + two `ci.yml` steps: a PNG per screen as an artifact, so the next verification round compares images rather than arguing about memory. Marked `continue-on-error` — a screenshot failure must not fail the build, but a missing artifact is visible in the run.

## What it did not change, and why

Setup screen copy, step names, and the manager identity fields — see notes C, D, E. All three are product decisions; the lane's job at this stage is to surface the mismatch with a reason, which is what this table is.

## Honest status

Group 1 is **PARTIAL**, not green: Role selection now matches its board on every text item; Setup does not, because matching it requires the SMS verification step that this sprint excludes. `docs/questions/Q-C98-02.md` carries the decision.
