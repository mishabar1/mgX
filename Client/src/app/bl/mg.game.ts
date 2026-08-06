import {GameData} from '../entities/game.data';
import {MgThree} from './mg.three';
import {UserData} from '../entities/user.data';
import {ItemData} from '../entities/item.data';
import * as THREE from 'three';
import {TextGeometry} from 'three/examples/jsm/geometries/TextGeometry.js';
import * as ThreeMeshUI from 'three-mesh-ui';
import {PlayerData} from '../entities/player.data';
import {find, forEach, keys} from 'lodash';
import {V3} from '../entities/V3';
import * as TWEEN from '@tweenjs/tween.js';
import {BoxGeometry, MathUtils, Mesh, MeshBasicMaterial} from 'three';
import {GeneralService} from './general.service';
import {SignalrService} from '../services/SignalrService';
import {Vector3} from "three/src/math/Vector3.js";
import {Box3} from "three/src/math/Box3.js";
import {Group} from "three/src/objects/Group.js";

export class MgGame{

  gameData!: GameData;
  mgThree!:MgThree;

  playerData!: PlayerData;
  allItems: { [key: string]: ItemData } = {};

  // red bounding-box helpers + the green/cyan player table & hand boxes —
  // all hidden unless DEBUG is toggled on.
  showDebugBoxes = false;
  boxHelpers: THREE.Object3D[] = [];
  debugPlanes: THREE.Mesh[] = []; // player table (green) + hand (cyan) anchors
  setDebugBoxes(on: boolean) {
    this.showDebugBoxes = on;
    this.boxHelpers.forEach(h => h.visible = on);
    // Toggle the coloured boxes' visibility via opacity so any child items
    // (e.g. cards on a player's table) keep rendering when DEBUG is off.
    this.debugPlanes.forEach(m => {
      const mat = m.material as THREE.MeshBasicMaterial;
      mat.transparent = true;
      mat.opacity = on ? 1 : 0;
      mat.needsUpdate = true;
    });
  }

  // Player avatar heads (suzanne). Optional — toggled from the game setup page
  // (persisted in localStorage) and read when the game loads.
  showHeads = true;
  headMeshes: THREE.Object3D[] = [];
  setHeadsVisible(on: boolean) {
    this.showHeads = on;
    this.headMeshes.forEach(h => h.visible = on);
  }

  getPlayerByUserId(userId: string): PlayerData | null | undefined {
    return find(this.gameData.players, p => p.user?.id == userId);
  }

  loadGame(mgThree:MgThree, user:UserData) {
    this.mgThree=mgThree;

    this.playerData = this.getPlayerByUserId(user.id)!;

    // Heads-visibility preference set on the game setup page (default: shown).
    this.showHeads = localStorage.getItem('mg.showHeads') !== 'false';

    console.log("loadGame");
    // console.log(gameData, dayjs().startOf('month').add(1, 'day').set('year', 2018).format('YYYY-MM-DD HH:mm:ss'));
    this.createItem(this.gameData.table, null);

    this.addPlayers();
    if (this.playerData) {
      this.mgThree.camera.position.set(this.playerData.camera.position.x, this.playerData.camera.position.y, this.playerData.camera.position.z);
    } else {
      this.mgThree.camera.position.set(this.gameData.observer.position.x, this.gameData.observer.position.y, this.gameData.observer.position.z);
    }

  }

