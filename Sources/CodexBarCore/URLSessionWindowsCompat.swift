#if os(Windows)
import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

// swift-corelibs-foundation on Windows does not yet provide the async
// `URLSession.data(for:)` (it is gated behind Darwin Security auth-challenge
// APIs). Provide it via the cross-platform completion-handler `dataTask`, so
// the shared `ProviderHTTPTransport` conformance and all provider fetchers work
// unchanged on Windows.
extension URLSession {
    public func data(for request: URLRequest) async throws -> (Data, URLResponse) {
        try await withCheckedThrowingContinuation { continuation in
            let task = self.dataTask(with: request) { data, response, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if let data, let response {
                    continuation.resume(returning: (data, response))
                } else {
                    continuation.resume(throwing: URLError(.badServerResponse))
                }
            }
            task.resume()
        }
    }
}
#endif
