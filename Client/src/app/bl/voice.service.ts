import {Injectable} from '@angular/core';
import {SignalrService} from '../services/SignalrService';

// One remote peer we're connected to over WebRTC.
interface Peer {
  pc: RTCPeerConnection;
  audio: HTMLAudioElement;
  name: string;
}

/**
 * Peer-to-peer voice chat over WebRTC. The SignalR hub is used only to exchange the
 * connection handshake (offer / answer / ICE); the audio streams flow directly between
 * browsers. A small mesh (one RTCPeerConnection per other participant) — fine for the
 * 2–3 players in a game. STUN only (no TURN) for this first version.
 *
 * Also provides:
 *  - a "who is speaking" indicator (WebAudio level metering of each stream), and
 *  - auto-transcript / live captions via the browser SpeechRecognition API (Chrome/Edge;
 *    degrades gracefully where unsupported). Each browser transcribes its OWN mic and
 *    broadcasts the text over the hub so everyone sees a shared transcript.
 */
@Injectable({providedIn: 'root'})
export class VoiceService {

  joined = false;
  muted = false;
  error = '';
  selfName = '';
  selfSpeaking = false;
  // remote participants currently in the call (not including yourself)
  participants: { id: string; name: string; speaking?: boolean }[] = [];

  // captions / transcript
  transcriptSupported = false;
  captionsOn = false;
  transcript: { name: string; text: string }[] = [];

  private gameId = '';
  private localStream?: MediaStream;
  private peers: { [connId: string]: Peer } = {};

  // speaking detection
  private audioCtx?: AudioContext;
  private analysers: { [id: string]: { analyser: AnalyserNode; data: Uint8Array<ArrayBuffer> } } = {};
  private levelTimer?: any;

  // speech recognition
  private recognition?: any;

  private readonly rtcConfig: RTCConfiguration = {
    iceServers: [
      {urls: 'stun:stun.l.google.com:19302'},
      {urls: 'stun:stun1.l.google.com:19302'},
    ],
  };

  constructor(private signal: SignalrService) {}

  private get hub() { return this.signal.hubConnection; }

  /** Ask for the mic, wire up signaling, and announce ourselves to the room. */
  async join(gameId: string, userName: string) {
    if (this.joined) return;
    this.error = '';
    this.gameId = gameId;
    this.selfName = userName || 'player';

    try {
      this.localStream = await navigator.mediaDevices.getUserMedia({audio: true, video: false});
    } catch (e) {
      this.error = 'Microphone unavailable or permission denied.';
      return;
    }

    this.wireSignaling();
    this.setupSpeaking();
    this.setupRecognition();
    this.joined = true;
    this.signal.joinVoice(gameId, this.selfName);

    // Auto-start captions where supported (the "auto" in auto-transcript).
    if (this.transcriptSupported) {
      this.captionsOn = true;
      try { this.recognition?.start(); } catch {}
    }
  }

  private wireSignaling() {
    this.hub.off('VoicePeers');
    this.hub.off('VoicePeerJoined');
    this.hub.off('VoicePeerLeft');
    this.hub.off('VoiceSignal');
    this.hub.off('Transcript');

    // We just joined: offer to everyone already here.
    this.hub.on('VoicePeers', async (peers: any[]) => {
      for (const p of peers || []) {
        const pc = this.createPeer(p.connectionId, p.userName);
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        this.signal.voiceSignal(p.connectionId, {type: 'offer', sdp: offer});
      }
    });

    // Someone new joined: just note them; their offer will arrive via VoiceSignal.
    this.hub.on('VoicePeerJoined', (p: any) => {
      this.addParticipant(p.connectionId, p.userName);
    });

    this.hub.on('VoicePeerLeft', (p: any) => {
      this.removePeer(p.connectionId);
    });

    // Incoming handshake message from a specific peer.
    this.hub.on('VoiceSignal', async (msg: any) => {
      const from = msg.fromConnectionId;
      const data = msg.data;
      if (!from || !data) return;

      if (data.type === 'offer') {
        const pc = this.peers[from]?.pc || this.createPeer(from, this.nameFor(from));
        await pc.setRemoteDescription(new RTCSessionDescription(data.sdp));
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        this.signal.voiceSignal(from, {type: 'answer', sdp: answer});
      } else if (data.type === 'answer') {
        const pc = this.peers[from]?.pc;
        if (pc) await pc.setRemoteDescription(new RTCSessionDescription(data.sdp));
      } else if (data.type === 'candidate' && data.candidate) {
        const pc = this.peers[from]?.pc;
        if (pc) { try { await pc.addIceCandidate(new RTCIceCandidate(data.candidate)); } catch {} }
      }
    });

    // Live transcript line from any participant (including our own echo).
    this.hub.on('Transcript', (msg: any) => {
      if (!msg || msg.gameId !== this.gameId) return;
      this.addTranscript(msg.userName, msg.text);
    });
  }

