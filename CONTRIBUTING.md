# Contributing to Wida

Wida is an open-source project under the [MIT License](LICENSE). Contributions to code, documentation, translations, accessibility, and reproducible bug reports are welcome.

## Set up a checkout

Fork or clone this repository and follow the [API setup guide](Wida.Api/README.md). Connected use needs PostgreSQL and Google configuration; automatic extraction also needs RabbitMQ and Azure. The [companion repository](https://github.com/samankassou/wida-front) is maintained separately; it is not a subdirectory of this Git checkout. Use your own service accounts and local secrets for connected development.

Run commands from this repository's root unless a guide explicitly names another directory. Keep changes and commits in the repository they belong to. The current READMEs document the required runtime versions.

## Report an issue

Use this repository's Issues tab for bugs and feature requests. Include the commit or version, runtime/browser versions, demo or connected mode, steps to reproduce, expected behavior, and actual behavior. Use a minimal fictional invoice when a file is needed. Remove session cookies, tokens, passwords, real invoices, and personal data from logs and screenshots.

For a security-sensitive report, use GitHub's **Report a vulnerability** option in the Security tab if enabled. Do not post exploit details or sensitive data in a public issue; if private reporting is unavailable, ask for a private reporting channel without disclosing the vulnerability. No private reporting channel or response-time commitment is assumed by this document.

## Submit a pull request

For a substantial feature or breaking change, explain the proposed scope in an issue first so maintainers can discuss it. Keep a pull request focused and describe the problem, resulting behavior, and validation performed. Include screenshots for visible interface changes and document any migration or configuration changes.

Use existing project conventions. Update affected documentation and meaningful tests when behavior changes. Do not include generated build output, uploaded originals, credentials, or unrelated formatting changes. Use fictional fixtures and retain license notices.

For application changes, run:

```sh
dotnet restore Wida.slnx
dotnet build Wida.slnx
dotnet test Wida.slnx
```

Record failures and unavailable integrations honestly. Automated tests are not evidence of a live Google/Azure workflow. For documentation-only changes, check links, commands, and consistency with the current source; a full application build is unnecessary.

Contributions are submitted under the project's MIT License. Be respectful, provide actionable feedback, and avoid including other people's private information.
