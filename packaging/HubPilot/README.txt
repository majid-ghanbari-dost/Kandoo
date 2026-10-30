Kandoo Hub C-22 Windows pilot
=============================

This folder is self-contained: install no .NET runtime. Kandoo.Capture.dll is
included beside Kandoo.Hub.exe.

First run
---------
1. Set Hub__OwnerBootstrapProof to an owner-only bootstrap secret in the local
   process environment. Do not write secrets into this package or the repo.
2. Run Start-KandooHub.ps1. The existing console and loopback pairing page
   provide the explicitly issued first-run pairing grant.
3. Keep Hub__LocalShutdownEnabled=false outside test environments.

Data, logs, and backups
-----------------------
The launcher and appsettings.json place hub.db, logs, captures, and scheduled
SQLite backups under C:\ProgramData\Kandoo, never in this program folder.
Backups use SQLite's online backup API, an integrity check, atomic rename, and
bounded retention. Replacing this entire program folder does not remove or
change that external data directory.

Extractor boundary
------------------
Hub:PythonExtractorPath is an empty configuration location only. No extractor
executable or interface is included before the separately gated C-21 task.
