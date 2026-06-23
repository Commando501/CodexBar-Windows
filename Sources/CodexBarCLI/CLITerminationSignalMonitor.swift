import CodexBarCore
import Dispatch
import Foundation
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#elseif canImport(Musl)
import Musl
#endif

#if os(Windows)
// POSIX signal monitoring (DispatchSourceSignal + sigaction) is unavailable on
// Windows. A no-op stub keeps the CLI building; Ctrl+C handling via
// SetConsoleCtrlHandler is a future enhancement.
final class CLITerminationSignalMonitor: @unchecked Sendable {
    static let signalNumbers: [Int32] = []

    init(onSignal: @escaping @Sendable (Int32) -> Void) { _ = onSignal }

    func cancel() {}

    static func terminateActiveHelpersAndReraise(_ signalNumber: Int32) {
        TTYCommandRunner.terminateActiveProcessesForAppShutdown()
        _ = signalNumber
    }
}
#else

private func handleCLITerminationSignal(_: Int32) {}

final class CLITerminationSignalMonitor: @unchecked Sendable {
    static let signalNumbers = [SIGINT, SIGTERM, SIGHUP]

    private let lock = NSLock()
    private let sources: [DispatchSourceSignal]
    private var isCancelled = false

    init(onSignal: @escaping @Sendable (Int32) -> Void) {
        self.sources = Self.signalNumbers.map { signalNumber in
            Self.installCaptureHandler(for: signalNumber)
            let source = DispatchSource.makeSignalSource(signal: signalNumber, queue: .global(qos: .utility))
            source.setEventHandler {
                onSignal(signalNumber)
            }
            source.resume()
            return source
        }
    }

    func cancel() {
        self.lock.lock()
        guard !self.isCancelled else {
            self.lock.unlock()
            return
        }
        self.isCancelled = true
        self.lock.unlock()

        for source in self.sources {
            source.cancel()
        }
        for signalNumber in Self.signalNumbers {
            Self.restoreDefaultHandler(for: signalNumber)
        }
    }

    deinit {
        self.cancel()
    }

    static func terminateActiveHelpersAndReraise(_ signalNumber: Int32) {
        TTYCommandRunner.terminateActiveProcessesForAppShutdown()
        self.restoreDefaultHandler(for: signalNumber)
        _ = kill(getpid(), signalNumber)
    }

    private static func installCaptureHandler(for signalNumber: Int32) {
        #if canImport(Darwin)
        _ = Darwin.signal(signalNumber, handleCLITerminationSignal)
        #elseif canImport(Glibc)
        _ = Glibc.signal(signalNumber, handleCLITerminationSignal)
        #elseif canImport(Musl)
        _ = Musl.signal(signalNumber, handleCLITerminationSignal)
        #endif
    }

    private static func restoreDefaultHandler(for signalNumber: Int32) {
        #if canImport(Darwin)
        _ = Darwin.signal(signalNumber, SIG_DFL)
        #elseif canImport(Glibc)
        _ = Glibc.signal(signalNumber, SIG_DFL)
        #elseif canImport(Musl)
        _ = Musl.signal(signalNumber, SIG_DFL)
        #endif
    }
}
#endif
