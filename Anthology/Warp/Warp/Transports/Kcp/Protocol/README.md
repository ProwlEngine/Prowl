# KCP protocol (vendored kcp2k)

The KCP reliability algorithm, as ported to C# by [kcp2k](https://github.com/MirrorNetworking/kcp2k),
which is in turn a port of [skywind3000/kcp](https://github.com/skywind3000/kcp).

- Upstream commit: `66efda6686f649838d42f078fbaabf56ac449de4` (2024-10-12)
- Files: `kcp2k/kcp2k/kcp/{Kcp,Segment,AckItem,Pool,Utils}.cs`
- License: MIT, see [LICENSE.txt](LICENSE.txt)

**The only change from upstream is the `namespace` line** (`kcp2k` becomes
`Prowl.Warp.Transports.Kcp.Protocol`). Don't edit anything else here: Warp's own code
(`KcpTransport`, `KcpConnection`) adapts to this API instead, so updating stays a file copy.

## Updating

From this folder, with a newer kcp2k checkout at `$KCP2K`:

```sh
for f in Kcp Segment AckItem Pool Utils; do cp "$KCP2K/kcp2k/kcp2k/kcp/$f.cs" .; done
perl -i -pe 's/^namespace kcp2k(\r?)$/namespace Prowl.Warp.Transports.Kcp.Protocol$1/' *.cs
```

Then bump the commit above. `git diff` should show nothing but the namespace lines and upstream's
own changes. The `.gitattributes` here keeps the files byte-for-byte (upstream uses CRLF), so a diff
against upstream stays clean.

Only the core algorithm is vendored. kcp2k's high-level client/server layer is not used; Warp has its
own socket, handshake and connection management in the parent folder.
