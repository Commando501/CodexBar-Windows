#if os(Windows)
import CodexBarCore
import Foundation
import Testing

/// Windows-only tests for DPAPI secret-at-rest encryption. They exercise the real
/// public store APIs (no test doubles), so they confirm the full encrypt-on-save /
/// decrypt-on-load round-trip through DPAPI and assert that no plaintext secret is
/// written to disk. These cover the config, token-accounts, and Antigravity credential
/// stores — including the token-accounts and Antigravity paths that have no CLI entry
/// point to exercise otherwise.
@Suite
struct WindowsSecretStorageTests {
    private static func makeTempDir() -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbar-dpapi-tests-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    @Test
    func configAPIKeyIsEncryptedAtRestAndRoundTrips() throws {
        let dir = Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("config.json")
        let store = CodexBarConfigStore(fileURL: url)

        let secret = "sk-secret-CONFIG-\(UUID().uuidString)"
        var config = CodexBarConfig.makeDefault()
        config.setProviderConfig(ProviderConfig(id: .openrouter, enabled: true, apiKey: secret))
        try store.save(config)

        let onDisk = try String(contentsOf: url, encoding: .utf8)
        #expect(!onDisk.contains(secret))
        #expect(onDisk.contains("dpapi:v1:"))

        let loaded = try store.load()
        #expect(loaded?.providerConfig(for: .openrouter)?.apiKey == secret)
    }

    @Test
    func tokenAccountTokenIsEncryptedAtRestAndRoundTrips() throws {
        let dir = Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("token-accounts.json")
        let store = FileTokenAccountStore(fileURL: url)

        let secret = "tok-secret-ACCT-\(UUID().uuidString)"
        let account = ProviderTokenAccount(
            id: UUID(),
            label: "main",
            token: secret,
            addedAt: 0,
            lastUsed: nil)
        let data = ProviderTokenAccountData(version: 1, accounts: [account], activeIndex: 0)
        try store.storeAccounts([.openai: data])

        let onDisk = try String(contentsOf: url, encoding: .utf8)
        #expect(!onDisk.contains(secret))
        #expect(onDisk.contains("dpapi:v1:"))

        let loaded = try store.loadAccounts()
        #expect(loaded[.openai]?.accounts.first?.token == secret)
        // Non-secret fields stay readable.
        #expect(loaded[.openai]?.accounts.first?.label == "main")
    }

    @Test
    func antigravityCredentialsAreEncryptedAtRestAndRoundTrip() throws {
        let dir = Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("oauth_creds.json")
        let store = AntigravityOAuthCredentialsStore(fileURL: url)

        let accessSecret = "at-secret-\(UUID().uuidString)"
        let refreshSecret = "rt-secret-\(UUID().uuidString)"
        let creds = AntigravityOAuthCredentials(
            accessToken: accessSecret,
            refreshToken: refreshSecret,
            expiryDate: nil,
            email: "user@example.com")
        try store.save(creds)

        let onDisk = try String(contentsOf: url, encoding: .utf8)
        #expect(!onDisk.contains(accessSecret))
        #expect(!onDisk.contains(refreshSecret))
        #expect(onDisk.contains("dpapi:v1:"))

        let loaded = try store.load()
        #expect(loaded?.accessToken == accessSecret)
        #expect(loaded?.refreshToken == refreshSecret)
        #expect(loaded?.email == "user@example.com")
    }

    @Test
    func plaintextConfigStillLoadsAndMigratesOnSave() throws {
        let dir = Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("config.json")

        // Write a plaintext (unmarked) config as a pre-existing/hand-written file would be.
        let secret = "sk-legacy-PLAINTEXT-\(UUID().uuidString)"
        let plaintextJSON = """
        {"version":1,"providers":[{"id":"openrouter","enabled":true,"apiKey":"\(secret)"}]}
        """
        try plaintextJSON.write(to: url, atomically: true, encoding: .utf8)

        let store = CodexBarConfigStore(fileURL: url)
        // Loads despite being unmarked plaintext.
        let loaded = try store.load()
        #expect(loaded?.providerConfig(for: .openrouter)?.apiKey == secret)

        // Saving migrates it to ciphertext.
        try store.save(loaded!)
        let onDisk = try String(contentsOf: url, encoding: .utf8)
        #expect(!onDisk.contains(secret))
        #expect(onDisk.contains("dpapi:v1:"))
    }
}
#endif
