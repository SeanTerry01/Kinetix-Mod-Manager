# Translating Kinetix Mod Manager

Thank you for helping make Kinetix Mod Manager usable in more languages! This folder
holds the program's translations. Each language is a single `.json` file, and you do
**not** need any programming tools or to rebuild the app — you just edit a text file.

`en.json` is the English original and the source of truth. You translate a **copy** of it.

---

## Quick start

1. **Copy `en.json`** in this `lang` folder and rename the copy to your language's
   two-letter code, all lowercase, e.g.:
   - Spanish → `es.json`
   - French → `fr.json`
   - German → `de.json`
   - Portuguese → `pt.json`
   - (The code is the ISO 639-1 two-letter language code. If unsure, ask Sean.)
2. Open your new file in a plain-text editor (Notepad, VS Code, Notepad++ …).
3. **Save it as UTF-8** so accented and non-Latin characters are preserved. In
   Notepad: *Save As* → *Encoding: UTF-8*.
4. Translate the text (see the rules below).
5. Send the finished file back to Sean, or drop it into this `lang` folder to test it
   (see "Testing your translation").

---

## The single most important rule

Every line looks like this:

```json
  "themeMgr.deleted": "Theme deleted.",
```

- The part on the **left** (`"themeMgr.deleted"`) is the **key**. It is the program's
  internal name for the line. **Never change, translate, or reorder the keys.**
- The part on the **right** (`"Theme deleted."`) is the **text the user sees and hears**.
  **This is the only thing you translate.**

So in Spanish that line becomes:

```json
  "themeMgr.deleted": "Tema eliminado.",
```

Left side untouched, right side translated. That's the whole job, line after line.

---

## Placeholders: `{0}`, `{1}`, `{2}` …

Some lines contain numbered placeholders in curly braces. The program replaces them at
runtime with real values (a mod name, a number, a keyboard shortcut, etc.).

```json
  "themeMgr.activeChanged": "Active theme changed to {0}. Press Save to confirm.",
```

Here `{0}` becomes the theme's name. Rules:

- **Keep every placeholder** exactly as written: `{0}`, `{1}`, and so on. Do not translate
  the number, do not add spaces inside the braces.
- **You may move them** to wherever your language's grammar needs them. For example, if
  your language puts the name first, you can write `"{0}: el tema activo cambió. …"`.
- **Do not invent new placeholders** and do not drop any that were in the English line.
  If the English text has `{0}` and `{1}`, your translation must also use `{0}` and `{1}`.

A few placeholders include a format like `{1:F0}` (a percentage). Keep the `:F0` part
exactly as-is — just keep the whole `{1:F0}` token intact.

---

## Things to leave in their original form

Translate the *meaning*, but keep these unchanged when they appear inside a line:

- **Product and brand names**: *Kinetix Mod Manager*, *Stardew Valley*,
  *Skyrim Special Edition*, *Fallout 4*, *Nexus Mods*, *SMAPI*, *Steam*, *GOG*, *GitHub*,
  *NVDA*, *JAWS*. (You may add your language's connecting words around them.)
- **Web addresses (URLs)** and file names like `config.json`, `d3dx9_42.dll`.
- **Placeholders** `{0}`, `{1}`, … as described above.
- **Keyboard keys** that are spoken as instructions (e.g. "Control S", "Escape",
  "F6") — translate the surrounding sentence, but keep the key names recognizable so
  users press the right keys. Use your best judgement for what blind users in your
  language will expect to hear.

You do **not** need to translate anything that isn't in `en.json`. Some on-screen lists
(category filters, the log filter, wiki categories) are deliberately kept in English
because the program matches them internally — they aren't in this file, so there's
nothing to do.

---

## Special characters

- `\n` means "new line". Keep each `\n` where it makes sense in your translation; you can
  move them to match your sentence flow, but don't delete them all or the text will run
  together.
- A real double-quote inside the text is written `\"`. Keep it written that way.
- A backslash is written `\\`. Keep both characters.

---

## The `_name` line

The very first line of the file is special:

```json
  "_name": "English",
```

Change its value to your language's name **written in that language**, so it reads
naturally in the Settings menu. For example:

```json
  "_name": "Español",
```

Lines whose key starts with an underscore (`_`) are settings for the file itself, not
user text. There is only `_name` for now — translate its value as above.

---

## Keep it natural for screen readers

This program is built for blind and low-vision users, and most of these lines are **read
aloud** by a screen reader. So:

- Translate into natural, **spoken** language — how you'd actually say it, not a stiff
  literal rendering.
- Keep it concise. Short, clear announcements are easier to listen to.
- Mind punctuation — periods and commas affect how the screen reader paces speech.

---

## Don't worry about translating everything at once

If you leave some lines untranslated, the program automatically falls back to the
English text for those lines. So a partial translation still works — you can translate
in passes and send updates over time. (If a line ever shows the raw key name like
`themeMgr.deleted` instead of real text, that means the key was accidentally changed —
restore it to match `en.json`.)

---

## Testing your translation

1. Put your file (e.g. `es.json`) in this `lang` folder, next to `en.json`.
2. Start Kinetix Mod Manager.
3. Open **Settings** (Control P), find the **Language** dropdown, choose your language,
   press **Save Settings**, then **restart** the program.
   - Alternatively, if your whole Windows display language matches, the manager will pick
     your file automatically on startup.
4. Click around and listen. Fix anything that sounds off, save the file, and restart to
   hear the changes.

---

## Valid JSON checklist

The file must stay valid JSON or it won't load (the app will quietly fall back to
English). Before sending it back:

- Every line inside the `{ … }` ends with a comma **except the very last one**.
- Every key and value is wrapped in `"double quotes"`.
- You didn't remove the opening `{` at the top or the closing `}` at the bottom.
- The file is saved as **UTF-8**.

If you'd like, paste the file into an online "JSON validator" to confirm it's clean
before sending. When it's ready, send it to Sean and it can be bundled into the next
release. Thank you again!
