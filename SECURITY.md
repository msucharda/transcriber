# Security policy

## Reporting a vulnerability

Please do not post credentials, recordings, transcripts, or exploit details in a
public issue. Use **Security > Report a vulnerability** on
[this repository](https://github.com/msucharda/transcriber/security) for private
reporting. If that option is unavailable, open an issue requesting a private
reporting channel without including sensitive details.

Include the affected version, Windows version, reproduction steps using
synthetic text/audio, impact, and any proposed fix. There is no guaranteed
response time or security SLA.

## Supported versions

Security fixes target the latest published release. Older binaries are not
automatically updated; download a new release when a fix is available.
Self-contained EXEs include a .NET runtime, so updating a separately installed
.NET runtime does not patch an older Tiny Transcriber EXE.

## Security boundaries and limitations

- Dictation is cloud-based, not offline. Your configured Azure Speech service
  receives the recording. Use only an endpoint you own or are authorized to use.
- Microsoft Entra ID is the recommended authentication method. Deployment
  templates disable local/API-key authentication and assign an account-scoped
  role. Never commit credentials or include them in release assets.
- The temporary WAV is not application-encrypted. Normal completion and handled
  failures clean it up; a crash, forced termination, or disk/access error can
  leave recordings behind. See [privacy guidance](docs/privacy.md).
- Transcripts go through the Windows clipboard. Other software, clipboard
  history, and clipboard sync may access or retain them.
- Automatic paste checks the target HWND and foreground focus, but it cannot
  authenticate a browser tab, document, or text field. A narrow focus race is
  still possible. Stay in the intended field until completion and check the
  result before sending or executing it.
- Neither speech recognition nor a security review guarantees correctness or
  confidentiality in every environment. Do not use this experimental tool to
  dictate passwords or other secrets.
- Releases are currently unsigned. Checksums detect differing bytes; they are
  not an Authenticode signature or independent proof of publisher identity.
  Do not bypass security software or organizational restrictions to run it.

## Release safeguards

The project uses locked NuGet dependencies, vulnerability auditing of transitive
packages, pinned GitHub Actions, isolated Windows builds, tests, and SHA-256
release checksums. Publishing should stop when these checks fail. These controls
reduce risk; they do not establish that an executable is free of vulnerabilities.
