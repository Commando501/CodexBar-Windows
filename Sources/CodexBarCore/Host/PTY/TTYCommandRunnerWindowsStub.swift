#if os(Windows)
import Foundation

// Windows stub for the POSIX/PTY command runner.
//
// The real `TTYCommandRunner` (Host/PTY/TTYCommandRunner.swift) is gated out on
// Windows because it relies on forkpty/termios/posix_spawn and POSIX signals.
// This stub preserves the public/internal API the rest of CodexBarCore uses so
// the engine compiles. PTY-based interactive probing (`run`) reports that it is
// unsupported on Windows; the non-PTY helpers (`which`, `enrichedEnvironment`,
// the app-shutdown registry) are implemented for real where it is cheap and
// useful. ConPTY-based process spawning is a future enhancement.

enum TTYProcessTreeTerminator {
    struct ProcessIdentity: Hashable {
        let pid: pid_t
        let startToken: UInt64
    }

    static func descendantPIDs(
        of rootPID: pid_t,
        childResolver: (pid_t) -> [pid_t] = { _ in [] }) -> [pid_t]
    {
        _ = (rootPID, childResolver)
        return []
    }

    static func currentChildPIDs(of parentPID: pid_t) -> [pid_t] {
        _ = parentPID
        return []
    }

    static func processIdentity(for pid: pid_t) -> ProcessIdentity? {
        _ = pid
        return nil
    }

    static func isCurrent(_ identity: ProcessIdentity) -> Bool {
        _ = identity
        return false
    }

    static func terminateProcessTree(
        rootPID: pid_t,
        processGroup: pid_t?,
        signal: Int32,
        knownDescendants: [pid_t] = [],
        childResolver: (pid_t) -> [pid_t] = { _ in [] },
        signalSender: (pid_t, Int32) -> Void = { _, _ in })
    {
        _ = (rootPID, processGroup, signal, knownDescendants, childResolver, signalSender)
    }
}

public struct TTYCommandRunner {
    public struct Result: Sendable {
        public let text: String
    }

    public struct Options: Sendable {
        public var rows: UInt16 = 50
        public var cols: UInt16 = 160
        public var timeout: TimeInterval = 20.0
        public var idleTimeout: TimeInterval?
        public var workingDirectory: URL?
        public var extraArgs: [String] = []
        public var baseEnvironment: [String: String]?
        public var initialDelay: TimeInterval = 0.4
        public var sendEnterEvery: TimeInterval?
        public var sendOnSubstrings: [String: String]
        public var stopOnURL: Bool
        public var stopOnSubstrings: [String]
        public var settleAfterStop: TimeInterval
        public var forceCodexStatusMode: Bool
        public var useClaudeProbeWorkingDirectory: Bool

        public init(
            rows: UInt16 = 50,
            cols: UInt16 = 160,
            timeout: TimeInterval = 20.0,
            idleTimeout: TimeInterval? = nil,
            workingDirectory: URL? = nil,
            extraArgs: [String] = [],
            baseEnvironment: [String: String]? = nil,
            initialDelay: TimeInterval = 0.4,
            sendEnterEvery: TimeInterval? = nil,
            sendOnSubstrings: [String: String] = [:],
            stopOnURL: Bool = false,
            stopOnSubstrings: [String] = [],
            settleAfterStop: TimeInterval = 0.25,
            forceCodexStatusMode: Bool = false,
            useClaudeProbeWorkingDirectory: Bool = false)
        {
            self.rows = rows
            self.cols = cols
            self.timeout = timeout
            self.idleTimeout = idleTimeout
            self.workingDirectory = workingDirectory
            self.extraArgs = extraArgs
            self.baseEnvironment = baseEnvironment
            self.initialDelay = initialDelay
            self.sendEnterEvery = sendEnterEvery
            self.sendOnSubstrings = sendOnSubstrings
            self.stopOnURL = stopOnURL
            self.stopOnSubstrings = stopOnSubstrings
            self.settleAfterStop = settleAfterStop
            self.forceCodexStatusMode = forceCodexStatusMode
            self.useClaudeProbeWorkingDirectory = useClaudeProbeWorkingDirectory
        }
    }

    public enum Error: Swift.Error, LocalizedError, Sendable {
        case binaryNotFound(String)
        case launchFailed(String)
        case timedOut

        public var errorDescription: String? {
            switch self {
            case let .binaryNotFound(bin):
                "Missing CLI '\(bin)'. Install it or add it to PATH."
            case let .launchFailed(msg): "Failed to launch process: \(msg)"
            case .timedOut: "PTY command timed out."
            }
        }
    }

    public init() {}

    public func run(
        binary: String,
        send script: String,
        options: Options = Options(),
        onURLDetected: (@Sendable () -> Void)? = nil) throws -> Result
    {
        _ = (binary, script, options, onURLDetected)
        throw Error.launchFailed("PTY-based CLI probing is not supported on Windows yet")
    }

    public static func terminateActiveProcessesForAppShutdown() {}

    static func locateBundledHelper(_ name: String) -> String? {
        _ = name
        return nil
    }

    /// Resolves a tool on `PATH` using Windows separators and executable extensions.
    public static func which(_ tool: String) -> String? {
        let fileManager = FileManager.default
        let pathExtensions = (ProcessInfo.processInfo.environment["PATHEXT"]
            ?? ".COM;.EXE;.BAT;.CMD")
            .split(separator: ";").map { String($0).lowercased() }
        let directories = (ProcessInfo.processInfo.environment["PATH"] ?? "")
            .split(separator: ";").map(String.init)
        let hasExtension = tool.contains(".")
        for directory in directories where !directory.isEmpty {
            let base = directory.hasSuffix("\\") ? String(directory.dropLast()) : directory
            let candidates: [String] = hasExtension
                ? ["\(base)\\\(tool)"]
                : [tool] + pathExtensions.map { "\(tool)\($0)" }
                    .map { "\(base)\\\($0)" }
            for candidate in candidates where fileManager.isExecutableFile(atPath: candidate) {
                return candidate
            }
        }
        return nil
    }

    public static func enrichedPath() -> String {
        PathBuilder.effectivePATH(purposes: [.tty, .nodeTooling])
    }

    static func enrichedEnvironment(
        baseEnv: [String: String] = ProcessInfo.processInfo.environment,
        loginPATH: [String]? = LoginShellPathCache.shared.current,
        home: String = NSHomeDirectory()) -> [String: String]
    {
        var env = baseEnv
        env["PATH"] = PathBuilder.effectivePATH(
            purposes: [.tty, .nodeTooling],
            env: baseEnv,
            loginPATH: loginPATH,
            home: home)
        if env["HOME"]?.isEmpty ?? true {
            env["HOME"] = home
        }
        return env
    }

    static func registerActiveProcessForAppShutdown(pid: pid_t, binary: String) -> Bool {
        _ = (pid, binary)
        return true
    }

    static func beginActiveProcessLaunchForAppShutdown() -> Bool { true }

    static func endActiveProcessLaunchForAppShutdown() {}

    static func updateActiveProcessGroupForAppShutdown(pid: pid_t, processGroup: pid_t?) {
        _ = (pid, processGroup)
    }

    static func unregisterActiveProcessForAppShutdown(pid: pid_t) {
        _ = pid
    }
}
#endif
