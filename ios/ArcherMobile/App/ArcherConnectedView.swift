import SwiftUI
import AVFoundation

@MainActor
struct ArcherConnectedView: View {
    @EnvironmentObject private var auth: ArcherAuthSession
    @Environment(\.scenePhase) private var scenePhase
    @ObservedObject var device: ArcherDeviceIdentity
    @State private var client = ArcherPrivateClient()
    @State private var data: ArcherOverview?
    @State private var message = ""
    @State private var status = "Connect to your private Archer server."
    @State private var working = false
    @State private var queuedRequest: UUID? = ArcherPendingReceipt.read()
    @State private var readAloud = false
    @State private var speech = AVSpeechSynthesizer()
    @State private var selected = "Chat"

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Your Archer").font(.title2.bold())
            if let host = ArcherPrivateClient.configuredHost {
                Text(host).font(.caption).foregroundStyle(.secondary)
            } else {
                Text("Server address has not been configured in this build.").font(.subheadline)
            }
            if device.prepared {
                DisclosureGroup("Device approval") {
                    Text("Your server owner needs to approve this public fingerprint:")
                    Text(device.approvalFingerprint).font(.caption.monospaced()).textSelection(.enabled)
                }
            } else {
                Text("Prepare this iPhone's security key in Settings below before connecting.")
            }
            HStack {
                Button("Connect / refresh") { Task { await reload() } }
                    .buttonStyle(.borderedProminent).disabled(working || !device.prepared || ArcherPrivateClient.configuredHost == nil)
                if working { ProgressView() }
            }
            Text(status).font(.subheadline).foregroundStyle(.secondary).accessibilityIdentifier("gatewayStatus")
            if let data {
                Picker("Workspace", selection: $selected) {
                    Text("Chat").tag("Chat"); Text("Work").tag("Work"); Text("Inbox").tag("Inbox")
                }.pickerStyle(.segmented)
                if selected == "Chat" {
                    ForEach(data.conversation.suffix(25)) { item in
                        VStack(alignment: .leading, spacing: 5) {
                            Text(item.role == "user" ? "You" : "Archer").font(.caption.weight(.semibold)).foregroundStyle(.cyan)
                            Text(item.content).font(.body).textSelection(.enabled)
                        }.padding(10).frame(maxWidth:.infinity,alignment:.leading)
                            .background(.white.opacity(0.05),in:RoundedRectangle(cornerRadius:10))
                    }
                    TextField("Tell Archer what to do…",text:$message,axis:.vertical).lineLimit(2...5)
                        .textFieldStyle(.roundedBorder).disabled(working || queuedRequest != nil)
                    HStack {
                        Button("Send") { Task { await send() } }
                            .buttonStyle(.borderedProminent)
                            .disabled(working || queuedRequest != nil || message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || message.count > 3000)
                        if queuedRequest != nil {
                            Button("Check last message") { Task { await send(recover:true) } }.disabled(working)
                        }
                    }
                    Toggle("Read replies aloud",isOn:$readAloud)
                } else if selected == "Work" {
                    if let summary = data.overnightSummary, !summary.isEmpty {
                        DisclosureGroup("Recorded work · last 12 hours") {
                            Text(summary)
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                                .textSelection(.enabled)
                            Text("This is a local work summary, not evidence of a deployment, revenue or a sent notification.")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .accessibilityIdentifier("overnightWorkSummary")
                    }
                    if data.assignments.isEmpty && data.research.isEmpty { Text("No recorded assignments.") }
                    ForEach(data.research) { item in
                        VStack(alignment:.leading,spacing:5) {
                            Text("Research · " + item.status).font(.caption.bold()).foregroundStyle(.cyan)
                            Text(item.title)
                            if let findings = item.findings, !findings.isEmpty {
                                DisclosureGroup("Read findings") {
                                    Text(findings).font(.subheadline).textSelection(.enabled)
                                }
                            }
                        }.padding(.vertical,6)
                    }
                    ForEach(data.assignments) { item in
                        VStack(alignment:.leading,spacing:5) {
                            Text(item.project + " · " + item.status).font(.caption.bold()).foregroundStyle(.cyan)
                            Text(item.title)
                            if let total = item.totalTasks, total > 0,
                               let verified = item.verifiedTasks {
                                let done = min(max(0, verified), total)
                                ProgressView(value: Double(done), total: Double(total))
                                    .tint(.cyan)
                                    .accessibilityLabel("Verified coding tasks")
                                    .accessibilityValue("\(done) of \(total)")
                                Text("\(done) of \(total) code tasks verified")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            } else if item.status == "planning" {
                                ProgressView("Planning tasks")
                                    .font(.caption)
                                    .tint(.cyan)
                            }
                            if let link = item.recordedReviewURL {
                                Link("Review recorded draft on GitHub", destination: link)
                                    .font(.subheadline.weight(.semibold))
                            }
                        }.padding(.vertical,6)
                    }
                } else {
                    if data.alerts.isEmpty { Text("No recorded alerts.") }
                    ForEach(data.alerts) { item in
                        VStack(alignment:.leading,spacing:5) {
                            Text(item.priority).font(.caption).foregroundStyle(.cyan)
                            Text(item.title).font(.headline)
                            Text(item.summary).font(.subheadline)
                        }.padding(.vertical,6)
                    }
                }
            }
        }
        .onDisappear { speech.stopSpeaking(at:.immediate) }
        // Read-only status refresh when the owner opens or returns to the
        // app. No background polling, wake word, push registration or replay
        // of a previously sent command.
        .task {
            if auth.signedIn && device.prepared &&
               ArcherPrivateClient.configuredHost != nil && data == nil {
                await reload()
            }
        }
        .onChange(of: scenePhase) { _, next in
            if next == .active && auth.signedIn && device.prepared &&
               ArcherPrivateClient.configuredHost != nil {
                Task { await reload() }
            }
        }
    }

    private func reload() async {
        guard !working else { return }; working = true; defer { working = false }
        do {
            let snapshot = try await client.overview(auth:auth,device:device)
            guard auth.signedIn else { data=nil; return }
            data=snapshot; status="Connected. Work and conversation are shared with Archer."
        } catch { data=nil; status="Could not connect. Check your private server, phone network, sign-in and approved device." }
    }

    private func send(recover: Bool = false) async {
        guard !working else { return }
        let text = message.trimmingCharacters(in:.whitespacesAndNewlines)
        if !recover {
            guard !text.isEmpty, text.count <= 3000 else { return }
            let id=UUID()
            guard ArcherPendingReceipt.save(id) else { queuedRequest=ArcherPendingReceipt.read(); status="A previous request needs checking, or secure receipt storage is unavailable."; return }
            queuedRequest=id; message=""
        }
        guard let id=queuedRequest else { return }
        working=true; defer { working=false }
        status="Archer is processing your message…"
        do {
            let result: ArcherMessageResult
            if recover { result=try await client.receipt(id,auth:auth,device:device) }
            else { result=try await client.send(text,id:id,auth:auth,device:device) }
            guard auth.signedIn else { data=nil; return }
            if result.status == "Pending" { status="The earlier message is still pending. Check again; this only reads its receipt."; return }
            guard ArcherPendingReceipt.clear() else { status="Reply received, but local receipt storage could not be cleared. Check again."; return }
            queuedRequest=nil
            status=result.status == "Completed" ? "Reply received." : "Message stopped: " + result.status + ". No automatic retry."
            data=try await client.overview(auth:auth,device:device)
            if readAloud, let answer=result.answer {
                // Never silently replace the requested British male voice.
                let voices=AVSpeechSynthesisVoice.speechVoices().filter { $0.language == "en-GB" && $0.gender == .male && $0.quality != .default }
                if let voice=voices.sorted(by:{$0.quality.rawValue > $1.quality.rawValue}).first {
                    let utterance=AVSpeechUtterance(string:String(answer.prefix(1500))); utterance.voice=voice
                    speech.stopSpeaking(at:.immediate); speech.speak(utterance)
                } else { status="Reply received. Install an enhanced British male iOS voice to read aloud." }
            }
        } catch { status="Connection interrupted. Use Check last message to look up the same request; no automatic resend." }
    }
}
