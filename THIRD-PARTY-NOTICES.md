# Third-party notices

WinnowRSS is licensed under the MIT License (see [LICENSE](LICENSE)). It uses the following components, each under its own license.

## Shipped with the application

| Component | License | Project |
|---|---|---|
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Dapper | Apache-2.0 | https://github.com/DapperLib/Dapper |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw (bundle_e_sqlite3) | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | Public domain | https://sqlite.org/copyright.html |
| Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging.Abstractions | MIT | https://github.com/dotnet/runtime |
| System.ServiceModel.Syndication | MIT | https://github.com/dotnet/runtime |
| .NET runtime and WPF (self-contained release) | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| Microsoft.Web.WebView2 | BSD-3-Clause (text below) | https://aka.ms/webview |

The Microsoft Edge WebView2 Runtime itself is not shipped: it is part of Windows 11 and is installed separately on Windows 10.

## Used only to build and test

| Component | License | Project |
|---|---|---|
| xUnit.net | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |
| coverlet.collector | MIT | https://github.com/coverlet-coverage/coverlet |
| FlaUI | MIT | https://github.com/FlaUI/FlaUI |
| Pillow (icon generator, `tools/icon`) | MIT-CMU | https://github.com/python-pillow/Pillow |

## Not distributed

- **Ollama** and the language models it runs (for example Qwen3, Apache-2.0) are installed by the user and used over a local HTTP API.
- **Color themes** installed from Open VSX or from a file are downloaded by the user; each keeps its own license.
- Icons come from the Segoe Fluent Icons font that ships with Windows.

## Microsoft.Web.WebView2 license

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
