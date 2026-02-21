# Security To-Do List

The following items were identified as areas for improvement during the security audit:

## 1. Plaintext Token Storage Fallbacks
Currently, some configurations are read from and stored in plaintext JSON files:
- `~/.config/token-budget/copilot.json`
- `~/.local/share/opencode/auth.json`

**Action Item:** 
- [ ] Migrate credential storage to use `Windows.Security.Credentials.PasswordVault` or `DPAPI` for encrypting locally stored secrets. This is especially important for native Windows applications to fortify local key protection.
- [ ] Update `CopilotCredentialReader` and `CredentialReader` to interface with the `PasswordVault`.

## 2. Debug Logging Review
A few classes, such as `OAuthCredentialReader` and `HttpGateway`, use `System.Diagnostics.Debug.WriteLine(ex.Message)` for error handling.
**Action Item:**
- [ ] Ensure that any future network or file parsing error handling does not inadvertently print raw server responses or file contents that might reflect bearer tokens or other sensitive data. Standardize error logging to sanitize outputs.

## 3. Implicit Trust in HttpClient
`HttpGateway` uses the default `HttpClient` which enforces the OS certificate trust store.
**Action Item:**
- [ ] Investigate and potentially implement Certificate Pinning for high-security endpoints like `api.anthropic.com` and `api.z.ai` to prevent Man-in-the-Middle (MITM) attacks, adding an extra layer of security for the desktop widget.
