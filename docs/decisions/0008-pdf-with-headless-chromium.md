# 0008: PDF labels and invoices: HTML printed by headless Chromium

- Status: Accepted
- Date: 2026-11-21

## Context

Sellers print shipping labels (A4 sticker sheets and 100 × 150 mm thermal) and, later,
invoices. Buyer names and addresses are often in Bangla. Bangla needs **complex-script
shaping**: conjuncts (ক্ষ, ন্ত) and vowel signs that attach before or around a consonant are
built by an OpenType shaping engine (HarfBuzz or similar). A PDF library without shaping prints
the letters in the wrong order or as separate glyphs. CLAUDE.md asks for free, open-source tools
and to avoid libraries that moved to commercial licences.

Options considered:
- **QuestPDF:** the best .NET API, and it uses HarfBuzz. But it moved to a dual licence in 2023
  (free community licence only under USD 1M revenue; not OSI open source), the kind of
  dependency we avoid.
- **PDFsharp / MigraDoc (MIT):** pure .NET and light, but no complex-script shaping, so
  Bangla breaks.
- **Browser print from the SPA:** no server dependency, but there is no PDF to send by API,
  attach or archive, and the result depends on each user's browser and printer settings.
- **HTML + CSS printed by headless Chromium via Playwright for .NET (Apache-2.0):** correct
  shaping, CSS page sizes, templates anyone can edit. It needs a browser binary on the server.

## Decision

Render HTML templates to PDF with headless Chromium through **Microsoft.Playwright**.
- One browser per API process (`ChromiumPdfRenderer`, singleton), started on first use and
  restarted if it dies. At most 2 renders at a time.
- Each document gets a fresh browser context with **JavaScript disabled and every network
  request aborted**. Templates HTML-encode every value. Seller-typed text can neither run
  script nor make the server fetch URLs (no SSRF).
- The font (**Noto Sans Bengali**, SIL OFL 1.1, Bangla + Latin) is embedded in the assembly
  and inlined as a `data:` URI, so output is the same on every machine, including font-less
  Linux containers.
- Chromium is installed with `playwright.ps1 install chromium` (README; CI adds `--with-deps`).
  A missing or crashed browser gives 503 "try again", never a 500 with details.

## Consequences

- About 150 MB of browser on each API host (and later in the Docker image, using a Playwright
  base image or an install step), and about 0.2–0.5 s per document. Fine for labels printed in
  batches of up to 100.
- If PDF volume grows, move rendering to a separate worker (Hangfire job or a sidecar), so
  Chromium's memory use never competes with API requests.
- Invoices reuse the same renderer with their own template.