  private createPeer(connId: string, name: string): RTCPeerConnection {
    if (this.peers[connId]) return this.peers[connId].pc;

    const pc = new RTCPeerConnection(this.rtcConfig);
    // Send our mic to this peer.
    this.localStream!.getTracks().forEach(t => pc.addTrack(t, this.localStream!));

    // Play this peer's incoming audio.
    const audio = document.createElement('audio');
    audio.autoplay = true;
    (audio as any).playsInline = true;
    document.body.appendChild(audio);

    pc.ontrack = (ev) => {
      audio.srcObject = ev.streams[0];
      this.attachAnalyser(connId, ev.streams[0]); // metering for the speaking indicator
    };
    pc.onicecandidate = (ev) => {
      if (ev.candidate) this.signal.voiceSignal(connId, {type: 'candidate', candidate: ev.candidate});
    };
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed' || pc.connectionState === 'closed') this.removePeer(connId);
    };

    this.peers[connId] = {pc, audio, name};
    this.addParticipant(connId, name);
    return pc;
  }

  private nameFor(connId: string): string {
    return this.participants.find(x => x.id === connId)?.name || 'player';
  }

  private addParticipant(connId: string, name: string) {
    if (!this.participants.find(x => x.id === connId)) {
      this.participants.push({id: connId, name: name || 'player', speaking: false});
    }
  }

  private removePeer(connId: string) {
    const peer = this.peers[connId];
    if (peer) {
      try { peer.pc.close(); } catch {}
      peer.audio.srcObject = null;
      peer.audio.remove();
      delete this.peers[connId];
    }
    delete this.analysers[connId];
    this.participants = this.participants.filter(x => x.id !== connId);
  }

  // ---- speaking indicator (WebAudio level metering) ----
  private setupSpeaking() {
    try {
      this.audioCtx = new (window.AudioContext || (window as any).webkitAudioContext)();
      if (this.localStream) this.attachAnalyser('self', this.localStream);
      this.levelTimer = setInterval(() => this.tickLevels(), 150);
    } catch {}
  }

  private attachAnalyser(id: string, stream: MediaStream) {
    if (!this.audioCtx) return;
    try {
      const src = this.audioCtx.createMediaStreamSource(stream);
      const analyser = this.audioCtx.createAnalyser();
      analyser.fftSize = 512;
      src.connect(analyser); // analyser is a sink; not connected to destination, so no echo
      this.analysers[id] = {analyser, data: new Uint8Array(new ArrayBuffer(analyser.frequencyBinCount))};
    } catch {}
  }

  private tickLevels() {
    const THRESH = 12; // RMS deviation from silence
    for (const id of Object.keys(this.analysers)) {
      const a = this.analysers[id];
      a.analyser.getByteTimeDomainData(a.data as any);
      let sum = 0;
      for (let i = 0; i < a.data.length; i++) { const v = a.data[i] - 128; sum += v * v; }
      const rms = Math.sqrt(sum / a.data.length);
      const speaking = rms > THRESH;
      if (id === 'self') {
        this.selfSpeaking = !this.muted && speaking;
      } else {
        const p = this.participants.find(x => x.id === id);
        if (p) p.speaking = speaking;
      }
    }
  }

  // ---- auto transcript / captions (browser SpeechRecognition) ----
  private setupRecognition() {
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (!SR) { this.transcriptSupported = false; return; }
    this.transcriptSupported = true;

    const rec = new SR();
    rec.continuous = true;
    rec.interimResults = false;
    rec.lang = navigator.language || 'en-US';
    rec.onresult = (e: any) => {
      for (let i = e.resultIndex; i < e.results.length; i++) {
        if (e.results[i].isFinal) {
          const text = (e.results[i][0].transcript || '').trim();
          // Broadcast only — our own line comes back via the "Transcript" echo, so it's
          // added once (and in the same order everyone else sees).
          if (text) { try { this.signal.sendTranscript(this.gameId, this.selfName, text); } catch {} }
        }
      }
    };
    rec.onend = () => { if (this.joined && this.captionsOn) { try { rec.start(); } catch {} } };
    rec.onerror = () => {};
    this.recognition = rec;
  }

  toggleCaptions() {
    if (!this.transcriptSupported) return;
    this.captionsOn = !this.captionsOn;
    if (this.captionsOn) { try { this.recognition?.start(); } catch {} }
    else { try { this.recognition?.stop(); } catch {} }
  }

  private addTranscript(name: string, text: string) {
    if (!text) return;
    this.transcript.push({name: name || 'player', text});
    if (this.transcript.length > 60) this.transcript.shift();
  }

  /** Mute/unmute your own mic (keeps the connection up). */
  toggleMute() {
    this.muted = !this.muted;
    this.localStream?.getAudioTracks().forEach(t => t.enabled = !this.muted);
  }

  /** Leave the call and tear everything down. */
  leave() {
    if (!this.joined) return;
    try { this.signal.leaveVoice(this.gameId); } catch {}
    Object.keys(this.peers).forEach(id => this.removePeer(id));
    this.localStream?.getTracks().forEach(t => t.stop());
    this.localStream = undefined;

    if (this.levelTimer) { clearInterval(this.levelTimer); this.levelTimer = undefined; }
    this.analysers = {};
    try { this.audioCtx?.close(); } catch {}
    this.audioCtx = undefined;

    try { this.recognition?.stop(); } catch {}
    this.recognition = undefined;
    this.captionsOn = false;

    this.participants = [];
    this.selfSpeaking = false;
    this.joined = false;
    this.muted = false;

    this.hub.off('VoicePeers');
    this.hub.off('VoicePeerJoined');
    this.hub.off('VoicePeerLeft');
    this.hub.off('VoiceSignal');
    this.hub.off('Transcript');
  }
}
