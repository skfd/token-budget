# Code Signing — GitHub Secrets Reference

## What the secrets are

Two GitHub Actions secrets power MSIX code signing in the `release.yml` workflow.

| Secret | What it holds |
|---|---|
| `SIGNING_CERT_PFX` | Base64-encoded PFX file (certificate + private key) |
| `SIGNING_CERT_PASSWORD` | Password that decrypts the PFX |

During a release run, the workflow decodes `SIGNING_CERT_PFX` back to a `.pfx` file, imports it using `SIGNING_CERT_PASSWORD`, and passes it to MSBuild for signing. It also exports the public-only `.cer` file so installers can trust the package.

## Why both are needed

A PFX bundles the certificate (public) and the private key together, protected by a password. MSBuild needs both to sign the MSIX. Without the password the PFX is useless; without the PFX there is nothing to sign with.

## Backing up the secret values

**GitHub secrets are write-only once set — you cannot read them back through the UI or API.** You must keep the originals yourself.

### What to back up

- The original `.pfx` file (binary)
- The password (plaintext string)

The base64 value stored in GitHub is just `[Convert]::ToBase64String(Get-Content cert.pfx -Raw -AsByteStream)` — you can always re-derive it from the `.pfx`.

### Recommended backup approach

Store both in a password manager (Bitwarden, 1Password, KeePass, etc.):

1. Open the entry for this project's signing key.
2. Add a secure file attachment for the `.pfx` file.
3. Store the password in the password field.

That's it. The password manager gives you encryption at rest, sync across devices, and a single place to look if you ever need to re-upload.

### If you need to re-upload the secrets

```powershell
# Re-encode the PFX to base64
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("path\to\cert.pfx"))

# Upload via GitHub CLI
gh secret set SIGNING_CERT_PFX  --body $b64
gh secret set SIGNING_CERT_PASSWORD --body "your-password-here"
```

## Certificate expiry

Self-signed certificates expire. Check the expiry date:

```powershell
$pass = ConvertTo-SecureString "your-password" -Force -AsPlainText
$cert = Get-PfxCertificate -FilePath "cert.pfx" -Password $pass
$cert.NotAfter
```

When it expires, generate a new self-signed cert, export it as PFX, and re-upload both secrets. Installers will also need the new `.cer` installed in Trusted People.
