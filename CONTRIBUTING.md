# Contributing to Pheme

Thank you for your interest. Pheme is maintained by one person, and every change goes through the maintainer's
approval.

## Open an issue first

Please open an issue before writing code: a bug report, a question, or a proposal with the use case behind it. Pull
requests without a discussed issue may be closed, and the work wasted.

The scope of the library is deliberately narrow (see "Scope" in [`docs/design.md`](docs/design.md)). A proposal
outside it, or one that serves a single application and could be an extension point (option, delegate, behavior)
implemented by that application, may be declined.

## Pull requests

Only after the maintainer agreed to the change in an issue. Only the maintainer commits to the repository and merges.

- Branch from `develop`, open the pull request against `develop`.
- `dotnet build` without warnings (warnings are errors) and `dotnet test` green. Integration tests and the example
  need Docker; `dotnet test --filter-trait "Category=Unit" --ignore-exit-code 8` runs the unit tests without it.
- A public API change is listed in the package's `PublicAPI.Unshipped.txt`; the build fails until it is.
- A public API has XML documentation. Documentation (`README.md`, [`docs/`](docs/README.md)) describes the code as it is
  after the change.
- A new test for every fix or feature; assertions with xUnit's `Assert` only.
- Code style (block-scoped namespaces, 4 spaces) is enforced by the build.

Conventions of the repository and the reasons for them are in [`docs/development.md`](docs/development.md).

## Security

Report vulnerabilities privately, see [`SECURITY.md`](SECURITY.md).
