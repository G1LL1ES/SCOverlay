# Dependency Policy

Phase 1 intentionally uses only the .NET 8 SDK and WPF platform libraries.

Rules:

- Prefer platform APIs and small internal abstractions before adding packages.
- Add third-party packages only when they remove meaningful risk or complexity.
- Every new package must be recorded here with its purpose and license.
- Production runtime dependencies must support offline build/release workflows where practical.

Current third-party packages: none.
