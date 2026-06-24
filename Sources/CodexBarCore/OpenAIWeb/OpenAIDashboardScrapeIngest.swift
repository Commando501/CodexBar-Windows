import Foundation

/// The raw result object produced by `openAIDashboardScrapeScript` when it runs in a
/// browser (WKWebView on macOS, WebView2 in the Windows tray). Decoded from the JSON
/// the script returns so the same payload can be parsed off-platform.
public struct OpenAIDashboardScrapePayload: Decodable, Sendable {
    public var loginRequired: Bool?
    public var workspacePicker: Bool?
    public var cloudflareInterstitial: Bool?
    public var href: String?
    public var bodyText: String?
    public var signedInEmail: String?
    public var authStatus: String?
    public var accountPlan: String?
    public var creditsPurchaseURL: String?
    public var rows: [[String]]?
    public var usageBreakdownJSON: String?
    public var creditsHeaderPresent: Bool?

    public init(
        loginRequired: Bool? = nil,
        workspacePicker: Bool? = nil,
        cloudflareInterstitial: Bool? = nil,
        href: String? = nil,
        bodyText: String? = nil,
        signedInEmail: String? = nil,
        authStatus: String? = nil,
        accountPlan: String? = nil,
        creditsPurchaseURL: String? = nil,
        rows: [[String]]? = nil,
        usageBreakdownJSON: String? = nil,
        creditsHeaderPresent: Bool? = nil)
    {
        self.loginRequired = loginRequired
        self.workspacePicker = workspacePicker
        self.cloudflareInterstitial = cloudflareInterstitial
        self.href = href
        self.bodyText = bodyText
        self.signedInEmail = signedInEmail
        self.authStatus = authStatus
        self.accountPlan = accountPlan
        self.creditsPurchaseURL = creditsPurchaseURL
        self.rows = rows
        self.usageBreakdownJSON = usageBreakdownJSON
        self.creditsHeaderPresent = creditsHeaderPresent
    }
}

/// Builds an `OpenAIDashboardSnapshot` from a scrape payload using the shared
/// `OpenAIDashboardParser`. This is the platform-agnostic half of the OpenAI web
/// dashboard fetch: the macOS app drives a WKWebView and parses inline, while on
/// Windows the .NET tray drives WebView2 and pipes the payload to the engine, which
/// calls this. Parsing logic therefore lives in one place (the parser), never
/// duplicated in C#.
public enum OpenAIDashboardScrapeIngest {
    public enum IngestError: LocalizedError, Equatable {
        case loginRequired
        case cloudflareInterstitial
        case workspacePicker
        case noUsableData

        public var errorDescription: String? {
            switch self {
            case .loginRequired:
                "OpenAI dashboard requires sign-in (no authenticated session in the scrape)."
            case .cloudflareInterstitial:
                "OpenAI dashboard returned a Cloudflare interstitial; retry after it clears."
            case .workspacePicker:
                "OpenAI dashboard is showing the workspace picker; select a workspace and retry."
            case .noUsableData:
                "OpenAI dashboard scrape contained no usable usage data."
            }
        }
    }

    /// Parses a scrape payload into a snapshot, mirroring the macOS fetcher's
    /// `parseDashboardScrape` + `makeDashboardSnapshot` (without the JSON-API merge,
    /// which only the in-app fetcher performs). Throws on blocking states
    /// (login/cloudflare/workspace) and when nothing usable was scraped.
    public static func snapshot(
        from payload: OpenAIDashboardScrapePayload,
        now: Date = Date()) throws -> OpenAIDashboardSnapshot
    {
        // A logged-out SPA can render a generic shell without obvious auth inputs, so an
        // explicit non-"logged_in" authStatus is treated as login-required, matching the
        // macOS fetcher.
        let authStatus = payload.authStatus?.trimmingCharacters(in: .whitespacesAndNewlines)
        let authSaysLoggedOut = (authStatus.map { !$0.isEmpty && $0.lowercased() != "logged_in" }) ?? false
        if payload.loginRequired == true || authSaysLoggedOut {
            throw IngestError.loginRequired
        }
        if payload.cloudflareInterstitial == true {
            throw IngestError.cloudflareInterstitial
        }
        if payload.workspacePicker == true {
            throw IngestError.workspacePicker
        }

        let bodyText = payload.bodyText ?? ""
        let codeReview = OpenAIDashboardParser.parseCodeReviewRemainingPercent(bodyText: bodyText)
        let codeReviewLimit = OpenAIDashboardParser.parseCodeReviewLimit(bodyText: bodyText, now: now)
        let events = OpenAIDashboardParser.parseCreditEvents(rows: payload.rows ?? [])
        let dailyBreakdown = OpenAIDashboardSnapshot.makeDailyBreakdown(from: events, maxDays: 30)
        let usageBreakdown = Self.decodeUsageBreakdown(payload.usageBreakdownJSON)
        let rateLimits = OpenAIDashboardParser.parseRateLimits(bodyText: bodyText, now: now)
        let creditsRemaining = OpenAIDashboardParser.parseCreditsRemaining(bodyText: bodyText)
        let accountPlan = payload.accountPlan?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank

        let hasUsableData =
            creditsRemaining != nil
            || codeReview != nil
            || !events.isEmpty
            || !usageBreakdown.isEmpty
            || rateLimits.primary != nil
            || rateLimits.secondary != nil
        guard hasUsableData else { throw IngestError.noUsableData }

        return OpenAIDashboardSnapshot(
            signedInEmail: payload.signedInEmail?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            codeReviewRemainingPercent: codeReview,
            codeReviewLimit: codeReviewLimit,
            creditEvents: events,
            dailyBreakdown: dailyBreakdown,
            usageBreakdown: usageBreakdown,
            creditsPurchaseURL: payload.creditsPurchaseURL?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            primaryLimit: rateLimits.primary,
            secondaryLimit: rateLimits.secondary,
            extraRateWindows: nil,
            creditsRemaining: creditsRemaining,
            accountPlan: accountPlan,
            updatedAt: now)
    }

    private static func decodeUsageBreakdown(_ raw: String?) -> [OpenAIDashboardDailyBreakdown] {
        guard let raw, !raw.isEmpty else { return [] }
        guard let decoded = try? JSONDecoder().decode(
            [OpenAIDashboardDailyBreakdown].self,
            from: Data(raw.utf8))
        else {
            return []
        }
        return OpenAIDashboardDailyBreakdown.removingSkillUsageServices(from: decoded)
    }
}

extension String {
    fileprivate var nilIfBlank: String? {
        self.isEmpty ? nil : self
    }
}
