# H2Sharp

Copyright 2026 H2Sharp contributors.

## Attribution

H2Sharp is a port of H2 Database.

> Copyright 2004-2026 H2 Group.
>
> Licensed under the Mozilla Public License, Version 2.0 (the "License");
> you may not use this file except in compliance with the License.
> You may obtain a copy of the License at <https://mozilla.org/MPL/2.0/>.
> SPDX-License-Identifier: MPL-2.0

> Copyright 2004-2026 H2 Group.
>
> Licensed under the Eclipse Public License, Version 1.0 (the "License");
> you may not use this file except in compliance with the License.
> You may obtain a copy of the License at <http://www.eclipse.org/legal/epl-v10.html>.
> SPDX-License-Identifier: EPL-1.0

The full license text as shipped with H2 is in [LICENSE-H2.txt](LICENSE-H2.txt).

## Modifications

All source files under `src/` are modified versions of the corresponding files from
`h2/src/main/org/h2/` in the H2 source tree, or new files written for this port. Ported files
carry a header naming the original copyright holder and stating that the file was modified.

The exact H2 revision this port tracks is recorded in `reference/H2_COMMIT.txt` and in
`src/H2Sharp/H2Version.cs`.

## File-level copyleft

Files ported from H2 remain under MPL-2.0 OR EPL-1.0. If you modify a ported file, you must
make your modified version of that file available under the same license.

## The strong name key

`H2Sharp.snk` in the repository root is the key this project signs its assemblies with, and it
is committed on purpose. A strong name is not a security feature -- it identifies a build, and
anyone can read the public key out of any assembly that carries it -- so the private key is not
a secret in the sense that would make committing it a leak.

It is committed because the key has to be the *same* key everywhere: the Entity Framework Core
provider is a separate repository, it is signed with a key of its own, and the engine opens
its internals to it by naming that key. A key that lived only in one place, or one that each
contributor replaced with their own, would break that arrangement: the friend assembly's
public key would stop matching the name in the InternalsVisibleTo declaration, and the access
would stop working.

## No warranty

EXCEPT AS SET OUT IN THE LICENSE, THIS SOFTWARE IS PROVIDED ON AN "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED.