# C-30 Avalonia throwaway spike

This is disposable C-30 evidence, not the real UI. One shared Avalonia `MainView` supplies the RTL Persian screen to a Windows Desktop host and an Android host. The Desktop host's `--probe` path executes the same `HubProbe.cs` used by the screen against the real Hub `/openapi/v1.json`. CI builds both targets but does not claim an Android emulator, visual accessibility audit, or production packaging result.
