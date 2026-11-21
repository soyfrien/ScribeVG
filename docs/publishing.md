# Publishing the ClickOnce install

People install ScribeVG from **https://ppdac.ltd/scribevg/**. Installed copies check
`https://ppdac.ltd/scribevg/ScribeVG.application` before each start and update when it
lists a higher version.

## How it's set up

- `ScribeVG/Properties/PublishProfiles/ClickOnce.pubxml`: self-contained win-x64, web
  install, update check before startup, unsigned manifests. The version is
  `<Version>` from the .csproj plus a revision: `0.2.0.1`, `0.2.0.2`, and so on.
- cosmic2 (nginx, `/etc/nginx/sites-enabled/000-default`, ppdac.ltd server block):
  `location ^~ /scribevg/` serves `/var/www/scribevg/` with the ClickOnce MIME types
  (`.application`, `.manifest`, `.deploy`) and `Cache-Control: no-cache`.
- `pages/index.html`: the landing page; `__VERSION__` is filled in on upload.

## Releasing an update

1. **Pick the next revision.** It must be higher than the one live now (check the
   version on https://ppdac.ltd/scribevg/). If you bump `<Version>`, you can keep counting
   the revision up; it never has to reset.
   | Date | Version |
   |------|---------|
   | 2026-11-21 | 0.2.0.1 |
2. **Build on a Windows PC with Visual Studio** (ClickOnce can't be built with
   `dotnet publish` or on Linux). From a clean copy of the source (`git archive HEAD`):

       powershell -ExecutionPolicy Bypass -File tools\publish-clickonce.ps1 -Revision 2 -Source C:\path\to\ScribeVG

   It writes `%TEMP%\scribevg-build\clickonce.tgz`.
3. **Upload** from a machine with `az login` to the cosmic subscription:

       tools/upload-clickonce.sh clickonce.tgz

   It adds the landing page, sends the build through a short-lived private blob, swaps
   `/var/www/scribevg/` on cosmic2, then deletes the blob.
4. Add a row to the table above and commit.

Only the newest version needs to be on the server; ClickOnce downloads just the files
that changed.

## Notes

- Manifests are unsigned, so Windows shows "Unknown publisher" and SmartScreen may warn.
  If you sign later, existing installs may need a one-time reinstall.
- Edge opens the `.application` link directly. Chrome and Firefox may download it;
  opening the file or running `setup.exe` works too.
- Installs are per user. Progress is in `%AppData%\ScribeVG` and survives updates and
  uninstalls.
