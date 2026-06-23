import CodexBarCore
import Commander
import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if canImport(WinSDK)
import WinSDK
#endif

// MARK: - login command options

struct LoginOptions: CommanderParsable {
    @Flag(names: [.short("v"), .long("verbose")], help: "Enable verbose logging")
    var verbose: Bool = false

    @Flag(name: .long("json-output"), help: "Emit machine-readable logs")
    var jsonOutput: Bool = false

    @Option(name: .long("log-level"), help: "Set log level (trace|verbose|debug|info|warning|error|critical)")
    var logLevel: String?

    @Option(name: .long("format"), help: "Output format: text | json")
    var format: OutputFormat?

    @Flag(name: .long("json"), help: "")
    var jsonShortcut: Bool = false

    @Flag(name: .long("json-only"), help: "Emit JSON only (suppress non-JSON output)")
    var jsonOnly: Bool = false

    @Flag(name: .long("pretty"), help: "Pretty-print JSON output")
    var pretty: Bool = false

    @Option(name: .long("provider"), help: "Provider to log in: antigravity")
    var provider: String?

    @Option(name: .long("timeout"), help: "Seconds to wait for the browser callback (default 120)")
    var timeout: Double?

    @Flag(name: .long("no-open"), help: "Do not auto-open the browser; print the URL instead")
    var noOpen: Bool = false

    @Flag(name: .long("no-enable"), help: "Save credentials without enabling the provider")
    var noEnable: Bool = false
}

private struct LoginResult: Encodable {
    let provider: String
    let email: String?
    let enabled: Bool
    let credentialsPath: String
}

// MARK: - login command

extension CodexBarCLI {
    static func runLogin(_ values: ParsedValues) async {
        let output = CLIOutputPreferences.from(values: values)

        let rawProvider = values.options["provider"]?.last?.lowercased()
        guard let rawProvider, rawProvider == "antigravity" else {
            Self.exit(
                code: .failure,
                message: "login currently supports only --provider antigravity.",
                output: output,
                kind: .args)
        }

        guard let oauthClient = AntigravityOAuthConfig.resolvedClient() else {
            Self.exit(
                code: .failure,
                message: AntigravityOAuthConfig.missingCredentialsMessage,
                output: output,
                kind: .runtime)
        }

        let timeout = values.options["timeout"]?.last.flatMap(Double.init) ?? 120
        let autoOpen = !values.flags.contains("noOpen")

        if values.flags.contains("verbose") {
            let secret = oauthClient.clientSecret
            let secretHint = secret.count > 4 ? "…\(secret.suffix(4))" : "(short)"
            FileHandle.standardError.write(Data(
                "[login] resolved OAuth client_id=\(oauthClient.clientID) secret=\(secretHint)\n".utf8))
        }

        do {
            let email = try await Self.runAntigravityLogin(
                oauthClient: oauthClient,
                timeout: timeout,
                autoOpen: autoOpen,
                output: output)

            let enableProvider = !values.flags.contains("noEnable")
            var enabled = false
            if enableProvider {
                enabled = Self.enableAntigravityProvider(output: output)
            }

            let result = LoginResult(
                provider: "antigravity",
                email: email,
                enabled: enabled,
                credentialsPath: AntigravityOAuthCredentialsStore.defaultURL().path)

            switch output.format {
            case .text:
                let who = email.map { " as \($0)" } ?? ""
                print("Antigravity login succeeded\(who).")
                print("Credentials saved to \(result.credentialsPath).")
                if enabled {
                    print("Antigravity usage is now enabled.")
                } else {
                    print("Run `codexbar config enable --provider antigravity` to show it.")
                }
            case .json:
                Self.printJSON(result, pretty: output.pretty)
            }
            Self.exit(code: .success, output: output, kind: .runtime)
        } catch {
            Self.exit(code: .failure, message: error.localizedDescription, output: output, kind: .runtime)
        }
    }

