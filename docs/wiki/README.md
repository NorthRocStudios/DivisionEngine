# Wiki source (`docs/wiki`)

These files are the **source** for the repository wiki
(`https://github.com/<owner>/DivisionEngine/wiki`). They are plain Markdown with the exact
filenames the wiki expects:

| File | Wiki page |
| --- | --- |
| `Home.md` | `Home` — the wiki landing page |
| `_Sidebar.md` | Navigation shown on every page |
| `Division-CLI.md` | `Division CLI` |
| `Division-Compute-CLI.md` | `DivisionCompute CLI` |

Rules that follow from how GitHub wikis work:

* The **filename is the page title**; `-` renders as a space. Don't rename a page casually —
  inbound links are filename-based.
* `_Sidebar.md` and `_Footer.md` are special; every other `*.md` is a page.
* Only pushes to the wiki's **default branch** go live.
* Wikilinks `[[Page Name]]` and relative links `[text](Page-Name)` both work.

## Publishing

The wiki is a **separate git repository** — it is not part of the main repo.

### First-time setup

1. Repository → **Settings → General → Features → Wikis** (tick it). Optionally tick
   *Restrict editing to collaborators only*.
2. Open the **Wiki** tab and create the home page in the browser (title `Home`, any body).
   This is required: until a first page exists, the `.wiki.git` clone URL returns
   "Repository not found".
3. Clone it next to (not inside) this repository:

```shell
git clone https://github.com/<owner>/DivisionEngine.wiki.git ../DivisionEngine.wiki
```

4. Copy the pages in and push:

```powershell
Copy-Item docs\wiki\*.md ..\DivisionEngine.wiki\
cd ..\DivisionEngine.wiki
git add -A
git commit -m "Add Division CLI documentation"
git push
```

### Updating later

```powershell
Copy-Item docs\wiki\*.md ..\DivisionEngine.wiki\
cd ..\DivisionEngine.wiki
git add -A; git commit -m "Update CLI docs"; git push
```

### Editing in the browser

Every page has an **Edit** button. Fine for typo fixes; prefer the git route for anything
larger so the change is reviewable in a diff.

## Why keep the source here

The wiki has no pull requests and is not part of the main repo's history, so:

* the content is reviewable as a diff on a normal branch,
* docs changes land in the same commit/PR as the code they describe (*"if you change a
  flag in `DivisionCLI.cs`, change `docs/wiki/Division-CLI.md`"*),
* the Markdown still renders on GitHub in the repo,
* if the wiki is ever outgrown, the same files can be served by GitHub Pages or a static
  site generator without rewriting anything.

The only cost is the copy step above. If that becomes annoying, automate it with a
workflow that pushes to the wiki using a PAT — see the notes in the repository issue or
ask for the workflow to be added. (`GITHUB_TOKEN` is scoped to the main repository, so
pushing to `*.wiki.git` needs a separate credential.)

## Style

* One `#` H1 per page, matching the page title.
* Prefer tables for command options; they survive narrow screens.
* Always show the full invocation, not just the flags.
* State the default for every option.
* Describe exit codes for anything a build script would gate on.
