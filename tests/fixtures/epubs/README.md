# EPUB fixtures

Real EPUBs as real producers ship them, for tests that read what users actually upload. Tests of broken or unusual
books build their EPUBs in memory with `EpubBuilder` instead.

Each file is committed as downloaded and never edited, so its hash below still matches. Producers rebuild their
files from time to time, so a fresh download may differ from the copy here; replace a file only on purpose, and
update its row and the tests that read it when you do.

| File | Source | Fetched | Licence | SHA-256 |
|---|---|---|---|---|
| `standard-ebooks-a-princess-of-mars.epub` | [Standard Ebooks](https://standardebooks.org/ebooks/edgar-rice-burroughs/a-princess-of-mars), the compatible EPUB, built 2026-11-15 | 2026-11-20 | Public domain text; production dedicated to the public domain under CC0 | `bbdcd2553b9161d8d5a8d0bfc3ab568aa76e1065569f0dfb6fd5390738ca4114` |
| `gutenberg-the-yellow-wallpaper-epub2.epub` | [Project Gutenberg #1952](https://www.gutenberg.org/ebooks/1952), "EPUB (older E-readers)", via the PGLAF mirror (`gutenberg.pglaf.org/cache/epub/1952/pg1952-images.epub`), built 2026-10-01 | 2026-11-20 | Public domain in the USA; kept unmodified with the Project Gutenberg licence it carries | `bd017a85702c3d26f979491647b94fd04833a4b1cb960a142d3c572e85305a15` |

## What each one is for

**`standard-ebooks-a-princess-of-mars.epub`**: a modern EPUB 3 from a careful producer.

- A nav document with nesting (28 chapters under the half-title), and an NCX beside it.
- Cover marked with `properties="cover-image"` and named by `<meta name="cover">`.
- `file-as` refinements on the title and the author.
- Series as an EPUB 3 collection: `belongs-to-collection` "Martian", type `series`, position 1.
- Several `role` refinements on one creator (`ann`, `aut`, `dto`, `wfw`), and `dc:contributor`s with roles Nook does not keep.
- A `dc:date` that is the producer's release date (2014), not the book's first publication (1912).
- An HTML description, escaped inside `dc:description`.
- A URL as the only identifier, so none with a scheme.

**`gutenberg-the-yellow-wallpaper-epub2.epub`**: an EPUB 2 from Project Gutenberg's Ebookmaker.

- NCX table of contents only, with two entries (the story and the licence).
- Cover named by `<meta name="cover">` only, with a generated file name, and a cover page first in the spine.
- `opf:file-as` on the creator, and no role.
- One `dc:date` with `opf:event="publication"` (Gutenberg's release, 1999-11-01) and one with `opf:event="conversion"`.
- The identifier `http://www.gutenberg.org/1952` with `opf:scheme="URI"`.
- Several LCSH subjects, no description or publisher.
