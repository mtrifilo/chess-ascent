# Third-party notices

Chess Ascent is licensed under AGPL-3.0-or-later (see `LICENSE`). It builds on the work listed here.

This file records what the project depends on directly: every engine, model, font and asset, and every library that a project file or `package.json` names. Each entry is added in the same pull request that introduces the dependency. Libraries pulled in only by other libraries are not listed here, but the same licence test applies to them: before adding a dependency, check what it brings with it. A component whose licence is not compatible with AGPL-3.0-or-later, or that forbids commercial use, is not added.

Library versions are not repeated here, because they would go stale: `backend/Directory.Packages.props` and `frontend/bun.lock` hold them. Engines, models and assets are pinned by hand, so their versions are recorded below.

## Engines and models

Run as separate programs, or loaded as data. Not linked into Chess Ascent's own code.

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| | | | | |

## Back-end libraries

| Component | Source | Licence | How it is used |
|---|---|---|---|
| AwesomeAssertions | https://github.com/AwesomeAssertions/AwesomeAssertions | Apache-2.0 | Assertions in the test projects only |
| Microsoft.AspNetCore.Mvc.Testing | https://github.com/dotnet/aspnetcore | MIT | Runs the API in memory for the API tests (`WebApplicationFactory`); test projects only |
| xunit.v3 | https://github.com/xunit/xunit | Apache-2.0 | Test framework, on Microsoft.Testing.Platform (MIT), which it brings in; test projects only |

## Front-end libraries

| Component | Source | Licence | How it is used |
|---|---|---|---|
| | | | |

## Fonts, pieces and other assets

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| | | | | |
