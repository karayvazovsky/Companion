---
name: share-update
description: Share Companion's latest progress. Drafts a playful devlog, a GitHub release and social posts through the companion-herald agent, shows them to the user, and after approval pushes to GitHub and publishes the release. Use when the user says "share an update", "post our progress", "/share-update", or similar.
---

# Share a Companion update

Repository: `D:\Claude\Companion`. Run every command from there.

Publishing goes out to the public, so **nothing is pushed, tagged or released until the user approves the draft in chat.** Social posts are only ever drafted. The user posts them by hand.

## 1. Preflight

1. Run `gh auth status`. If `gh` is missing, ask the user's permission, then install it with `winget install --id GitHub.cli -e`. If it isn't logged in, ask the user to run `gh auth login --web` themselves. Never handle their GitHub password or token.
2. Run `git remote get-url origin`. If there's no remote, do the **first-time setup** at the bottom of this file, then come back.
3. Run `git status --short`. If there are uncommitted changes, show them and ask whether to commit them first or leave them out. Ignore `outbox/`, which is gitignored.
4. Find the last shared point with `git describe --tags --abbrev=0 --match "v*"`. If there are no tags, this is the first update and the range is `--root`.
5. Run `git log --oneline <lastTag>..HEAD`. If nothing is new since the last tag, tell the user there's nothing to share yet and stop.
6. Run `dotnet test src/Companion.Tests -v q`. If tests fail, show the failures and ask whether to share anyway. Don't share a red build silently.

## 2. Pick the tag

- If the commits finish a new version (a `chore: vX.Y ...` commit, or a new plan or spec in `docs/` that's now done), propose `vX.Y`.
- Otherwise propose `<lastTag>-devlog.<n>`, where n is the next free number.

Mention the proposed tag in the review step, so the user can change it there.

## 3. Draft with the herald

Spawn the `companion-herald` agent in the foreground. You need its result. Give it `RANGE`, `TAG`, `DATE` (today), and anything the user said about what to emphasise.

## 4. Review with the user

Show the user:
- the devlog entry, pasted as-is,
- the GIF, via `SendUserFile` with `display: render`,
- a one-line summary of each social draft and the path to `outbox/<DATE>/social.md`,
- anything the herald flagged as uncertain.

Then ask one question with these options: **Publish**, **Change something** (take their notes, then edit the drafts or re-run the herald), or **Cancel** (revert the DEVLOG, README and media changes with `git checkout -- DEVLOG.md README.md` and delete the new GIF).

Also ask: "Attach a ready-to-run build (zip) to the release?" The default is no.

## 5. Publish (only after the user says Publish)

```
git add DEVLOG.md README.md docs/media
git commit -m "docs: devlog <DATE> (<TAG>)"
git tag -a <TAG> -m "<TAG>: <the pet's headline line>"
git push origin HEAD:master
git push origin <TAG>
```

Fill `{{REPO_URL}}` in `outbox/<DATE>/release.md` and `social.md` with `gh repo view --json url -q .url`.

If the user wants a build attached:

```
powershell -File tools/publish.ps1
Compress-Archive -Path app, skins -DestinationPath outbox\<DATE>\Companion-<TAG>-win-x64.zip -Force
```

Then create the release. Leave out the zip if they said no:

```
gh release create <TAG> --title "<TAG> — <headline>" --notes-file outbox/<DATE>/release.md [outbox/<DATE>/Companion-<TAG>-win-x64.zip]
```

Use `--prerelease` for `-devlog.` tags.

## 6. Hand off

Tell the user:
- the release URL and the devlog URL (`<repo>/blob/master/DEVLOG.md`),
- that the social drafts are ready in `outbox/<DATE>/social.md`, with links filled in and the GIF to attach next to each one.

---

## First-time setup (no `origin` yet)

Do each step only after the user agrees to it.

1. **Email privacy.** Commits carry the author email. Offer to switch future commits to the GitHub no-reply address. Get it from `gh api user -q '"\(.id)+\(.login)@users.noreply.github.com"'`, then run `git config user.email <that>`. Also offer to rewrite the existing history to use it, with `git filter-branch --env-filter`. That rewrite can only happen before the first push.
2. **Default branch.** Fast-forward `master` to the work branch with `git branch -f master <branch>` (only if `master` is an ancestor), then `git checkout master`.
3. **Create the repo**, public, with `gh repo create Companion --public --source . --remote origin --description "A tiny pixel pet that lives on your Windows desktop and answers anything through Claude."`. Then run `git push -u origin master`.
4. Continue with the normal flow. The first update covers `--root`. Make the showreel GIF and tag the current version, for example `v1.1`.
