CLF-UoClassic EU - European Latin diacritics
============================================

A modified build of CLF-UoClassic 1.1 that renders Slovak, Czech, German,
Polish, Hungarian, Romanian and other European Latin accented letters in the
original hand-drawn UO-classic style, instead of the plain Mgen+ fallback
glyphs that shipped with the original font.

File
----
CLF-UoClassic-EU.ttf      Family name: "CLF-UoClassic EU"

How the accents are made
------------------------
Each accented letter is a composite of the UO-classic base letter plus an
accent mark built from the font's OWN hand-drawn UO symbols, so the marks
match the heavy UO weight:

  dlzen / acute      <- apostrophe '   (enlarged + slanted)
  makcen / caron     <- caret ^        (flipped vertically)
  circumflex (o^)    <- caret ^
  umlaut / diaeresis <- quotedbl "
  krouzek / ring     <- degree sign
  dot above          <- period .
  macron             <- hyphen -
  grave              <- backtick `
  cedilla / comma    <- comma ,
  double acute (o")  <- two apostrophes
  d' t' l' L'        <- apostrophe at the stem's upper right (correct SK/CZ form)

Fixes vs. the original
----------------------
- Accented letters now use the UO letterform, not the Mgen+ fallback.
- Small i and j drop their dot when an accent is added (i -> dotless base),
  e.g. i-acute, i-circumflex, j-circumflex.
- d' t' l' L' use the apostrophe-form caron, not a centred caron.
- Advance widths of accented letters equal their base letter.
- ASCII and all other glyphs are untouched.

Coverage
--------
316 accented letters across Latin-1 Supplement, Latin Extended-A/-B and Latin
Extended Additional get UO-style marks. Full coverage for SK, CZ, DE, PL, HU,
RO. A few rare marks with no matching UO stroke (breve a-breve, Vietnamese
horn, dot-below, etc.) keep the original font's glyph. Letters with no
base+accent form (ss o-slash eth thorn l-stroke d-stroke ae oe ...) are left
as the original font had them.

License
-------
Modification of CLF-UoClassic 1.1, licensed under SIL Open Font License 1.1
(see LICENSE.txt). Family name changed as required by OFL clause 3.
Original: Copyright (c) 2018 uo.clife.work (https://uo.clife.work/).