  addPlayers(){

    forEach(this.gameData.players,(playerData:PlayerData)=>{

      let group = new THREE.Group();
      group.name = "PLAYER";
      playerData.avatar.mesh = group;
      group.position.set(playerData.avatar.position.x,playerData.avatar.position.y,playerData.avatar.position.z);
      group.lookAt(0,0,0);
      this.mgThree.scene.add(group);

      // head
      this.mgThree.gltfLoader.load('\\assets\\heads\\suzanne.glb', (gltf) => {
        const head: THREE.Group = gltf.scene;

        // let texture = this.mgThree.textureLoader.load("\\assets\\heads\\base-color.png");
        this.mgThree.textureLoader.load("\\assets\\heads\\metallic.png",texture=>{
          texture.flipY = false;
          head.traverse( function( object:any ) {
            if ( object.isMesh ) {
              object.material.map = texture;
              object.material.side = THREE.DoubleSide;
              object.material.needsUpdate = true;
            }
          } );
        });

        // playerData.avatar.mesh = mesh;
        // mesh.lookAt(0,0,0);
        if(this.playerData && this.playerData.id == playerData.id){
          // this is me, no need to add head
          // mesh.position.set(playerData.avatar.position.x,playerData.avatar.position.y,playerData.avatar.position.z);
          // this.mgThree.camera.add(mesh);
        }else{
          head.visible = this.showHeads;   // optional per the setup-page setting
          this.headMeshes.push(head);
          playerData.avatar.mesh!.add(head)
        }

      });

      //table (green) — a debug anchor for the player's table items
      const playerTable = new Mesh(new BoxGeometry(0.1, 0.01, 0.1), new MeshBasicMaterial({color: 0x00ff00, transparent: true, opacity: this.showDebugBoxes ? 1 : 0}));
      playerTable.name = "PLAYER TABLE";
      playerData.avatar.mesh?.add(playerTable);
      playerTable.position.set(0,-1.5,1.5);
      this.debugPlanes.push(playerTable);

      this.createItem(playerData.table,playerTable);

      //hand (cyan) — a debug anchor for the player's hand items
      const playerHand = new Mesh(new BoxGeometry(0.1, 0.01, 0.1), new MeshBasicMaterial({color: 0x00ffff, transparent: true, opacity: this.showDebugBoxes ? 1 : 0}));
      playerHand.name = "PLAYER HAND";
      playerData.avatar.mesh?.add(playerHand);
      playerHand.rotation.x = -Math.PI / 2;
      playerHand.position.set(0,0,1.5);
      this.debugPlanes.push(playerHand);

      this.createItem(playerData.hand, playerHand);



    });
  }

