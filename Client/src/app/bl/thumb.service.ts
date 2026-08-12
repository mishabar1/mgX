import { Injectable } from '@angular/core';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

// Renders a glb/gltf model (under assets/games) to a framed PNG data URL, for UI thumbnails.
// Standalone (own loader + tiny offscreen renderer) so pages without a 3D scene can use it.
// Results are cached by URL.
@Injectable({ providedIn: 'root' })
export class ThumbService {
  private loader = new GLTFLoader();
  private renderer?: THREE.WebGLRenderer;
  private cache: { [url: string]: string } = {};

  async render(assetRelUrl: string, size = 256): Promise<string> {
    if (!assetRelUrl) return '';
    if (this.cache[assetRelUrl]) return this.cache[assetRelUrl];
    try {
      const url = '/assets/games/' + assetRelUrl;
      const gltf: any = await new Promise((res, rej) => this.loader.load(url, res, undefined, rej));
      const model = gltf.scene;
      const scene = new THREE.Scene();
      scene.add(new THREE.HemisphereLight(0xffffff, 0x888c96, 1.7));
      scene.add(new THREE.AmbientLight(0xffffff, 0.6));
      const dir = new THREE.DirectionalLight(0xffffff, 1.4); dir.position.set(3, 6, 5); scene.add(dir);
      scene.add(model);

      const box = new THREE.Box3().setFromObject(model);
      const sphere = box.getBoundingSphere(new THREE.Sphere());
      const r = sphere.radius || 1;
      const fov = 35;
      const dist = (r / Math.sin((fov / 2) * Math.PI / 180)) * 1.1;
      const cam = new THREE.PerspectiveCamera(fov, 1, 0.01, 2000);
      cam.position.set(sphere.center.x + dist * 0.25, sphere.center.y + dist * 0.3, sphere.center.z + dist);
      cam.lookAt(sphere.center);

      if (!this.renderer) this.renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
      this.renderer.setSize(size, size);
      this.renderer.setClearColor(0xeef2f8, 1);   // light background so darker heroes stay clear
      this.renderer.render(scene, cam);
      const data = this.renderer.domElement.toDataURL('image/png');
      this.cache[assetRelUrl] = data;
      return data;
    } catch {
      return '';
    }
  }
}
