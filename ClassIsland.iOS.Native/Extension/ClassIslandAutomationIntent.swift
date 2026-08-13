import AppIntents
import Foundation

private let automationIntentNotificationName = Notification.Name(
    "ClassIslandAutomationIntentRun"
)
private let pendingAutomationUriKey =
    "classisland.shortcuts.pending-automation-uri"

@available(iOS 16.0, *)
struct RunClassIslandAutomationIntent: OpenIntent {
    static let title: LocalizedStringResource = "运行 ClassIsland 自动化"
    static let description = IntentDescription(
        "通过“调用 Uri 时”触发器运行一个 ClassIsland 自动化工作流。"
    )


    @Parameter(
        title: "URI 后缀",
        description: "填写自动化工作流中“调用 Uri 时”触发器配置的 URI 后缀。"
    )
    var target: String

    static var parameterSummary: some ParameterSummary {
        Summary("运行 ClassIsland 自动化 \(\.$target)")
    }

    func perform() async throws -> some IntentResult {
        let suffix = try Self.validate(target)
        let uri = "classisland://app/api/automation/run/\(suffix)"
        UserDefaults.standard.set(uri, forKey: pendingAutomationUriKey)
        NotificationCenter.default.post(
            name: automationIntentNotificationName,
            object: nil
        )
        return .result()
    }

    private static func validate(_ value: String) throws -> String {
        let suffix = value.trimmingCharacters(in: .whitespacesAndNewlines)
        let allowedCharacters = CharacterSet(
            charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_/~"
        )
        let segments = suffix.split(separator: "/", omittingEmptySubsequences: false)
        guard !suffix.isEmpty,
              suffix.unicodeScalars.allSatisfy(allowedCharacters.contains),
              segments.allSatisfy({ !$0.isEmpty && $0 != "." && $0 != ".." }) else {
            throw InvalidAutomationSuffixError()
        }

        return suffix
    }
}

private struct InvalidAutomationSuffixError: LocalizedError {
    var errorDescription: String? {
        "URI 后缀只能包含英文字母、数字、-、_、~ 和用于分段的 /。"
    }
}
