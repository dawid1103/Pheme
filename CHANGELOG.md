# Changelog

All notable changes to Pheme. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the
versions follow [Semantic Versioning](https://semver.org/).

## [2.1.0] - 2026-10-11

The first version on nuget.org. It contains everything of 2.0.0 and 2.0.1, which were published to GitHub Packages
only; `OutboxOptions.RetentionLogLevel` (2.0.1) is part of the public API from this version.

## [2.0.1]

### Added

- The outbox processor logs each retention cleanup, at the level `OutboxOptions.RetentionLogLevel`
  (`Information` by default).

## [2.0.0]

First release: commands and queries with ordered pipeline behaviors, request logging, FluentValidation, domain events
dispatched on `SaveChanges`, integration events with an outbox, tracing and metrics, test helpers.

[2.1.0]: https://github.com/dawid1103/Pheme/releases/tag/v2.1.0