  createItem(itemData: ItemData, parentMesh: THREE.Object3D | null) {
    //console.log("createItem",itemData,parentMesh);

    if (itemData.asset) {
      // Guard: skip items whose asset key isn't in the dictionary. Without this, one
      // unresolved asset threw and aborted loadGame — blanking the ENTIRE scene.
      if (!this.gameData.assets[itemData.asset]) {
        console.warn('Skipping item with unknown asset key:', itemData.asset, itemData.name);
        this.allItems[itemData.id] = itemData;
        return;
      }
      const frontURL = '\\assets\\games\\' + this.gameData.assets[itemData.asset].frontURL;
      const backURL = '\\assets\\games\\' + (this.gameData.assets[itemData.asset].backURL || this.gameData.assets[itemData.asset].frontURL);
      const asset = this.gameData.assets[itemData.asset];
      const assetType = asset.type;

      if (assetType == "OBJECT") {
        if (frontURL.toLowerCase().endsWith("glb") || frontURL.toLowerCase().endsWith("gltf")) {
          this.mgThree.gltfLoader.load(frontURL, (gltf) => {
            const group:Group = gltf.scene;
            // console.log(gltf.animations.length);
            // debugger;

            if(gltf.animations && gltf.animations.length && itemData.animationIdx!=null) {
              let mixer = new THREE.AnimationMixer(group);
              mixer.clipAction(gltf.animations[itemData.animationIdx]).play();
              this.mgThree.animationMixers.push(mixer);
            }

            // scale to 1
            const box = new THREE.Box3();
            box.setFromObject(group);
            // mesh.geometry.computeBoundingBox();
            // let box = mesh.geometry.boundingBox;
            // console.log(box);
            let x= Math.abs(box!.min.x) + Math.abs(box!.max.x);
            let z= Math.abs(box!.min.z) + Math.abs(box!.max.z);
            let scaleX =  asset.scale.x / Math.max(x,z);
            let scaleY =  asset.scale.y / Math.max(x,z);
            let scaleZ =  asset.scale.z / Math.max(x,z);

            group.scale.set(scaleX,scaleY,scaleZ);
            let g = new Group();
            g.add(group);


            this.processItem(itemData, g, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("stl")) {
          this.mgThree.stlLoader.load(frontURL, (geometry) => {
            const mesh = new THREE.Mesh(geometry);
            // NOT SURE WHY NEED TO ROTATE.... BUT NEED...  :-(
            mesh.rotation.x = -Math.PI / 2;
            let group = new THREE.Group();
            group.add(mesh);


            // scale to 1
            const box = new THREE.Box3();
            box.setFromObject(mesh);
            // mesh.geometry.computeBoundingBox();
            // let box = mesh.geometry.boundingBox;
            // console.log(box);
            let x= Math.abs(box!.min.x) + Math.abs(box!.max.x);
            let z= Math.abs(box!.min.z) + Math.abs(box!.max.z);
            let scale =  1 / Math.max(x,z);
            mesh.scale.set(scale,scale,scale);
            // let g = new Group();
            // g.add(group);

            this.processItem(itemData, group, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("obj")) {
          this.mgThree.objLoader.load(frontURL, (group) => {
            //     // (object.children[0] as THREE.Mesh).material = material
            //     // object.traverse(function (child) {
            //     //     if ((child as THREE.Mesh).isMesh) {
            //     //         (child as THREE.Mesh).material = material
            //     //     }
            //     // })

            // scale to 1
            const box = new THREE.Box3();
            box.setFromObject(group);
            // mesh.geometry.computeBoundingBox();
            // let box = mesh.geometry.boundingBox;
            // console.log(box);
            let x= Math.abs(box!.min.x) + Math.abs(box!.max.x);
            let z= Math.abs(box!.min.z) + Math.abs(box!.max.z);
            let scale =  1 / Math.max(x,z);
            group.scale.set(scale,scale,scale);
            let g = new Group();
            g.add(group);

            this.processItem(itemData, g, parentMesh);
          });
        }
      }

      if (assetType == "TOKEN") {

        this.mgThree.textureLoader.load(frontURL, frontTexture => {
          // console.log( frontTexture.image.width, frontTexture.image.height );
          let aspect = frontTexture.image.width / frontTexture.image.height;
          let x = 1;
          let z = 1 / aspect;
          if (aspect < 1) {
            z = 1;
            x = aspect;
          }

          var cubeMaterial = [

            new THREE.MeshBasicMaterial({
              //left
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //right
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // top
              map: frontTexture, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // bottom
              map: this.mgThree.textureLoader.load(backURL), transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // front
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //back
              color: 0xffffff, opacity: 0.5, transparent: true
            })
          ];

          let mesh = new THREE.Mesh(new THREE.BoxGeometry(x, x / 100, z), cubeMaterial);
          this.processItem(itemData, mesh, parentMesh);

        });

      }

      if (assetType == "TEXT3D") {
        this.mgThree.fontLoader.load('https://threejs.org/examples/fonts/helvetiker_regular.typeface.json', (font) => {

          const geometry = new TextGeometry(itemData.text!, {
            font: font,
            size: 0.5,
            depth: 0.2,
            curveSegments: 12,
            bevelEnabled: true,
            bevelThickness: 0.03,
            bevelSize: 0.02,
            bevelOffset: 0,
            bevelSegments: 5,
          });
          geometry.center(); // center the text on the item's position
          var textMaterial = new THREE.MeshPhongMaterial(
            {color: 0xff0000, specular: 0xffffff}
          );
          let mesh = new THREE.Mesh(geometry, textMaterial);
          this.processItem(itemData, mesh, parentMesh);
        });

      }
      if (assetType == "TEXTBLOCK") {

        // DOCS ! - RTFM !
        // https://github.com/felixmariotto/three-mesh-ui/wiki/API-documentation
        //
        const container: any = new ThreeMeshUI.Block({

          bestFit: 'auto',
          width: 100,
          height: 100,
          justifyContent: 'center',
          textAlign: 'center',
          fontFamily: 'assets/fonts/Roboto-msdf.json',
          fontTexture: 'assets/fonts/Roboto-msdf.png',
          fontColor: new THREE.Color(0x000000),
          // borderRadius: 0.05,
          backgroundOpacity: 0
        });

        // container.position.set( 0, 0, 0 );
        // container.rotation.x = -0.55;
        // this.scene.add( container );

        const t1: any = new ThreeMeshUI.Text({content: itemData.text});
        container.add(t1);

        this.processItem(itemData, container, parentMesh);

      }
      if (assetType == "SOUND") {

        // console.log(itemData,frontURL,backURL,assetType);

        // create the PositionalAudio object (passing in the listener)
        const sound = new THREE.PositionalAudio(this.mgThree.audioListener);

        // load a sound and set it as the Audio object's buffer
        const audioLoader = new THREE.AudioLoader();
        audioLoader.load(frontURL, function (buffer) {
          sound.setBuffer(buffer);
          sound.setLoop(itemData.playType == "LOOP");
          sound.setVolume(1);
          sound.play();
          // sound.stop()
        });

        let soundGroup = new THREE.Group()
        soundGroup.add(sound);
        this.processItem(itemData, soundGroup, parentMesh);


      }


    } else {
      const mesh: THREE.Group = new THREE.Group()
      this.processItem(itemData, mesh, parentMesh);
    }


  }

  updateItem(new_item: ItemData, parentMesh: any) {
    //console.log("updateItem",new_item, parentMesh);

    let old_item = this.allItems[new_item.id];
    if (!old_item) {
      this.createItem(new_item, parentMesh);
      return;
    }

    old_item.markForDelete = false;

    //update props
    old_item.clickActions = new_item.clickActions;
    old_item.visible = new_item.visible;
    old_item.hoverActions = new_item.hoverActions;
    old_item.attributes = new_item.attributes;

    // colour the selected piece / move-target markers from their attributes
    this.refreshItemHighlight(old_item);

    // update position/scale/rotation/actions....
    this.updateItemPosition(old_item, V3.FromJson(new_item.position));
    this.updateItemScale(old_item, V3.FromJson(new_item.scale))
    this.updateItemRotation(old_item, V3.FromJson(new_item.rotation))

    this.updateItemText(old_item, new_item.text)

    //update all child items
    forEach(new_item.items, new_item => {
      this.updateItem(new_item, old_item.mesh);
    });


    this.handleItemClickActions(old_item);

    this.handleItemVisibility(old_item);
  }

  updateGame(new_game: GameData) {
    //console.log("updateGame",new_game);

    //mark all items to delete - and each item that updated - will be mrked "not"
    forEach(this.allItems, (item, key) => {
      item.markForDelete = true;
    });

    this.updateItem(new_game.table, null);

    forEach(this.allItems, (item, key) => {
      if (item.markForDelete) {
        this.removeAction(item);

        if (item.playType) {
          (item.mesh!.children[0] as THREE.PositionalAudio).stop()
        }
        item.mesh?.parent?.remove(item.mesh);
        delete this.allItems[item.id];
      }
    });

    //players - move / add / remove

  }



  updateItemPosition(item: ItemData, position: V3) {
    //console.log("updateItemPosition", item, position);

    item.position = position;
    // Set directly (was a TWEEN.Tween that stopped advancing after the tween.js v25 bump,
    // so pieces updated in data but never moved on screen). Direct set is reliable.
    item.mesh!.position.set(position.x, position.y, position.z);

  }

  updateItemScale(item: ItemData, scale: V3) {
    //console.log("updateItemScale", item, scale);

    item.scale = scale;
    item.mesh!.scale.set(scale.x, scale.y, scale.z);

  }

  updateItemRotation(item: ItemData, rot: V3) {
    //console.log("updateItemRotation", item, rot);

    item.rotation = rot;

    let r = {
      x: MathUtils.degToRad(rot.x),
      y: MathUtils.degToRad(rot.y),
      z: MathUtils.degToRad(rot.z)
    }

    item.mesh!.rotation.set(r.x, r.y, r.z);

  }

  updateItemText(item: ItemData, text?: string) {
    if (item.asset == "TEXTBLOCK") {
      item.text = text;
      (item.mesh! as any).childrenTexts[0].set({content: text});
    }
  }


  processItem(itemData: ItemData, mesh: THREE.Object3D, parentMesh: THREE.Object3D | null) {
    //console.log("processItem",itemData,mesh,parentMesh);

    // // Add skeleton.
    // let skeletonHelper = new THREE.SkeletonHelper( mesh );
    // this.mgThree.scene.add( skeletonHelper );

    // let group = new THREE.Group();
    // group.add(mesh);

    mesh.name = itemData.asset || itemData.name || "";

    // position
    mesh.position.set(itemData.position.x, itemData.position.y, itemData.position.z);

    // rotation
    mesh.rotation.set(
      MathUtils.degToRad(itemData.rotation.x),
      MathUtils.degToRad(itemData.rotation.y),
      MathUtils.degToRad(itemData.rotation.z));

    //scale
    mesh.scale.set(itemData.scale.x, itemData.scale.y, itemData.scale.z);


    if (parentMesh) {
      parentMesh.add(mesh);
    } else {
      this.mgThree.scene.add(mesh);
    }

    // Add a box helper
    let boxHelper = new THREE.BoxHelper(mesh, new THREE.Color(0xFF0000));
    boxHelper.visible = this.showDebugBoxes;   // only visible when DEBUG is on
    this.boxHelpers.push(boxHelper);
    this.mgThree.scene.add(boxHelper);


    mesh.userData['ItemData'] = itemData;
    itemData.mesh = mesh

    // Cast & receive shadows so pieces separate visually (contact + inter-piece shadows).
    mesh.traverse((o: any) => {
      if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; }
    });

    // Bake in an optional base colour tint (e.g. warm ivory for white chess pieces)
    // BEFORE any highlight runs, so highlight revert returns to the tinted colour.
    this.applyBaseTint(itemData);

    forEach(itemData.items, (itemData: ItemData) => {
      this.createItem(itemData, mesh);
    });

    // ClickActions
    this.handleItemClickActions(itemData);

    // visibility
    this.handleItemVisibility(itemData);

    // colour newly-created items (e.g. yellow move markers) from their attributes
    this.refreshItemHighlight(itemData);

    this.allItems[itemData.id] = itemData;
  }

  handleItemClickActions(itemData: ItemData) {
    //console.log("handleItemClickActions",itemData);
    let action = null;
    if (this.playerData) {
      action = itemData.clickActions[this.playerData.id] || itemData.clickActions[''];
    }
    if (action) {
      this.addClickAction(itemData, action);
    } else {
      this.removeAction(itemData);
    }

  }

  handleItemVisibility(itemData: ItemData) {
    //console.log("handleItemVisibility",itemData);
    let isVisible: boolean = keys(itemData.visible).length == 0;
    if (this.playerData) {
      isVisible = isVisible || itemData.visible[this.playerData.id] == true;
    }
    //console.log("handleItemVisibility","isVisible",isVisible);
    itemData.mesh!.visible = isVisible;

    if (!isVisible) {
      this.removeAction(itemData);
    }
  }




  MeshClickFunc(event: any) {
    // console.log(event.point);
    // const direction = new THREE.Vector3();
    // direction.subVectors( event.target.position, event.point ) ;
    // console.log(direction);

    if (this.playerData) {

      let action = event.target.userData.ItemData.clickActions[this.playerData.id] || event.target.userData.ItemData.clickActions[''];
      SignalrService.singletone.executeAction(
        this.gameData.id,
        this.playerData.id,
        event.target.userData.ItemData.id,
        action,
        '',
        event.point);
    }
  }

  MeshMouseOverFunc(event: any) {
    // event.target.userData['c'] = event.target.material.clone().color;
    // event.target.material.color.set(0xff0000);
    document.body.style.cursor = 'pointer';
    this.mgThree.orbitControls.enabled = false;
  }

  MeshMouseOutFunc(event: any) {
    // let c: any = event.target.userData['c'];
    // event.target.material.color.set(c.r, c.g, c.b);
    document.body.style.cursor = 'default';
    this.mgThree.orbitControls.enabled = true;
  }

  onMeshClickFunc = this.MeshClickFunc.bind(this);
  onMeshMouseOverFunc = this.MeshMouseOverFunc.bind(this);
  onMeshMouseOutFunc = this.MeshMouseOutFunc.bind(this);

  addClickAction(itemData: ItemData, action: string) {
    //console.log("addClickAction", itemData ,action);

    this.removeAction(itemData);

    (itemData.mesh as any).addEventListener('click', this.onMeshClickFunc);

    (itemData.mesh as any).addEventListener('mouseover', this.onMeshMouseOverFunc);

    (itemData.mesh as any).addEventListener('mouseout', this.onMeshMouseOutFunc);

    this.mgThree.interactionManager.add(itemData.mesh!);
  }

  // Tint every material in an item's mesh to `hex` (sets base colour + emissive so it's
  // clearly visible regardless of the model's material); null reverts to the original.
  applyEmissive(item: ItemData, hex: number | null) {
    if (!item.mesh) return;
    item.mesh.traverse((o: any) => {
      if (o.isMesh && o.material) {
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach((m: any) => {
          if (!m) return;
          if (hex != null) {
            if (!o.userData.hl) {
              o.userData.hl = {
                color: m.color ? m.color.clone() : null,
                emissive: m.emissive ? m.emissive.clone() : null
              };
            }
            if (m.color) m.color.setHex(hex);
            if (m.emissive) m.emissive.setHex(hex);
            m.needsUpdate = true;
          } else if (o.userData.hl) {
            if (m.color && o.userData.hl.color) m.color.copy(o.userData.hl.color);
            if (m.emissive && o.userData.hl.emissive) m.emissive.copy(o.userData.hl.emissive);
            o.userData.hl = null;
            m.needsUpdate = true;
          }
        });
      }
    });
  }

  // Permanently recolour an item's model to its `tint` attribute (a hex string like
  // "0xE3D5B8"). Sets the base colour only (no glow); applied once at creation.
  applyBaseTint(item: ItemData) {
    const a = item.attributes || {};
    if (!a['tint'] || !item.mesh) return;
    const hex = parseInt(a['tint']);
    if (isNaN(hex)) return;
    item.mesh.traverse((o: any) => {
      if (o.isMesh && o.material) {
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach((m: any) => {
          if (m && m.color) { m.color.setHex(hex); m.needsUpdate = true; }
        });
      }
    });
  }

  // Colour an item from its attributes: selected piece = bright green, move-target = bright yellow.
  refreshItemHighlight(item: ItemData) {
    const a = item.attributes || {};
    if (a['selected'] == '1') this.applyEmissive(item, 0x33ff44);
    else if (a['moveMarker'] || a['captureTarget'] == '1') this.applyEmissive(item, 0xffe000);
    else this.applyEmissive(item, null);
  }

  removeAction(itemData: ItemData) {
    //console.log("removeAction",itemData);
    (itemData.mesh as any).removeEventListener('click', this.onMeshClickFunc);
    (itemData.mesh as any).removeEventListener('click', this.onMeshMouseOverFunc);
    this.mgThree.interactionManager.remove(itemData.mesh!);
  }
}
