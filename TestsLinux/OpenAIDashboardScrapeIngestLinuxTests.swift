import CodexBarCore
import Foundation
import Testing

/// Cross-platform tests for `OpenAIDashboardScrapeIngest` — the platform-agnostic half
/// of the OpenAI web dashboard fetch that the Windows tray's WebView2 scrape will feed.
/// Verifies it builds a snapshot from a scrape payload and rejects blocking states.
@Suite
struct OpenAIDashboardScrapeIngestLinuxTests {
    private static let usageBreakdownJSON =
        #"[{"day":"2026-06-23","services":[{"service":"CLI","creditsUsed":1.5}],"totalCreditsUsed":1.5}]"#

    @Test
    func validPayloadProducesSnapshot() throws {
        let payload = OpenAIDashboardScrapePayload(
            bodyText: "Account usage\nCredits remaining 42.50\n",
            signedInEmail: "user@example.com",
            authStatus: "logged_in",
            accountPlan: "Pro",
            usageBreakdownJSON: Self.usageBreakdownJSON)
        let snapshot = try OpenAIDashboardScrapeIngest.snapshot(from: payload)
        #expect(snapshot.creditsRemaining == 42.5)
        #expect(snapshot.signedInEmail == "user@example.com")
        #expect(snapshot.accountPlan == "Pro")
        #expect(snapshot.usageBreakdown.count == 1)
        #expect(snapshot.usageBreakdown.first?.day == "2026-06-23")
    }

    @Test
    func loginRequiredFlagThrows() {
        #expect(throws: OpenAIDashboardScrapeIngest.IngestError.loginRequired) {
            try OpenAIDashboardScrapeIngest.snapshot(
                from: OpenAIDashboardScrapePayload(loginRequired: true))
        }
    }

    @Test
    func loggedOutAuthStatusThrowsLoginRequired() {
        #expect(throws: OpenAIDashboardScrapeIngest.IngestError.loginRequired) {
            try OpenAIDashboardScrapeIngest.snapshot(
                from: OpenAIDashboardScrapePayload(
                    bodyText: "Credits remaining 10",
                    authStatus: "logged_out"))
        }
    }

    @Test
    func cloudflareInterstitialThrows() {
        #expect(throws: OpenAIDashboardScrapeIngest.IngestError.cloudflareInterstitial) {
            try OpenAIDashboardScrapeIngest.snapshot(
                from: OpenAIDashboardScrapePayload(
                    cloudflareInterstitial: true,
                    authStatus: "logged_in"))
        }
    }

    @Test
    func workspacePickerThrows() {
        #expect(throws: OpenAIDashboardScrapeIngest.IngestError.workspacePicker) {
            try OpenAIDashboardScrapeIngest.snapshot(
                from: OpenAIDashboardScrapePayload(
                    workspacePicker: true,
                    authStatus: "logged_in"))
        }
    }

    @Test
    func emptyPayloadThrowsNoUsableData() {
        #expect(throws: OpenAIDashboardScrapeIngest.IngestError.noUsableData) {
            try OpenAIDashboardScrapeIngest.snapshot(
                from: OpenAIDashboardScrapePayload(bodyText: "nothing here", authStatus: "logged_in"))
        }
    }
}
