#if os(Windows)
import Foundation

/// Encrypts the secret-bearing fields of `config.json` at rest on Windows using DPAPI.
///
/// macOS/Linux keep these fields plaintext in `config.json` (the OS Keychain only
/// protects the separate cache layer — see `KeychainCacheStore`). Windows has no
/// Keychain, so we transparently DPAPI-encrypt the secrets when the config is written
/// and decrypt them when it is read. Encrypted values carry a `dpapi:v1:` marker;
/// unmarked values are treated as plaintext, so a hand-written or pre-existing config
/// keeps working and is migrated to ciphertext on the next save.
enum WindowsConfigSecretCodec {
    static let marker = "dpapi:v1:"

    /// Encrypts a plaintext secret for storage. Returns the input unchanged when it is
    /// nil/empty, already marked, or if DPAPI fails (never blocks a save).
    static func protect(_ value: String?) -> String? {
        guard let value, !value.isEmpty, !value.hasPrefix(self.marker) else { return value }
        guard let blob = WindowsDPAPI.protect(Data(value.utf8)) else { return value }
        return self.marker + blob.base64EncodedString()
    }

    /// Decrypts a stored secret. Returns the input unchanged when it is unmarked
    /// (plaintext) or nil; returns the raw stored string if decryption fails so the
    /// value is never silently dropped.
    static func reveal(_ value: String?) -> String? {
        guard let value, value.hasPrefix(self.marker) else { return value }
        let encoded = String(value.dropFirst(self.marker.count))
        guard let blob = Data(base64Encoded: encoded),
              let plaintext = WindowsDPAPI.unprotect(blob),
              let string = String(data: plaintext, encoding: .utf8)
        else { return value }
        return string
    }
}

extension CodexBarConfig {
    /// A copy with every secret field DPAPI-encrypted, ready to write to disk.
    func protectingSecretsForStorage() -> CodexBarConfig {
        self.mapSecrets(WindowsConfigSecretCodec.protect)
    }

    /// A copy with every secret field decrypted, as loaded from disk.
    func revealingSecretsFromStorage() -> CodexBarConfig {
        self.mapSecrets(WindowsConfigSecretCodec.reveal)
    }

    private func mapSecrets(_ transform: (String?) -> String?) -> CodexBarConfig {
        var copy = self
        copy.providers = self.providers.map { provider in
            var updated = provider
            updated.apiKey = transform(provider.apiKey)
            updated.secretKey = transform(provider.secretKey)
            updated.cookieHeader = transform(provider.cookieHeader)
            if let accounts = provider.tokenAccounts {
                let mapped = accounts.accounts.map { account in
                    ProviderTokenAccount(
                        id: account.id,
                        label: account.label,
                        token: transform(account.token) ?? account.token,
                        addedAt: account.addedAt,
                        lastUsed: account.lastUsed,
                        externalIdentifier: account.externalIdentifier,
                        organizationID: account.organizationID)
                }
                updated.tokenAccounts = ProviderTokenAccountData(
                    version: accounts.version,
                    accounts: mapped,
                    activeIndex: accounts.activeIndex)
            }
            return updated
        }
        return copy
    }
}
#endif
