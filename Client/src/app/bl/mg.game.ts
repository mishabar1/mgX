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

  // per-player hand/table anchor meshes, so updateGame can refresh those zones live
  handMeshes: { [id: string]: THREE.Object3D } = {};
  tableMeshes: { [id: string]: THREE.Object3D } = {};
  // floating name labels + a "DEFENDING" badge above each opponent's head
  nameSprites: { [id: string]: THREE.Sprite } = {};
  defendSprites: { [id: string]: THREE.Sprite } = {};

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
    // Read the "show heads" preference from the saved game state (default shown).
    this.showHeads = this.gameData.attributes?.['showHeads'] !== '0';

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

      // Unfilled seats (Durak has up to 6, most optional) get no avatar/zones in the scene.
      if (playerData.type === 'EMPTY_SEAT') return;

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

      // table anchor for the player's table items — an empty container (no geometry, renders
      // nothing). The card items parented to it still show.
      const playerTable = new Group();
      playerTable.name = "PLAYER TABLE";
      playerData.avatar.mesh?.add(playerTable);
      playerTable.position.set(0,-1.5,1.5);

      this.tableMeshes[playerData.id] = playerTable;
      this.createItem(playerData.table,playerTable);

      // hand anchor for the player's hand items — empty container (see note above).
      const playerHand = new Group();
      playerHand.name = "PLAYER HAND";
      playerData.avatar.mesh?.add(playerHand);
      playerHand.rotation.x = -Math.PI / 2;
      playerHand.position.set(0,0,1.5);

      this.handMeshes[playerData.id] = playerHand;
      this.createItem(playerData.hand, playerHand);

      // Floating name label + a DEFENDING badge above each OTHER player's head (you know your
      // own seat, and you get the "YOU DEFENDING" status line instead).
      if (!(this.playerData && this.playerData.id === playerData.id)) {
        const disp = playerData.user?.name || playerData.name || (playerData.type === 'AI' ? 'AI' : 'open');
        const nameSpr = this.makeTextSprite(disp, 'rgba(18,28,38,0.75)', '#ffffff');
        nameSpr.position.set(0, 3.3, 0);
        playerData.avatar.mesh!.add(nameSpr);
        this.nameSprites[playerData.id] = nameSpr;

        const defSpr = this.makeTextSprite('DEFENDING', 'rgba(210,32,42,0.92)', '#ffffff');
        defSpr.position.set(0, 2.5, 0);
        defSpr.visible = false;
        playerData.avatar.mesh!.add(defSpr);
        this.defendSprites[playerData.id] = defSpr;
      }



    });

    this.refreshDefenderBadges();
  }

  // Build a billboard text label (canvas texture) that always faces the camera.
  makeTextSprite(text: string, bg: string, fg: string): THREE.Sprite {
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d')!;
    const fontSize = 52;
    const font = `bold ${fontSize}px Arial, sans-serif`;
    ctx.font = font;
    const textW = Math.max(40, ctx.measureText(text || ' ').width);
    const pad = 26;
    canvas.width = Math.ceil(textW + pad * 2);
    canvas.height = Math.ceil(fontSize + pad * 2);
    ctx.font = font;                       // re-set (resizing the canvas clears state)
    ctx.fillStyle = bg;
    ctx.beginPath();
    if ((ctx as any).roundRect) (ctx as any).roundRect(0, 0, canvas.width, canvas.height, 18);
    else ctx.rect(0, 0, canvas.width, canvas.height);
    ctx.fill();
    ctx.fillStyle = fg;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, canvas.width / 2, canvas.height / 2);

    const tex = new THREE.CanvasTexture(canvas);
    tex.needsUpdate = true;
    const mat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: false, depthWrite: false });
    const spr = new THREE.Sprite(mat);
    const h = 0.9;
    spr.scale.set(h * (canvas.width / canvas.height), h, 1);
    return spr;
  }

  // Show the DEFENDING badge over whoever is currently defending (hide once the game is over).
  refreshDefenderBadges() {
    const def = this.gameData?.attributes?.['defender'];
    const over = this.gameData?.attributes?.['over'];
    forEach(this.defendSprites, (spr: THREE.Sprite, id: string) => {
      spr.visible = !over && !!def && id === def;
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

        this.mgThree.getTexture(frontURL, (frontTexture: any) => {
          // console.log( frontTexture.image.width, frontTexture.image.height );
          let aspect = frontTexture.image.width / frontTexture.image.height;
          let x = 1;
          let z = 1 / aspect;
          if (aspect < 1) {
            z = 1;
            x = aspect;
          }

          const backTexture = this.mgThree.getTexture(backURL);

          // Owner-only faces: if a card carries an "owner" attribute and I'm not that owner,
          // draw the BACK on the visible (top) face too — so opponents only ever see the back,
          // from any camera angle, with a single card item.
          const ownerId = itemData.attributes?.['owner'];
          const amOwner = !ownerId || (this.playerData && this.playerData.id === ownerId);
          const topTexture = amOwner ? frontTexture : backTexture;

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
              // top — alphaTest discards fully-transparent pixels so PNGs with an alpha
              // channel (e.g. the suit symbols) show the felt through, not a black square.
              map: topTexture, transparent: true, alphaTest: 0.5
            }),
            new THREE.MeshBasicMaterial({
              // bottom
              map: backTexture, transparent: true, alphaTest: 0.5
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
          // Colour from an optional "textColor" attribute (hex string, e.g. "ffffff");
          // defaults to red so existing text (chess "CHECK!") is unchanged.
          const colorAttr = itemData.attributes?.['textColor'];
          const textColor = colorAttr ? parseInt(colorAttr, 16) : 0xff0000;
          var textMaterial = new THREE.MeshPhongMaterial(
            {color: textColor, specular: 0xffffff}
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

      if (assetType == "CYLINDER") {
        // Procedural round disc (a flat cylinder lying on its circular face). Used for
        // Reversi discs & move markers; the per-item "tint" attribute recolours it via
        // applyBaseTint(). Unit size is diameter 1, thickness 0.16 — the item's scale
        // (and the asset scale) size it to the board.
        const geometry = new THREE.CylinderGeometry(0.5, 0.5, 0.16, 48);
        const material = new THREE.MeshStandardMaterial({color: 0xffffff, metalness: 0.0, roughness: 0.85});
        const mesh = new THREE.Mesh(geometry, material);
        let g = new Group();
        g.add(mesh);
        g.scale.set(asset.scale.x, asset.scale.y, asset.scale.z);
        this.processItem(itemData, g, parentMesh);
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

    // keep the asset dictionary current — new cards drawn during play add assets, and the
    // client resolves item.asset against this map (otherwise "unknown asset key").
    if (new_game.assets) this.gameData.assets = new_game.assets;

    // keep game attributes current (defender/turn/over) so the DEFENDING badge tracks play.
    if (new_game.attributes) this.gameData.attributes = new_game.attributes;
    this.refreshDefenderBadges();

    this.updateItem(new_game.table, null);

    // also refresh each player's hand & table zones (cards move in/out of these during play)
    forEach(new_game.players, (p: PlayerData) => {
      if (p.hand && this.handMeshes[p.id]) this.updateItem(p.hand, this.handMeshes[p.id]);
      if (p.table && this.tableMeshes[p.id]) this.updateItem(p.table, this.tableMeshes[p.id]);
    });

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
          if (!m) return;
          if (m.color) m.color.setHex(hex);
          // Normalize to a consistent MATTE finish. Some models (e.g. the black pawn)
          // ship glossy/metallic, so once tinted dark their white specular highlights
          // stand out as "strange glossy". Kill metalness and roughen the surface.
          if ('metalness' in m) m.metalness = 0.0;
          if ('roughness' in m) m.roughness = 0.9;
          if (m.metalnessMap) m.metalnessMap = null;   // maps would override the scalars
          if (m.roughnessMap) m.roughnessMap = null;
          if ('shininess' in m) m.shininess = 0;       // (Phong materials)
          if (m.specular) m.specular.setHex(0x000000);
          m.needsUpdate = true;
        });
      }
    });
  }

  // Colour an item from its attributes: selected piece = bright green, checked king = red,
  // move-target = bright yellow.
  refreshItemHighlight(item: ItemData) {
    const a = item.attributes || {};
    // The "playable" hint is private: only show it on cards the viewer OWNS, otherwise an
    // opponent's face-down cards would glow and leak which cards they can play.
    const owner = a['owner'];
    const mine = !owner || (this.playerData && this.playerData.id === owner);
    if (a['selected'] == '1') this.applyEmissive(item, 0x33ff44);
    else if (a['check'] == '1') this.applyEmissive(item, 0xEE2222);
    else if (a['playable'] == '1' && mine) this.applyEmissive(item, 0x8cff8c);   // a card YOU can play now
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