    /// Runs the loopback authorization-code flow and persists the resulting
    /// credentials. Returns the signed-in account email when available.
    private static func runAntigravityLogin(
        oauthClient: AntigravityOAuthClient,
        timeout: TimeInterval,
        autoOpen: Bool,
        output: CLIOutputPreferences) async throws -> String?
    {
        let state = UUID().uuidString.replacingOccurrences(of: "-", with: "")
        let box = AntigravityLoginCallbackBox(expectedState: state)
        let server = CLILocalHTTPServer(host: "127.0.0.1", port: 0) { request in
            box.handle(request)
        }

        let serverTask = Task {
            do {
                try await server.run(onListening: { port in box.setPort(port) })
            } catch {
                box.failStartup()
            }
        }
        defer {
            server.stop()
            serverTask.cancel()
        }

        guard let port = await box.awaitPort() else {
            throw AntigravityOAuthLogin.LoginError.tokenExchangeFailed(
                "Could not start the local login callback server.")
        }

        guard let redirectURL = URL(string: "http://127.0.0.1:\(port)/callback") else {
            throw AntigravityOAuthLogin.LoginError.invalidAuthorizationURL
        }
        let authURL = try AntigravityOAuthLogin.makeAuthorizationURL(
            redirectURL: redirectURL,
            state: state,
            oauthClient: oauthClient)

        if output.format == .text {
            if autoOpen, CLIBrowserOpener.open(authURL) {
                print("Opened your browser to sign in to Google for Antigravity…")
            } else {
                print("Open this URL in your browser to sign in to Google for Antigravity:")
            }
            print(authURL.absoluteString)
        }

        let callback = await box.awaitCallback(timeoutSeconds: timeout)
        server.stop()

        if let errorCode = callback.error {
            if errorCode == "access_denied" {
                throw AntigravityOAuthLogin.LoginError.tokenExchangeFailed("Login was cancelled.")
            }
            if errorCode == "timed_out" {
                throw AntigravityOAuthLogin.LoginError.tokenExchangeFailed("Antigravity login timed out.")
            }
            throw AntigravityOAuthLogin.LoginError.tokenExchangeFailed("Google login error: \(errorCode)")
        }
        guard let code = callback.code, !code.isEmpty else {
            throw AntigravityOAuthLogin.LoginError.tokenExchangeFailed(
                "Google login did not return an authorization code.")
        }

        let tokenResponse = try await AntigravityOAuthLogin.exchangeCodeForTokens(
            code: code,
            redirectURL: redirectURL,
            oauthClient: oauthClient)
        let email = await AntigravityOAuthLogin.fetchUserEmail(accessToken: tokenResponse.accessToken)
        let credentials = AntigravityOAuthLogin.makeCredentials(
            tokenResponse: tokenResponse,
            email: email,
            oauthClient: oauthClient)
        try AntigravityOAuthCredentialsStore().save(credentials)
        return email
    }

    private static func enableAntigravityProvider(output: CLIOutputPreferences) -> Bool {
        guard let provider = ProviderDescriptorRegistry.cliNameMap["antigravity"] else { return false }
        let store = CodexBarConfigStore()
        let config = Self.loadConfig(output: output)
        let updated = Self.configSettingProviderEnabled(config, provider: provider, enabled: true)
        do {
            try store.save(updated)
            return true
        } catch {
            return false
        }
    }
}

// MARK: - Loopback callback coordination

/// Thread-safe rendezvous between the loopback HTTP handler and the login flow.
/// Bridges the synchronous `onListening` / request callbacks of
/// ``CLILocalHTTPServer`` to the async login driver.
final class AntigravityLoginCallbackBox: @unchecked Sendable {
    struct Callback {
        let code: String?
        let state: String?
        let error: String?
    }

    private let expectedState: String
    private let lock = NSLock()

    private var resolvedPort: UInt16??
    private var portContinuation: CheckedContinuation<UInt16?, Never>?

    private var resolvedCallback: Callback?
    private var callbackContinuation: CheckedContinuation<Callback, Never>?

    init(expectedState: String) {
        self.expectedState = expectedState
    }

    // MARK: Port readiness

    func setPort(_ port: UInt16) {
        self.fulfillPort(.some(port))
    }

    func failStartup() {
        self.fulfillPort(Optional<UInt16>.none)
    }

