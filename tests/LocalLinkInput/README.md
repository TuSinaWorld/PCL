# Local link input checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tests/LocalLinkInput/Run.ps1
```

This standalone suite extracts the complete production `MyMsgBoxInput`, `MyMsgBoxConverter`, input constructor, and confirm/cancel handlers on every run. It compiles those blocks with the unchanged production invite codec and Framework VB compiler. The generated copy lowers only `NameOf` and one string interpolation for VB 2012, and retains production implicit conversions with `Option Strict Off`.

An actual WPF `TextBox` receives routed `TextInput` events without opening a window. This verifies that legal PCLF1 invitations longer than 1000 characters survive the explicit invitation limit, while ordinary inputs still enforce 1000. Default values, cancellation, validation guards, argument bounds, and ordinary/sensitive return logging are also checked. Direct `.Text` assignment alone cannot prove `MaxLength`, because WPF does not limit programmatically assigned text.

UI creation, validation policy, close animations, and log sinks are fixture substitutes. The full dialog function still runs WPF modal/dispatcher bookkeeping; no launcher, EasyTier process, network, clipboard, or private configuration is used. Generated source, compile provenance, and named assertion results are written under ignored `bin/`.
