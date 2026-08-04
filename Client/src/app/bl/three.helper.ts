import {ElementRef, Injectable} from '@angular/core';
import * as THREE from 'three';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment.js';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls.js';
import {InteractionManager} from '../services/mg.interaction.manager';
import * as ThreeMeshUI from 'three-mesh-ui';
import * as TWEEN from '@tweenjs/tween.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader.js';
import {STLLoader} from 'three/examples/jsm/loaders/STLLoader.js';
import {OBJLoader} from 'three/examples/jsm/loaders/OBJLoader.js';
import {BufferGeometry, Line, Matrix4, Raycaster, TextureLoader, Vector3} from 'three';
import {FontLoader} from 'three/examples/jsm/loaders/FontLoader.js';
import {Group} from 'three/src/objects/Group.js';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController.js';
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory.js';


export class ThreeHelper{

  static pi: number = 3.14;

  static func1(radius:number) {
    return this.pi * radius * radius;
  }

}
