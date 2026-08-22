import AppIntents
import Foundation

private let automationCatalogKey =
    "classisland.shortcuts.automation-catalog"
private let automationIntentNotificationName = Notification.Name(
    "ClassIslandAutomationIntentRun"
)
private let pendingAutomationUriKey =
    "classisland.shortcuts.pending-automation-uri"

private struct StoredAutomation: Decodable {
    let id: String
    let name: String
    let uriSuffix: String
}

@available(iOS 16.0, *)
private enum AutomationCatalog {
    static func load() -> [ClassIslandAutomation] {
        guard let json = UserDefaults.standard.string(forKey: automationCatalogKey),
              let data = json.data(using: .utf8),
              let stored = try? JSONDecoder().decode([StoredAutomation].self, from: data) else {
            return []
        }

        return stored.map {
            ClassIslandAutomation(
                id: $0.id,
                name: $0.name,
                uriSuffix: $0.uriSuffix
            )
        }
    }
}

@available(iOS 16.0, *)
struct ClassIslandAutomation: AppEntity {
    static let typeDisplayRepresentation = TypeDisplayRepresentation(
        name: "ClassIsland 自动化"
    )
    static let defaultQuery = ClassIslandAutomationQuery()

    let id: String
    let name: String
    let uriSuffix: String

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(
            title: "\(name)",
            subtitle: "URI 后缀：\(uriSuffix)"
        )
    }
}

@available(iOS 16.0, *)
struct ClassIslandAutomationQuery: EntityStringQuery {
    func entities(for identifiers: [String]) async throws -> [ClassIslandAutomation] {
        let entitiesById = Dictionary(
            uniqueKeysWithValues: AutomationCatalog.load().map { ($0.id, $0) }
        )
        return identifiers.compactMap { entitiesById[$0] }
    }

    func suggestedEntities() async throws -> [ClassIslandAutomation] {
        AutomationCatalog.load()
    }

    func entities(matching string: String) async throws -> [ClassIslandAutomation] {
        let query = string.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !query.isEmpty else {
            return AutomationCatalog.load()
        }

        return AutomationCatalog.load().filter {
            $0.name.localizedCaseInsensitiveContains(query) ||
                $0.uriSuffix.localizedCaseInsensitiveContains(query)
        }
    }
}

@available(iOS 16.0, *)
struct RunClassIslandAutomationIntent: OpenIntent {
    static let title: LocalizedStringResource = "运行 ClassIsland 自动化"
    static let description = IntentDescription(
        "运行一个配置了“调用 Uri 时”触发器的 ClassIsland 自动化工作流。"
    )

    @Parameter(
        title: "自动化",
        description: "选择 ClassIsland 中现有的自动化工作流。"
    )
    var target: ClassIslandAutomation

    static var parameterSummary: some ParameterSummary {
        Summary("运行 \(\.$target)")
    }

    func perform() async throws -> some IntentResult {
        guard let automation = AutomationCatalog.load().first(where: {
            $0.id == target.id
        }) else {
            throw AutomationUnavailableError()
        }

        let suffix = try Self.validate(automation.uriSuffix)
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

private struct AutomationUnavailableError: LocalizedError {
    var errorDescription: String? {
        "所选自动化不存在或已更改，请在快捷指令中重新选择。"
    }
}

private struct InvalidAutomationSuffixError: LocalizedError {
    var errorDescription: String? {
        "自动化的 URI 后缀只能包含英文字母、数字、-、_、~ 和用于分段的 /。"
    }
}
