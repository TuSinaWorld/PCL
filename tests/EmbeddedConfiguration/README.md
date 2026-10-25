# Embedded client configuration checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/EmbeddedConfiguration/Run.ps1` on Windows.

The harness compiles the actual `ModRecoveredConfig.vb` with Framework vbc and synthetic manifest resources named `PCL.EmbeddedClientConfig.json`. Only a generated copy lowers `NameOf` for VB 2012. It loads 27 independent module assemblies from a separate test assembly carrying a decoy resource, which verifies that resource lookup uses the production module's assembly.

Coverage includes environment > local JSON > embedded JSON > caller default, present empty environment variables, local empty/null disabling, absent/empty/null embedded fields, absent/wrong-name/corrupt/oversize resources, unknown and duplicate fields, invalid types and UTF-8, and rejection of embedded CDN or arbitrary configuration. Invalid embedded resources are rejected in full. Trace assertions verify that errors contain exception types without fixture credential values.

The child process clears test-relevant inherited environment variables without reading them and explicitly selects generated local paths. It never opens the actual local credential file, runs the launcher, or accesses the network. Outputs remain under ignored `bin/`; this is isolated production-module validation rather than a full application build.
