# Assistant rules for api-fairy

*A pet app by Astra.* Public credit is always Astra.

Browser interface prepared with Codex (GPT-6), 2026-10-08. Record the model and version when another assistant changes this repository.

## Scope

The dated browser entry is an offline API credential-metadata notebook. It stores labels and rotation notes, never API key values, and does not scan files. The existing Windows desktop implementation remains under src/. Its scanner and background checks are separate from this browser interface.

## Rules

- Keep the browser interface self-contained and dependency-free. Its dated entry is `api-fairy-20261008.html`.
- Treat user text as untrusted. Render it with textContent, validate links, and never insert record contents as HTML.
- Keep private records, health or finance entries, API secrets, credentials, environment files, logs and machine metadata outside Git. Browser exports are private user data.
- Label planned connections truthfully; do not imply an account, device, editor or service is connected without verification.
- Preserve the approved nine mood frames and the shared Cage character. Metadata is in `pet.json`; frame paths are in `sprite-20261008.json`.
- New descriptive files use lowercase names with the owner's America/Denver date immediately before the extension. Required contract files keep their standard names.
- Keep release tags immutable. Publish fixes as a new commit and tag; never force-push or retag an existing release.
- Update README limits and `docs/publications-20261008.md` when implementation or publication status changes.

## Validation

Check the actual browser script syntax and meaningful user flows after behavior changes. Keep data local, and do not launch scanners against the owner's files merely to test an interface.

Cage: https://github.com/findastra/astras-pet-apps. Repository topic: `pet-app`.
