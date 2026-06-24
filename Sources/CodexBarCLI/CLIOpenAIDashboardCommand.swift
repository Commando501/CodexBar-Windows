import CodexBarCore
import Commander
import Foundation

extension CodexBarCLI {
    /// Parses an OpenAI dashboard scrape payload (the JSON produced by the shared scrape
    /// script running in a browser) into a usage snapshot. The Windows tray drives a
    /// WebView2, scrapes the dashboard, and pipes the payload here so the engine's
    /// `OpenAIDashboardParser` produces the snapshot — keeping the parsing logic in one
    /// place rather than reimplementing it in C#.
    static func runOpenAIDashboardIngest(_ values: ParsedValues) {
        let output = CLIOutputPreferences.from(values: values)

        let raw: Data
        if let path = values.options["input"]?.last {
            guard let data = FileManager.default.contents(atPath: path) else {
                Self.exit(
                    code: .failure,
                    message: "Could not read input file: \(path)",
                    output: output,
                    kind: .args)
            }
            raw = data
        } else if values.flags.contains("stdin") {
            raw = FileHandle.standardInput.readDataToEndOfFile()
        } else {
            Self.exit(
                code: .failure,
                message: "Provide the scrape payload with --stdin or --input <path>.",
                output: output,
                kind: .args)
        }

        let payload: OpenAIDashboardScrapePayload
        do {
            payload = try JSONDecoder().decode(OpenAIDashboardScrapePayload.self, from: raw)
        } catch {
            Self.exit(
                code: .failure,
                message: "Invalid scrape payload JSON: \(error.localizedDescription)",
                output: output,
                kind: .args)
        }

        do {
            let snapshot = try OpenAIDashboardScrapeIngest.snapshot(from: payload)
            Self.printJSON(snapshot, pretty: output.pretty)
            Self.exit(code: .success, output: output, kind: .runtime)
        } catch let error as OpenAIDashboardScrapeIngest.IngestError {
            Self.exit(
                code: .failure,
                message: error.errorDescription ?? "OpenAI dashboard ingest failed.",
                output: output,
                kind: .runtime)
        } catch {
            Self.exit(
                code: .failure,
                message: error.localizedDescription,
                output: output,
                kind: .runtime)
        }
    }
}

struct OpenAIDashboardIngestOptions: CommanderParsable {
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

    @Flag(name: .long("stdin"), help: "Read the scrape payload JSON from stdin")
    var stdin: Bool = false

    @Option(name: .long("input"), help: "Read the scrape payload JSON from a file path")
    var input: String?
}