    private func fulfillPort(_ value: UInt16?) {
        self.lock.lock()
        if self.resolvedPort != nil {
            self.lock.unlock()
            return
        }
        self.resolvedPort = .some(value)
        let continuation = self.portContinuation
        self.portContinuation = nil
        self.lock.unlock()
        continuation?.resume(returning: value)
    }

    func awaitPort() async -> UInt16? {
        await withCheckedContinuation { (continuation: CheckedContinuation<UInt16?, Never>) in
            self.lock.lock()
            if let resolved = self.resolvedPort {
                self.lock.unlock()
                continuation.resume(returning: resolved)
                return
            }
            self.portContinuation = continuation
            self.lock.unlock()
        }
    }

    // MARK: Callback delivery

    func deliver(_ callback: Callback) {
        self.lock.lock()
        if self.resolvedCallback != nil {
            self.lock.unlock()
            return
        }
        self.resolvedCallback = callback
        let continuation = self.callbackContinuation
        self.callbackContinuation = nil
        self.lock.unlock()
        continuation?.resume(returning: callback)
    }

    func awaitCallback(timeoutSeconds: TimeInterval) async -> Callback {
        let timeoutTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(max(1, timeoutSeconds) * 1_000_000_000))
            self?.deliver(Callback(code: nil, state: nil, error: "timed_out"))
        }
        defer { timeoutTask.cancel() }

        return await withCheckedContinuation { (continuation: CheckedContinuation<Callback, Never>) in
            self.lock.lock()
            if let resolved = self.resolvedCallback {
                self.lock.unlock()
                continuation.resume(returning: resolved)
                return
            }
            self.callbackContinuation = continuation
            self.lock.unlock()
        }
    }

    // MARK: HTTP handling

    func handle(_ request: CLILocalHTTPRequest) -> CLILocalHTTPResponse {
        guard request.path == "/callback" else {
            return Self.htmlResponse(success: false, status: .notFound)
        }

        let code = request.queryItems["code"]
        let returnedState = request.queryItems["state"]
        var error = request.queryItems["error"]
        if error == nil, let returnedState, returnedState != self.expectedState {
            error = "state_mismatch"
        }

        self.deliver(Callback(code: code, state: returnedState, error: error))

        let success = error == nil && (code?.isEmpty == false)
        return Self.htmlResponse(success: success, status: success ? .ok : .badRequest)
    }

    private static func htmlResponse(success: Bool, status: CLIHTTPStatus) -> CLILocalHTTPResponse {
        let title = success ? "Login Successful" : "Login Failed"
        let detail = success
            ? "You can close this window and return to CodexBar."
            : "You can close this window and try again."
        let html = """
        <html>
          <body style="font-family: system-ui, -apple-system, sans-serif; padding: 32px; text-align: center;">
            <h1>\(title)</h1>
            <p>\(detail)</p>
          </body>
        </html>
        """
        return CLILocalHTTPResponse(
            status: status,
            body: Data(html.utf8),
            contentType: "text/html; charset=utf-8")
    }
}

// MARK: - Browser opener

enum CLIBrowserOpener {
    /// Opens `url` in the platform default browser. Returns `false` when the
    /// launch could not be initiated (the caller still prints the URL).
    static func open(_ url: URL) -> Bool {
        #if os(Windows)
        let result = url.absoluteString.withCString(encodedAs: UTF16.self) { urlW in
            "open".withCString(encodedAs: UTF16.self) { verbW in
                ShellExecuteW(nil, verbW, urlW, nil, nil, 1 /* SW_SHOWNORMAL */)
            }
        }
        return unsafeBitCast(result, to: Int.self) > 32
        #elseif os(macOS)
        return Self.launch(executable: "/usr/bin/open", arguments: [url.absoluteString])
        #else
        return Self.launch(executable: "/usr/bin/xdg-open", arguments: [url.absoluteString])
        #endif
    }

    #if !os(Windows)
    private static func launch(executable: String, arguments: [String]) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: executable) else { return false }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        do {
            try process.run()
            return true
        } catch {
            return false
        }
    }
    #endif
}
