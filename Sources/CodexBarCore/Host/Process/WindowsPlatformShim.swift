#if os(Windows)
import Foundation

// Minimal POSIX-compatibility shims for Windows.
//
// CodexBar's process layer was written against POSIX. On Windows most of that
// layer is either reimplemented on top of Foundation's `Process` or gated out
// behind `#if !os(Windows)`. These typealiases/constants exist only so the
// shared, cross-platform parts (types in signatures, signal-number arguments)
// continue to compile. They carry POSIX semantics in name only — actual
// process control on Windows goes through Foundation, not these values.

/// POSIX process-id type. Foundation's `Process.processIdentifier` is `Int32`.
package typealias pid_t = Int32

/// Signal numbers referenced by shared termination code. On Windows these are
/// never delivered as POSIX signals; they are mapped to Foundation process
/// control (`terminate()`) by the Windows code paths.
let SIGTERM: Int32 = 15
let SIGKILL: Int32 = 9
let SIGINT: Int32 = 2
#endif
