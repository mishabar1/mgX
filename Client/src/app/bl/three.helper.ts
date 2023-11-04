import {ElementRef, Injectable} from '@angular/core';
import * as THREE from 'three';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls';
import {InteractionManager} from '../services/mg.interaction.manager';
import * as ThreeMeshUI from 'three-mesh-ui';
import * as TWEEN from '@tweenjs/tween.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader';
import {STLLoader} from 'three/examples/jsm/loaders/STLLoader';
import {OBJLoader} from 'three/examples/jsm/loaders/OBJLoader';
import {BufferGeometry, Line, Matrix4, Raycaster, TextureLoader, Vector3} from 'three';
import {FontLoader} from 'three/examples/jsm/loaders/FontLoader';
import {Group} from 'three/src/objects/Group';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController';
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory';


export class ThreeHelper{

  static pi: number = 3.14;

  static func1(radius:number) {
    return this.pi * radius * radius;
  }

}
