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
 */
@Injectable({providedIn: 'root'})
export class VoiceService {

  joined = false;
  muted = false;
  error = '';
  // remote participants currently in the call (not including yourself)
  participants: { id: string; name: string }[] = [];

  private gameId = '';
  private localStream?: MediaStream;
  private peers: { [connId: string]: Peer } = {};

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

    try {
      this.localStream = await navigator.mediaDevices.getUserMedia({audio: true, video: false});
    } catch (e) {
      this.error = 'Microphone unavailable or permission denied.';
      return;
    }

    this.wireSignaling();
    this.joined = true;
    this.signal.joinVoice(gameId, userName);
  }

  private wireSignaling() {
    this.hub.off('VoicePeers');
    this.hub.off('VoicePeerJoined');
    this.hub.off('VoicePeerLeft');
    this.hub.off('VoiceSignal');

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

    pc.ontrack = (ev) => { audio.srcObject = ev.streams[0]; };
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
      this.participants.push({id: connId, name: name || 'player'});
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
    this.participants = this.participants.filter(x => x.id !== connId);
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
    this.participants = [];
    this.joined = false;
    this.muted = false;
    this.hub.off('VoicePeers');
    this.hub.off('VoicePeerJoined');
    this.hub.off('VoicePeerLeft');
    this.hub.off('VoiceSignal');
  }
}
