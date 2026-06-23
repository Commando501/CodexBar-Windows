import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Cross-platform Antigravity Google OAuth login helpers.
///
/// This holds the platform-agnostic parts of the authorization-code flow:
/// building the Google consent URL, exchanging the returned code for tokens,
/// resolving the account email, and assembling persistable credentials. The
/// loopback redirect server and browser launch are supplied by the caller
/// (the macOS app uses `NWListener` + `NSWorkspace`; the CLI uses
/// ``CLILocalHTTPServer`` + the platform browser opener), so this type stays
/// usable from any CodexBar host on macOS, Linux, and Windows.
public enum AntigravityOAuthLogin {
    public struct TokenResponse: Sendable, Equatable {
        public let accessToken: String
        public let refreshToken: String?
        public let expiresIn: Int
        public let idToken: String?

        public init(accessToken: String, refreshToken: String?, expiresIn: Int, idToken: String?) {
            self.accessToken = accessToken
            self.refreshToken = refreshToken
            self.expiresIn = expiresIn
            self.idToken = idToken
        }
    }

    public enum LoginError: LocalizedError, Sendable, Equatable {
        case invalidAuthorizationURL
        case tokenExchangeFailed(String)

        public var errorDescription: String? {
            switch self {
            case .invalidAuthorizationURL:
                "Could not build the Antigravity login URL."
            case let .tokenExchangeFailed(message):
                message
            }
        }
    }

    public typealias DataLoader = @Sendable (URLRequest) async throws -> (Data, URLResponse)

    /// Builds the Google authorization URL the user is sent to in the browser.
    public static func makeAuthorizationURL(
        redirectURL: URL,
        state: String,
        oauthClient: AntigravityOAuthClient) throws -> URL
    {
        guard var components = URLComponents(
            url: AntigravityOAuthConfig.authURL,
            resolvingAgainstBaseURL: false)
        else {
            throw LoginError.invalidAuthorizationURL
        }
        components.queryItems = [
            URLQueryItem(name: "client_id", value: oauthClient.clientID),
            URLQueryItem(name: "redirect_uri", value: redirectURL.absoluteString),
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "scope", value: AntigravityOAuthConfig.scopes.joined(separator: " ")),
            URLQueryItem(name: "access_type", value: "offline"),
            URLQueryItem(name: "prompt", value: "select_account consent"),
            URLQueryItem(name: "state", value: state),
        ]
        guard let url = components.url else {
            throw LoginError.invalidAuthorizationURL
        }
        return url
    }

    /// Exchanges the authorization code for access/refresh tokens.
    public static func exchangeCodeForTokens(
        code: String,
        redirectURL: URL,
        oauthClient: AntigravityOAuthClient,
        timeout: TimeInterval = 30,
        dataLoader: DataLoader = { try await ProviderHTTPClient.shared.data(for: $0) }) async throws
        -> TokenResponse
    {
        var request = URLRequest(url: AntigravityOAuthConfig.tokenURL)
        request.httpMethod = "POST"
        request.timeoutInterval = timeout
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.httpBody = Self.formBody([
            "code": code,
            "client_id": oauthClient.clientID,
            "client_secret": oauthClient.clientSecret,
            "redirect_uri": redirectURL.absoluteString,
            "grant_type": "authorization_code",
        ])

        let (data, response) = try await dataLoader(request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw LoginError.tokenExchangeFailed("Invalid token response.")
        }
        guard httpResponse.statusCode == 200 else {
            let message = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
                ?? "HTTP \(httpResponse.statusCode)"
            throw LoginError.tokenExchangeFailed(message)
        }

        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let accessToken = (json["access_token"] as? String)?.trimmedNonEmpty
        else {
            throw LoginError.tokenExchangeFailed("Could not decode token response.")
        }
        let expiresIn = (json["expires_in"] as? Int) ?? (json["expires_in"] as? Double).map(Int.init) ?? 3600
        return TokenResponse(
            accessToken: accessToken,
            refreshToken: (json["refresh_token"] as? String)?.trimmedNonEmpty,
            expiresIn: expiresIn,
            idToken: (json["id_token"] as? String)?.trimmedNonEmpty)
    }

    /// Resolves the signed-in account email. Best-effort: returns `nil` on failure.
    public static func fetchUserEmail(
        accessToken: String,
        timeout: TimeInterval = 15,
        dataLoader: DataLoader = { try await ProviderHTTPClient.shared.data(for: $0) }) async -> String?
    {
        var request = URLRequest(url: AntigravityOAuthConfig.userInfoURL)
        request.timeoutInterval = timeout
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")

        do {
            let (data, response) = try await dataLoader(request)
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            else {
                return nil
            }
            return (json["email"] as? String)?.trimmedNonEmpty
        } catch {
            return nil
        }
    }

    /// Assembles persistable credentials from a token exchange result. The client
    /// id/secret are stored so background refreshes do not need to re-discover them.
    public static func makeCredentials(
        tokenResponse: TokenResponse,
        email: String?,
        oauthClient: AntigravityOAuthClient,
        now: Date = Date()) -> AntigravityOAuthCredentials
    {
        AntigravityOAuthCredentials(
            accessToken: tokenResponse.accessToken,
            refreshToken: tokenResponse.refreshToken,
            expiryDate: now.addingTimeInterval(TimeInterval(tokenResponse.expiresIn)),
            idToken: tokenResponse.idToken,
            email: email,
            projectID: nil,
            clientID: oauthClient.clientID,
            clientSecret: oauthClient.clientSecret)
    }

    private static func formBody(_ values: [String: String]) -> Data? {
        values
            .map { key, value in
                let encodedKey = key.addingPercentEncoding(withAllowedCharacters: .antigravityOAuthQueryAllowed) ?? key
                let encodedValue = value
                    .addingPercentEncoding(withAllowedCharacters: .antigravityOAuthQueryAllowed) ?? value
                return "\(encodedKey)=\(encodedValue)"
            }
            .joined(separator: "&")
            .data(using: .utf8)
    }
}

extension CharacterSet {
    fileprivate static let antigravityOAuthQueryAllowed: CharacterSet = {
        var allowed = CharacterSet.urlQueryAllowed
        allowed.remove(charactersIn: "+&=")
        return allowed
    }()
}

extension String {
    fileprivate var trimmedNonEmpty: String? {
        let trimmed = self.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}
