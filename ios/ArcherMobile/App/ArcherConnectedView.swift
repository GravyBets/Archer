import SwiftUI
import AVFoundation

@MainActor
struct ArcherConnectedView: View {
    @EnvironmentObject private var auth: ArcherAuthSession
    @ObservedObject var device: ArcherDeviceIdentity
    @State private var client = ArcherPrivateClient()
    @State private var data: ArcherOverview?
    @State private var message = ""
    @State private var status = "Connect to your private Archer server."
    @State private var working = false
    @State private var queuedRequest: UUID?
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
                    if data.assignments.isEmpty { Text("No recorded assignments.") }
                    ForEach(data.assignments) { item in
                        VStack(alignment:.leading,spacing:5) {
                            Text(item.project + " · " + item.status).font(.caption.bold()).foregroundStyle(.cyan)
                            Text(item.title)
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
            queuedRequest=UUID(); message=""
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
