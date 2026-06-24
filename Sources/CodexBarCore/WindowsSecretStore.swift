#if os(Windows)
import Foundation
import WinSDK

/// Thin wrapper over the Windows Data Protection API (DPAPI). Encrypts blobs with
/// the current user's master key (`CryptProtectData` with no `LOCAL_MACHINE` flag),
/// so ciphertext can only be decrypted by the same Windows user on the same machine.
/// This is the Windows analogue of the macOS Keychain's
/// `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` accessibility.
enum WindowsDPAPI {
    /// App-specific secondary entropy mixed into every protect/unprotect call so that
    /// another process running as the same user cannot trivially round-trip our blobs.
    private static let entropy = Array("com.steipete.codexbar.dpapi.v1".utf8)

    /// `CRYPTPROTECT_UI_FORBIDDEN` — never display a UI prompt (we run headless).
    private static let uiForbidden: DWORD = 0x1

    static func protect(_ data: Data) -> Data? {
        self.transform(data, decrypt: false)
    }

    static func unprotect(_ data: Data) -> Data? {
        self.transform(data, decrypt: true)
    }

    private static func transform(_ data: Data, decrypt: Bool) -> Data? {
        guard !data.isEmpty else { return nil }
        var input = [UInt8](data)
        var entropy = self.entropy
        return input.withUnsafeMutableBufferPointer { inBuf -> Data? in
            entropy.withUnsafeMutableBufferPointer { entBuf -> Data? in
                var inBlob = DATA_BLOB(cbData: DWORD(inBuf.count), pbData: inBuf.baseAddress)
                var entBlob = DATA_BLOB(cbData: DWORD(entBuf.count), pbData: entBuf.baseAddress)
                var outBlob = DATA_BLOB()
                let ok: Bool = if decrypt {
                    CryptUnprotectData(&inBlob, nil, &entBlob, nil, nil, self.uiForbidden, &outBlob)
                } else {
                    CryptProtectData(&inBlob, nil, &entBlob, nil, nil, self.uiForbidden, &outBlob)
                }
                guard ok, let outPtr = outBlob.pbData else { return nil }
                defer { LocalFree(UnsafeMutableRawPointer(outPtr)) }
                return Data(bytes: outPtr, count: Int(outBlob.cbData))
            }
        }
    }
}

/// DPAPI-encrypted, file-backed secret cache. Backs `KeychainCacheStore` on Windows,
/// where there is no Keychain. Each `service` is one JSON file under
/// `%LOCALAPPDATA%\CodexBar\SecretCache\`, mapping `account` -> base64 of the DPAPI
/// ciphertext for that account's plaintext value.
///
/// Concurrency: an in-process lock serializes mutations. Cross-process writes (e.g. a
/// `codexbar` CLI invocation running alongside `codexbar serve`) use atomic file
/// replacement, so a concurrent writer can clobber a sibling's update — acceptable for
/// a best-effort cache, but not a transactional store.
enum WindowsSecretStore {
    enum LoadOutcome {
        case found(Data)
        case missing
        case invalid
    }

    enum ClearOutcome {
        case removed
        case missing
        case failed
    }

    private static let log = CodexBarLog.logger(LogCategories.keychainCache)
    private static let lock = NSLock()

    static func load(service: String, account: String) -> LoadOutcome {
        self.lock.withLock {
            guard let map = self.readMap(service: service) else { return .missing }
            guard let encoded = map[account] else { return .missing }
            guard let blob = Data(base64Encoded: encoded), let plaintext = WindowsDPAPI.unprotect(blob) else {
                self.log.error("DPAPI secret cache entry could not be decrypted (\(account))")
                return .invalid
            }
            return .found(plaintext)
        }
    }

    @discardableResult
    static func store(service: String, account: String, data: Data) -> Bool {
        self.lock.withLock {
            guard let blob = WindowsDPAPI.protect(data) else {
                self.log.error("DPAPI secret cache could not encrypt (\(account))")
                return false
            }
            var map = self.readMap(service: service) ?? [:]
            map[account] = blob.base64EncodedString()
            return self.writeMap(map, service: service)
        }
    }

    static func clear(service: String, account: String) -> ClearOutcome {
        self.lock.withLock {
            guard var map = self.readMap(service: service) else { return .missing }
            guard map.removeValue(forKey: account) != nil else { return .missing }
            return self.writeMap(map, service: service) ? .removed : .failed
        }
    }

    /// Returns all stored account names for a service, or `nil` if enumeration failed.
    /// A missing file is reported as an empty list (no entries), not a failure.
    static func accounts(service: String) -> [String]? {
        self.lock.withLock {
            let url = self.fileURL(service: service)
            guard FileManager.default.fileExists(atPath: url.path) else { return [] }
            guard let map = self.readMap(service: service) else { return nil }
            return Array(map.keys)
        }
    }

    // MARK: - File backing

    private static func readMap(service: String) -> [String: String]? {
        let url = self.fileURL(service: service)
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode([String: String].self, from: data)
    }

    private static func writeMap(_ map: [String: String], service: String) -> Bool {
        let url = self.fileURL(service: service)
        do {
            try FileManager.default.createDirectory(
                at: url.deletingLastPathComponent(),
                withIntermediateDirectories: true)
            let data = try JSONEncoder().encode(map)
            try data.write(to: url, options: [.atomic])
            return true
        } catch {
            self.log.error("DPAPI secret cache write failed (\(service)): \(error)")
            return false
        }
    }

    private static func fileURL(service: String) -> URL {
        self.baseDirectory.appendingPathComponent("\(self.sanitize(service)).json")
    }

    private static var baseDirectory: URL {
        let local = ProcessInfo.processInfo.environment["LOCALAPPDATA"]
            .flatMap { $0.isEmpty ? nil : URL(fileURLWithPath: $0, isDirectory: true) }
            ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        return local.appendingPathComponent("CodexBar", isDirectory: true)
            .appendingPathComponent("SecretCache", isDirectory: true)
    }

    /// Maps a Keychain-style service name (e.g. `com.steipete.codexbar.cache`) to a safe
    /// file name, replacing any character outside `[A-Za-z0-9._-]` with `_`.
    private static func sanitize(_ service: String) -> String {
        let allowed = Set("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-")
        let mapped = service.map { allowed.contains($0) ? $0 : "_" }
        let result = String(mapped)
        return result.isEmpty ? "default" : result
    }
}
#endif
