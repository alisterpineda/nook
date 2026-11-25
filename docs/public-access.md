# Public access

Nook can open parts of the library to visitors with no account. Public access is off by default, and while it
is off a visitor is sent to the sign-in page. An admin turns it on, and chooses what it opens, on the "Public
access" page under "Manage".

What a visitor may do with a book is one of three levels, each giving everything the one before it gives:

| Level | What it gives |
|---|---|
| Browse | The book's details and cover, in the lists and on its own page. |
| Read | Opening the book in the reader. |
| Download | The book's original files. |

Everything is closed until it is given a level. The page has a level for the Anonymous role, one for the
Default area and one for each section:

- The Anonymous level caps every other. A section, or the Default area, set higher gets the Anonymous level.
- A section left at None stays closed, whatever the Anonymous level is. So does the Default area.
- A visitor never sees a book in a closed area: it is missing from every list and count, and its address
  answers as if the book did not exist.

Read makes copying a book harder and does not prevent it: the reader runs in the visitor's browser, so the
browser fetches the whole book.

Someone signed in is not affected: they can browse, read and download everything, as before.

## Crawlers

Nook never asks to be indexed, whether public access is on or off. Every response carries
`X-Robots-Tag: noindex, nofollow`, and the web app's page carries the same as a robots meta tag.
`/robots.txt` lets in only Googlebot, Bingbot and Yandex, so that they can fetch a page and see that it is not
to be indexed, and turns every other crawler away.

## Rate limit

A visitor's reading and downloading are limited per client address. Past the limit, requests get 429 until the
minute is over. Someone signed in is not limited.

| Setting | Environment variable | Default | What it is |
|---|---|---|---|
| `RateLimiting:Anonymous:PermitLimit` | `RateLimiting__Anonymous__PermitLimit` | `300` | How many requests for book files one client address may make in a minute with no sign-in. |
