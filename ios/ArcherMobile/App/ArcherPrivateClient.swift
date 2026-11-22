import Foundation
import CryptoKit

struct ArcherOverview: Decodable {
    struct Message: Decodable, Identifiable { let id: UUID; let role: String; let content: String }
    struct Assignment: Decodable, Identifiable { let id: UUID; let project: String; let title: String; let status: String }
    struct Alert: Decodable, Identifiable { let id: UUID; let title: String; let summary: String; let priority: String }
    let conversation: [Message]
    struct Research: Decodable, Identifiable { let id: UUID; let title: String; let status: String; let findings: String? }
    let research: [Research]
    let assignments: [Assignment]
    let alerts: [Alert]
}
struct ArcherMessageResult: Decodable { let status: String; let answer: String? }

private final class RejectRedirects: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}
@MainActor
final class ArcherPrivateClient {
    private struct ChallengeRequest: Encodable { let method: String; let path: String; let bodyHash: String }
    private struct Challenge: Decodable {
        let ticket: String; let nonce: String; let method: String; let path: String; let bodyHash: String
    }
    private struct ReceiptRequest: Encodable { let requestId: UUID }
    private struct MessageRequest: Encodable { let requestId: UUID; let message: String; let voiceMode: Bool }
    private let session = ArcherPrivateClient.makeSession()
    private static func makeSession() -> URLSession {
        let c = URLSessionConfiguration.ephemeral
        c.httpCookieStorage = nil; c.urlCache = nil
        c.requestCachePolicy = .reloadIgnoringLocalCacheData
        c.timeoutIntervalForRequest = 30; c.timeoutIntervalForResource = 300
        return URLSession(configuration: c, delegate: RejectRedirects(), delegateQueue: nil)
    }
    static var configuredHost: String? {
        guard let path = Bundle.main.url(forResource: "Gateway", withExtension: "plist"),
              let data = try? Data(contentsOf: path),
              let info = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: String],
              let host = info["Host"], host.count <= 180,
              host.range(of: #"^[a-z0-9][a-z0-9.-]+\.ts\.net$"#, options: .regularExpression) != nil,
              URL(string: "https://" + host)?.host == host else { return nil }
        return host
    }
    private func request(_ path: String, _ method: String, _ token: String, _ body: Data?) throws -> URLRequest {
        guard ["/v1/challenge", "/v1/overview", "/v1/message", "/v1/receipt"].contains(path),
              let host = Self.configuredHost, let url = URL(string: "https://" + host + path) else {
            throw URLError(.unsupportedURL)
        }
        var r = URLRequest(url: url); r.httpMethod = method; r.httpBody = body
        r.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
        r.setValue("application/json", forHTTPHeaderField: "Accept")
        if body != nil { r.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        return r
    }
    private func response<T: Decodable>(_ request: URLRequest, as type: T.Type) async throws -> T {
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200,
              http.url?.host == Self.configuredHost, http.url?.scheme == "https",
              data.count <= 512 * 1024 else { throw URLError(.badServerResponse) }
        return try JSONDecoder().decode(type, from: data)
    }
    private func signed<T: Decodable>(_ path: String, method: String, body: Data?,
                                      auth: ArcherAuthSession, device: ArcherDeviceIdentity,
                                      type: T.Type) async throws -> T {
        let token = try await auth.gatewayAccessToken()
        let hash = SHA256.hash(data: body ?? Data()).map { String(format: "%02X", $0) }.joined()
        let initial = ChallengeRequest(method: method,path: path,bodyHash: hash)
        let start = try request("/v1/challenge","POST",token,JSONEncoder().encode(initial))
        let c = try await response(start, as: Challenge.self)
        guard c.method == method, c.path == path, c.bodyHash == hash,
              c.ticket.range(of: #"^[A-F0-9]{48}$"#, options: .regularExpression) != nil,
              c.nonce.range(of: #"^[A-F0-9]{64}$"#, options: .regularExpression) != nil else { throw URLError(.cannotParseResponse) }
        let payload = ["ARCHER_PRIVATE_V1",c.ticket,c.nonce,method,path,hash].joined(separator: "\n")
        let proof = try device.gatewayProof(Data(payload.utf8))
        var final = try request(path,method,token,body)
        final.setValue(c.ticket,forHTTPHeaderField:"X-Archer-Ticket")
        final.setValue(proof.publicKey.base64EncodedString(),forHTTPHeaderField:"X-Archer-Key")
        final.setValue(proof.signature.base64EncodedString(),forHTTPHeaderField:"X-Archer-Proof")
        return try await response(final,as:type)
    }
    func overview(auth: ArcherAuthSession, device: ArcherDeviceIdentity) async throws -> ArcherOverview {
        try await signed("/v1/overview",method:"GET",body:nil,auth:auth,device:device,type:ArcherOverview.self)
    }
    func receipt(_ id: UUID, auth: ArcherAuthSession, device: ArcherDeviceIdentity) async throws -> ArcherMessageResult {
        let body = try JSONEncoder().encode(ReceiptRequest(requestId:id))
        return try await signed("/v1/receipt",method:"POST",body:body,auth:auth,device:device,type:ArcherMessageResult.self)
    }
    func send(_ message: String, id: UUID, auth: ArcherAuthSession, device: ArcherDeviceIdentity) async throws -> ArcherMessageResult {
        let body = try JSONEncoder().encode(MessageRequest(requestId:id,message:message,voiceMode:false))
        return try await signed("/v1/message",method:"POST",body:body,auth:auth,device:device,type:ArcherMessageResult.self)
    }
}
